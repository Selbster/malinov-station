using Content.Server.CrewManifest;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.CCVar;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.PDA;
using Content.Shared.Radio.Components;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Server-side logic for the Malinov Messenger cartridge.
///     The messenger account is tied to the inserted ID card, not to the PDA itself.
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

        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeAddedEvent>(OnCartridgeAdded);
        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeRemovedEvent>(OnCartridgeRemoved);
        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeActivatedEvent>(OnActivated);
        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeDeactivatedEvent>(OnDeactivated);
        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeRelayedEvent<DeviceNetworkPacketEvent>>(OnPacketRelayed);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var now = _timing.CurTime;
        var query = EntityQueryEnumerator<MalinovMessengerCartridgeSessionComponent, MalinovMessengerCartridgeComponent>();

        while (query.MoveNext(out var uid, out var session, out _))
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

            var loaderUid = Transform(uid).ParentUid;
            if (loaderUid == EntityUid.Invalid)
                continue;

            // Keep the account link in sync with whichever ID card is currently inside the PDA.
            if (TryComp<PdaComponent>(loaderUid, out var pda))
            {
                var currentId = pda.ContainedId;
                if (currentId != session.LinkedAccount)
                {
                    if (session.LinkedAccount != null)
                    {
                        UnlinkAccount(session);
                        DisableNetworking(loaderUid);
                    }

                    if (currentId != null && HasComp<IdCardComponent>(currentId.Value))
                    {
                        LinkAccount(loaderUid, session, currentId.Value);
                        EnableNetworking(loaderUid, session);
                    }

                    UpdateUiState(uid, loaderUid, session: session);
                }
            }

            if (session.LinkedAccount is { } idUid && TryComp<IdCardComponent>(idUid, out var idCard))
            {
                var cardName = idCard.FullName;
                if (session.IdentityName != cardName)
                {
                    session.IdentityName = cardName;

                    if (!string.IsNullOrWhiteSpace(cardName) && GetServerAddress(loaderUid, session) != null)
                    {
                        AnnouncePresence(loaderUid, session);
                        RequestDirectory(loaderUid, session);
                        session.LastAnnounceTime = now;
                        session.LastDirectoryRequestTime = now;
                    }

                    UpdateUiState(uid, loaderUid, session: session);
                }
            }

            if (string.IsNullOrWhiteSpace(session.IdentityName))
                continue;

            var serverAddress = GetServerAddress(loaderUid, session);
            if (serverAddress == null)
                continue;

            var announceInterval = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.MalinovMessengerPeerTtlSeconds) / 2.0);
            if (now - session.LastAnnounceTime >= announceInterval)
            {
                AnnouncePresence(loaderUid, session);
                session.LastAnnounceTime = now;
            }

            if (now - session.LastDirectoryRequestTime >= announceInterval)
            {
                RequestDirectory(loaderUid, session);
                session.LastDirectoryRequestTime = now;
            }
        }
    }

    #region Cartridge lifecycle

    private void OnCartridgeAdded(Entity<MalinovMessengerCartridgeComponent> ent, ref CartridgeAddedEvent args)
    {
        var loaderUid = args.Loader.Owner;
        var session = EnsureComp<MalinovMessengerCartridgeSessionComponent>(ent);

        if (TryComp<PdaComponent>(loaderUid, out var pda) && pda.ContainedId is { } idUid)
        {
            LinkAccount(loaderUid, session, idUid);
        }

        if (!string.IsNullOrWhiteSpace(session.IdentityName))
        {
            EnableNetworking(loaderUid, session);
        }

        UpdateUiState(ent, loaderUid, session: session);
    }

    private void OnCartridgeRemoved(Entity<MalinovMessengerCartridgeComponent> ent, ref CartridgeRemovedEvent args)
    {
        var loaderUid = args.Loader.Owner;

        DisableNetworking(loaderUid);
        RemComp<MalinovMessengerCartridgeSessionComponent>(ent);
    }

    private void OnActivated(Entity<MalinovMessengerCartridgeComponent> ent, ref CartridgeActivatedEvent args)
    {
        var loaderUid = args.Loader.Owner;
        var session = EnsureComp<MalinovMessengerCartridgeSessionComponent>(ent);
        session.LastError = null;
        UpdateUiState(ent, loaderUid, session: session);
    }

    private void OnDeactivated(Entity<MalinovMessengerCartridgeComponent> ent, ref CartridgeDeactivatedEvent args)
    {
        // Intentionally left blank: the messenger stays connected in the background
        // so it can receive messages even when the user switches to another program.
    }

    #endregion

    #region ID card linking

    private void LinkAccount(EntityUid loaderUid, MalinovMessengerCartridgeSessionComponent session, EntityUid idUid)
    {
        if (!TryComp<IdCardComponent>(idUid, out var idCard))
            return;

        EnsureComp<MalinovMessengerAccountComponent>(idUid);
        session.LinkedAccount = idUid;
        session.IdentityName = idCard.FullName;
    }

    private void UnlinkAccount(MalinovMessengerCartridgeSessionComponent session)
    {
        session.LinkedAccount = null;
        session.IdentityName = null;
        session.SelectedContact = null;
    }

    #endregion

    #region Networking

    private void EnableNetworking(EntityUid loaderUid, MalinovMessengerCartridgeSessionComponent session)
    {
        _deviceNetwork.ConnectDevice(loaderUid);
        _deviceNetwork.SetReceiveFrequency(loaderUid, MalinovMessengerConstants.Frequency);
        _deviceNetwork.SetTransmitFrequency(loaderUid, MalinovMessengerConstants.Frequency);

        GetServerAddress(loaderUid, session);
        if (session.ServerAddress == null || string.IsNullOrWhiteSpace(session.IdentityName))
            return;

        AnnouncePresence(loaderUid, session);
        RequestDirectory(loaderUid, session);
        session.LastAnnounceTime = _timing.CurTime;
        session.LastDirectoryRequestTime = _timing.CurTime;
    }

    private void DisableNetworking(EntityUid loaderUid)
    {
        _deviceNetwork.SetReceiveFrequency(loaderUid, null);
        _deviceNetwork.SetTransmitFrequency(loaderUid, null);
        _deviceNetwork.DisconnectDevice(loaderUid, null);
    }

    private void AnnouncePresence(EntityUid loaderUid, MalinovMessengerCartridgeSessionComponent session)
    {
        if (string.IsNullOrWhiteSpace(session.IdentityName))
            return;

        var serverAddress = GetServerAddress(loaderUid, session);
        if (serverAddress == null)
            return;

        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandAnnounce,
            [MalinovMessengerConstants.SenderNameKey] = session.IdentityName,
        };

        _deviceNetwork.QueuePacket(loaderUid, serverAddress, payload, MalinovMessengerConstants.Frequency);
    }

    private void RequestDirectory(EntityUid loaderUid, MalinovMessengerCartridgeSessionComponent session)
    {
        var serverAddress = GetServerAddress(loaderUid, session);
        if (serverAddress == null)
            return;

        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandDirectoryRequest,
        };

        _deviceNetwork.QueuePacket(loaderUid, serverAddress, payload, MalinovMessengerConstants.Frequency);
    }

    private string? GetServerAddress(EntityUid loaderUid, MalinovMessengerCartridgeSessionComponent session)
    {
        var mapId = Transform(loaderUid).MapID;
        var query = EntityQueryEnumerator<TelecomServerComponent, ApcPowerReceiverComponent, DeviceNetworkComponent, TransformComponent>();

        while (query.MoveNext(out _, out _, out var power, out var device, out var transform))
        {
            if (transform.MapID != mapId || !power.Powered)
                continue;

            session.ServerAvailable = true;
            session.ServerAddress = device.Address;
            return device.Address;
        }

        session.ServerAvailable = false;
        session.ServerAddress = null;
        return null;
    }

    #endregion

    #region UI messages

    private void OnUiReady(Entity<MalinovMessengerCartridgeComponent> ent, ref CartridgeUiReadyEvent args)
    {
        var session = EnsureComp<MalinovMessengerCartridgeSessionComponent>(ent);
        UpdateUiState(ent, args.Loader.Owner, session: session);
    }

    private void OnUiMessage(Entity<MalinovMessengerCartridgeComponent> ent, ref CartridgeMessageEvent args)
    {
        if (args is not MalinovMessengerUiMessageEvent msg)
            return;

        var loaderUid = GetEntity(msg.LoaderUid);
        var session = EnsureComp<MalinovMessengerCartridgeSessionComponent>(ent);

        switch (msg.Action)
        {
            case MalinovMessengerUiAction.RefreshContacts:
                session.SelectedContact = msg.TargetName;
                session.LastError = null;
                UpdateUiState(ent, loaderUid, session: session);
                break;

            case MalinovMessengerUiAction.Send:
                if (!string.IsNullOrWhiteSpace(msg.TargetName) && !string.IsNullOrWhiteSpace(msg.Text))
                    SendMessage(ent, ent.Comp, session, loaderUid, msg.TargetName, msg.Text);
                break;
        }
    }

    /// <summary>
    ///     Sends a direct message from the given cartridge to the named contact.
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
        if (string.IsNullOrWhiteSpace(session.IdentityName))
        {
            SetSendError(uid, loaderUid, component, session, "malinov-messenger-no-idcard");
            return;
        }

        var serverAddress = GetServerAddress(loaderUid, session);
        if (serverAddress == null)
        {
            SetSendError(uid, loaderUid, component, session, "malinov-messenger-server-unavailable");
            return;
        }

        var now = _timing.CurTime;
        if (!session.Peers.TryGetValue(targetName, out var peer) || peer.Expiry < now)
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
            [MalinovMessengerConstants.SenderNameKey] = session.IdentityName,
            [MalinovMessengerConstants.TargetKey] = targetName,
            [MalinovMessengerConstants.TextKey] = text,
        };

        _deviceNetwork.QueuePacket(loaderUid, serverAddress, payload, MalinovMessengerConstants.Frequency);

        var message = new MalinovMessengerMessage(session.IdentityName, text, _timing.CurTime, outgoing: true);
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
        Entity<MalinovMessengerCartridgeComponent> ent,
        ref CartridgeRelayedEvent<DeviceNetworkPacketEvent> args)
    {
        var packet = args.Args;

        if (packet.Frequency != MalinovMessengerConstants.Frequency)
            return;

        if (!packet.Data.TryGetValue(MalinovMessengerConstants.CommandKey, out var cmdObj) || cmdObj is not string cmd)
            return;

        var session = EnsureComp<MalinovMessengerCartridgeSessionComponent>(ent);
        var loaderUid = args.Loader.Owner;

        switch (cmd)
        {
            case MalinovMessengerConstants.CommandAnnounce:
                HandleAnnounce(session, packet);
                UpdateUiState(ent, loaderUid, session: session);
                break;

            case MalinovMessengerConstants.CommandDirectory:
                HandleDirectory(ent, session, packet);
                UpdateUiState(ent, loaderUid, session: session);
                break;

            case MalinovMessengerConstants.CommandMessage:
                HandleMessage(ent, loaderUid, ent.Comp, session, packet);
                UpdateUiState(ent, loaderUid, session: session);
                break;
        }
    }

    private void HandleAnnounce(MalinovMessengerCartridgeSessionComponent session, DeviceNetworkPacketEvent packet)
    {
        if (!packet.Data.TryGetValue(MalinovMessengerConstants.SenderNameKey, out var nameObj) || nameObj is not string name)
            return;

        if (string.IsNullOrWhiteSpace(name))
            return;

        var ttl = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.MalinovMessengerPeerTtlSeconds));
        session.Peers[name] = new PeerCache(packet.SenderAddress, _timing.CurTime + ttl);
    }

    private void HandleMessage(
        EntityUid uid,
        EntityUid loaderUid,
        MalinovMessengerCartridgeComponent component,
        MalinovMessengerCartridgeSessionComponent session,
        DeviceNetworkPacketEvent packet)
    {
        if (!packet.Data.TryGetValue(MalinovMessengerConstants.SenderNameKey, out var senderObj) || senderObj is not string sender)
            return;

        if (!packet.Data.TryGetValue(MalinovMessengerConstants.TextKey, out var textObj) || textObj is not string text)
            return;

        // Server-side delivery error: empty sender name marks a relay-level failure.
        if (string.IsNullOrEmpty(sender))
        {
            SetSendError(uid, loaderUid, component, session, text);
            return;
        }

        var message = new MalinovMessengerMessage(sender, text, _timing.CurTime, outgoing: false);
        AddSessionMessage(session, sender, message);

        // If the user has not selected a contact yet, automatically open the conversation with the sender.
        session.SelectedContact ??= sender;
    }

    private void HandleDirectory(EntityUid uid, MalinovMessengerCartridgeSessionComponent session, DeviceNetworkPacketEvent packet)
    {
        if (!packet.Data.TryGetValue(MalinovMessengerConstants.EntriesKey, out var entriesObj) || entriesObj is not Dictionary<string, string> entries)
            return;

        var ttl = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.MalinovMessengerPeerTtlSeconds));
        var expiry = _timing.CurTime + ttl;

        foreach (var (name, address) in entries)
        {
            if (string.IsNullOrWhiteSpace(name))
                continue;

            session.Peers[name] = new PeerCache(address, expiry);
        }
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

        GetServerAddress(loaderUid, session);
        var serverStatus = session.ServerAvailable
            ? "malinov-messenger-server-online"
            : "malinov-messenger-server-unavailable";

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
        var sessions = GetSessionDictionary(session);
        var messages = session.SelectedContact != null && sessions.TryGetValue(session.SelectedContact, out var lines)
            ? lines
            : new List<MalinovMessengerMessage>();

        var state = new MalinovMessengerUiState(contacts, status, serverStatus, session.SelectedContact, messages, onlineNames);
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }

    private Dictionary<string, List<MalinovMessengerMessage>> GetSessionDictionary(MalinovMessengerCartridgeSessionComponent session)
    {
        if (session.LinkedAccount is { } accountUid
            && TryComp<MalinovMessengerAccountComponent>(accountUid, out var account))
        {
            return account.Sessions;
        }

        return session.LocalSessions;
    }

    private void AddSessionMessage(MalinovMessengerCartridgeSessionComponent session, string contactName, MalinovMessengerMessage message)
    {
        var sessions = GetSessionDictionary(session);

        if (!sessions.TryGetValue(contactName, out var history))
        {
            history = new List<MalinovMessengerMessage>();
            sessions[contactName] = history;
        }

        history.Add(message);

        var limit = _cfg.GetCVar(CCVars.MalinovMessengerHistoryPerContact);
        while (history.Count > limit && history.Count > 0)
            history.RemoveAt(0);
    }

    #endregion
}
