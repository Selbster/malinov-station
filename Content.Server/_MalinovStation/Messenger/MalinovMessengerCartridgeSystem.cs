using Content.Server.CrewManifest;
using Content.Server.DeviceNetwork.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Station.Systems;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.CartridgeLoader;
using Content.Shared.CCVar;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.PDA;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Server-side logic for the Malinov Messenger cartridge.
///     Builds the contact catalog from the owning station's crew manifest and handles P2P messaging.
/// </summary>
public sealed partial class MalinovMessengerCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private CrewManifestSystem _crewManifest = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeActivatedEvent>(OnActivated);
        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeRelayedEvent<DeviceNetworkPacketEvent>>(OnPacketRelayed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MalinovMessengerCartridgeSessionComponent>();

        while (query.MoveNext(out _, out var session))
        {
            var expired = new List<string>();

            foreach (var (name, peer) in session.Peers)
            {
                if (peer.Expiry < now)
                    expired.Add(name);
            }

            foreach (var name in expired)
            {
                session.Peers.Remove(name);
            }
        }
    }

    #region Activation / Deactivation

    private void OnActivated(EntityUid uid, MalinovMessengerCartridgeComponent component, ref CartridgeActivatedEvent args)
    {
        var loaderUid = args.Loader.Owner;
        var session = EnsureComp<MalinovMessengerCartridgeSessionComponent>(uid);

        _deviceNetwork.ConnectDevice(loaderUid);
        _deviceNetwork.SetReceiveFrequency(loaderUid, MalinovMessengerConstants.Frequency);
        _deviceNetwork.SetTransmitFrequency(loaderUid, MalinovMessengerConstants.Frequency);

        AnnouncePresence(loaderUid);
        UpdateUiState(uid, loaderUid, component, session);
    }

    private void OnDeactivated(EntityUid uid, MalinovMessengerCartridgeComponent component, ref CartridgeDeactivatedEvent args)
    {
        var loaderUid = args.Loader.Owner;

        _deviceNetwork.SetReceiveFrequency(loaderUid, null);
        _deviceNetwork.SetTransmitFrequency(loaderUid, null);
    }

    private void AnnouncePresence(EntityUid loaderUid)
    {
        if (!TryComp<PdaComponent>(loaderUid, out var pda) || string.IsNullOrWhiteSpace(pda.OwnerName))
            return;

        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandAnnounce,
            [MalinovMessengerConstants.SenderNameKey] = pda.OwnerName,
        };

        _deviceNetwork.QueuePacket(loaderUid, null, payload, MalinovMessengerConstants.Frequency);
    }

    #endregion

    #region UI messages

    private void OnUiReady(EntityUid uid, MalinovMessengerCartridgeComponent component, ref CartridgeUiReadyEvent args)
    {
        var session = EnsureComp<MalinovMessengerCartridgeSessionComponent>(uid);
        UpdateUiState(uid, args.Loader.Owner, component, session);
    }

    private void OnUiMessage(EntityUid uid, MalinovMessengerCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not MalinovMessengerUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(msg.LoaderUid);
        var session = EnsureComp<MalinovMessengerCartridgeSessionComponent>(uid);

        switch (msg.Action)
        {
            case MalinovMessengerUiAction.RefreshContacts:
                session.SelectedContact = msg.TargetName;
                session.LastError = null;
                UpdateUiState(uid, loaderUid, component, session);
                break;

            case MalinovMessengerUiAction.Send:
                if (!string.IsNullOrWhiteSpace(msg.TargetName) && !string.IsNullOrWhiteSpace(msg.Text))
                    SendMessage(uid, component, session, loaderUid, msg.TargetName, msg.Text);
                break;
        }
    }

    /// <summary>
    ///     Sends a direct P2P message from the given cartridge to the named contact.
    ///     Returns false if the contact is unknown or unreachable; the error is stored in the session.
    /// </summary>
    public bool TrySendMessage(EntityUid programUid, string targetName, string text)
    {
        if (!TryComp<MalinovMessengerCartridgeComponent>(programUid, out var component))
            return false;

        var session = EnsureComp<MalinovMessengerCartridgeSessionComponent>(programUid);
        var loaderUid = Transform(programUid).ParentUid;

        SendMessage(programUid, component, session, loaderUid, targetName, text);
        return session.LastError == null;
    }

    private void SendMessage(
        EntityUid uid,
        MalinovMessengerCartridgeComponent component,
        MalinovMessengerCartridgeSessionComponent session,
        EntityUid loaderUid,
        string targetName,
        string text)
    {
        if (!TryComp<PdaComponent>(loaderUid, out var pda) || string.IsNullOrWhiteSpace(pda.OwnerName))
        {
            SetSendError(uid, loaderUid, component, session, "malinov-messenger-error-offline");
            return;
        }

        var now = _timing.CurTime;
        if (!session.Peers.TryGetValue(targetName, out var peer) || peer.Expiry < now)
        {
            SetSendError(uid, loaderUid, component, session, "malinov-messenger-error-offline");
            return;
        }

        if (!IsPeerReachable(loaderUid, peer.Address))
        {
            SetSendError(uid, loaderUid, component, session, "malinov-messenger-error-offline");
            return;
        }

        var maxLength = _cfg.GetCVar(CCVars.MalinovMessengerMaxMessageLength);
        if (text.Length > maxLength)
            text = text[..maxLength];

        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandMessage,
            [MalinovMessengerConstants.SenderNameKey] = pda.OwnerName,
            [MalinovMessengerConstants.TextKey] = text,
        };

        _deviceNetwork.QueuePacket(loaderUid, peer.Address, payload, MalinovMessengerConstants.Frequency);

        var message = new MalinovMessengerMessage(pda.OwnerName, text, _timing.CurTime, outgoing: true);
        AddSessionMessage(session, targetName, message);

        session.LastError = null;
        UpdateUiState(uid, loaderUid, component, session);
    }

    private void SetSendError(
        EntityUid uid,
        EntityUid loaderUid,
        MalinovMessengerCartridgeComponent component,
        MalinovMessengerCartridgeSessionComponent session,
        string errorKey)
    {
        session.LastError = errorKey;
        UpdateUiState(uid, loaderUid, component, session);
    }

    private bool IsPeerReachable(EntityUid loaderUid, string address)
    {
        return TryComp<DeviceNetworkComponent>(loaderUid, out var device)
               && _deviceNetwork.IsAddressPresent(device.DeviceNetId, address);
    }

    #endregion

    #region Incoming packets

    private void OnPacketRelayed(
        EntityUid uid,
        MalinovMessengerCartridgeComponent component,
        ref CartridgeRelayedEvent<DeviceNetworkPacketEvent> args)
    {
        var packet = args.Args;

        if (packet.Frequency != MalinovMessengerConstants.Frequency)
            return;

        if (!packet.Data.TryGetValue(MalinovMessengerConstants.CommandKey, out var cmdObj) || cmdObj is not string cmd)
            return;

        var session = EnsureComp<MalinovMessengerCartridgeSessionComponent>(uid);
        var loaderUid = args.Loader.Owner;

        switch (cmd)
        {
            case MalinovMessengerConstants.CommandAnnounce:
                HandleAnnounce(session, packet);
                UpdateUiState(uid, loaderUid, component, session);
                break;

            case MalinovMessengerConstants.CommandMessage:
                HandleMessage(session, packet);
                UpdateUiState(uid, loaderUid, component, session);
                break;
        }
    }

    private void HandleAnnounce(MalinovMessengerCartridgeSessionComponent session, DeviceNetworkPacketEvent packet)
    {
        if (!packet.Data.TryGetValue(MalinovMessengerConstants.SenderNameKey, out var nameObj) || nameObj is not string name)
            return;

        // TODO: matching a station record to an announce by owner name is a known simplification.
        // A future stage should use the station records key/id when a directory server is available.
        var ttl = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.MalinovMessengerPeerTtlSeconds));
        session.Peers[name] = new PeerCache(packet.SenderAddress, _timing.CurTime + ttl);
    }

    private void HandleMessage(MalinovMessengerCartridgeSessionComponent session, DeviceNetworkPacketEvent packet)
    {
        if (!packet.Data.TryGetValue(MalinovMessengerConstants.SenderNameKey, out var senderObj) || senderObj is not string sender)
            return;

        if (!packet.Data.TryGetValue(MalinovMessengerConstants.TextKey, out var textObj) || textObj is not string text)
            return;

        var message = new MalinovMessengerMessage(sender, text, _timing.CurTime, outgoing: false);
        AddSessionMessage(session, sender, message);

        // If the user has not selected a contact yet, automatically open the conversation with the sender.
        session.SelectedContact ??= sender;
    }

    #endregion

    #region UI state helpers

    private void UpdateUiState(
        EntityUid uid,
        EntityUid loaderUid,
        MalinovMessengerCartridgeComponent? component = null,
        MalinovMessengerCartridgeSessionComponent? session = null)
    {
        if (!Resolve(uid, ref component))
            return;

        session ??= EnsureComp<MalinovMessengerCartridgeSessionComponent>(uid);

        var owningStation = _stationSystem.GetOwningStation(uid);

        if (owningStation is null)
        {
            var unavailableState = new MalinovMessengerUiState(status: "malinov-messenger-manifest-unavailable");
            _cartridgeLoader.UpdateCartridgeUiState(loaderUid, unavailableState);
            return;
        }

        var (_, entries) = _crewManifest.GetCrewManifest(owningStation.Value);
        var contacts = new List<MalinovMessengerContact>();

        if (entries != null)
        {
            foreach (var entry in entries.Entries)
            {
                contacts.Add(new MalinovMessengerContact(entry.Name, entry.JobTitle));
            }
        }

        var now = _timing.CurTime;
        var onlineNames = new HashSet<string>();

        foreach (var (name, peer) in session.Peers)
        {
            if (peer.Expiry >= now)
                onlineNames.Add(name);
        }

        var status = session.LastError ?? string.Empty;
        var messages = session.SelectedContact != null && session.Sessions.TryGetValue(session.SelectedContact, out var lines)
            ? lines
            : new List<MalinovMessengerMessage>();

        var state = new MalinovMessengerUiState(contacts, status, session.SelectedContact, messages, onlineNames);
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }

    private void AddSessionMessage(MalinovMessengerCartridgeSessionComponent session, string contactName, MalinovMessengerMessage message)
    {
        if (!session.Sessions.TryGetValue(contactName, out var history))
        {
            history = new List<MalinovMessengerMessage>();
            session.Sessions[contactName] = history;
        }

        history.Add(message);

        var limit = _cfg.GetCVar(CCVars.MalinovMessengerHistoryPerContact);
        while (history.Count > limit && history.Count > 0)
            history.RemoveAt(0);
    }

    #endregion
}
