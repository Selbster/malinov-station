using Content.Server.CrewManifest;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Popups;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared._MalinovStation.Messenger;
using Robust.Server.Containers;
using Content.Shared.Access.Components;
using Content.Shared.Audio;
using Content.Shared.CartridgeLoader;
using Content.Shared.CCVar;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Events;
using Content.Shared.Popups;
using Content.Shared.PDA;
using Content.Shared.Radio.Components;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Server-side logic for the Malinov Messenger cartridge.
///     The messenger account is tied to the inserted ID card, not to the PDA itself.
/// </summary>
public sealed partial class MalinovMessengerCartridgeSystem : EntitySystem
{
    /// <summary>
    ///     Test-only hook fired after an incoming message triggers a notification.
    /// </summary>
    public event Action<EntityUid, string>? OnNotificationSent;

    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private CrewManifestSystem _crewManifest = default!;
    [Dependency] private StationSystem _stationSystem = default!;
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private ContainerSystem _containerSystem = default!;
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
        session.IsProgramActive = true;
        session.LastError = null;
        UpdateUiState(ent, loaderUid, session: session);
    }

    private void OnDeactivated(Entity<MalinovMessengerCartridgeComponent> ent, ref CartridgeDeactivatedEvent args)
    {
        var session = EnsureComp<MalinovMessengerCartridgeSessionComponent>(ent);
        session.IsProgramActive = false;

        // Intentionally left blank for networking: the messenger stays connected in the background
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

            case MalinovMessengerUiAction.CloseChat:
                session.SelectedContact = null;
                session.LastError = null;
                UpdateUiState(ent, loaderUid, session: session);
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

        if (session.IsMuted)
        {
            SetSendError(uid, loaderUid, component, session, "malinov-messenger-error-muted");
            return;
        }

        if (targetName == session.IdentityName)
        {
            SetSendError(uid, loaderUid, component, session, "malinov-messenger-error-self");
            return;
        }

        var maxLength = _cfg.GetCVar(CCVars.MalinovMessengerMaxMessageLength);
        if (text.Length > maxLength)
            text = text[..maxLength];

        // The message is appended optimistically and the relay is the delivery authority:
        // a delivery failure comes back asynchronously as an error reply and removes the
        // message back from the sender's history. This keeps every send visible in the
        // sender's own chat immediately, even while the local peer cache is still warming
        // up or under a rate-limit burst.
        session.SelectedContact = targetName;

        var messageId = ++session.NextMessageId;

        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandMessage,
            [MalinovMessengerConstants.SenderNameKey] = session.IdentityName,
            [MalinovMessengerConstants.TargetKey] = targetName,
            [MalinovMessengerConstants.TextKey] = text,
            [MalinovMessengerConstants.MessageIdKey] = messageId,
        };

        _deviceNetwork.QueuePacket(loaderUid, serverAddress, payload, MalinovMessengerConstants.Frequency);

        var message = new MalinovMessengerMessage(session.IdentityName, text, _timing.CurTime, outgoing: true, messageId);
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

        if (name == session.IdentityName)
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

        var maxLength = _cfg.GetCVar(CCVars.MalinovMessengerMaxMessageLength);
        if (text.Length > maxLength)
            text = text[..maxLength];

        // Server-side delivery error: empty sender name marks a relay-level failure.
        // The outgoing message is correlated back via its id and rolled back (removed
        // from the sender's history) because the relay is the delivery authority.
        if (string.IsNullOrEmpty(sender))
        {
            var messageId = packet.Data.TryGetValue(MalinovMessengerConstants.MessageIdKey, out var idObj) && idObj is long idValue
                ? idValue
                : 0L;

            RollbackPendingOutgoing(session, messageId);

            if (text == "malinov-messenger-error-muted")
                session.IsMuted = true;

            SetSendError(uid, loaderUid, component, session, text);
            return;
        }

        // Ignore looped-back messages addressed to the local account.
        if (sender == session.IdentityName)
            return;

        var message = new MalinovMessengerMessage(sender, text, _timing.CurTime, outgoing: false);
        AddSessionMessage(session, sender, message);

        // If the user has not selected a contact yet, automatically open the conversation with the sender.
        session.SelectedContact ??= sender;

        if (!session.IsProgramActive)
        {
            NotifyIncomingMessage(loaderUid, sender);
        }
    }

    private void NotifyIncomingMessage(EntityUid loaderUid, string sender)
    {
        if (!TryGetPdaHolder(loaderUid, out var holderUid))
            return;

        var popupMessage = Loc.GetString("malinov-messenger-notification-popup", ("sender", sender));
        _popup.PopupEntity(popupMessage, loaderUid, holderUid, PopupType.Medium);

        if (_cfg.GetCVar(CCVars.MalinovMessengerSoundEnabled))
        {
            var sound = new SoundCollectionSpecifier(MalinovMessengerConstants.NotificationSoundCollection);
            _audio.PlayPvs(sound, loaderUid);
        }

        OnNotificationSent?.Invoke(loaderUid, sender);
    }

    /// <summary>
    ///     Walks up the container chain of the PDA and returns the first entity that has an
    ///     <see cref="Robust.Shared.Player.ActorComponent"/> (i.e. the player currently holding/wearing it).
    ///     Returns false if the PDA is not inside a player-owned container (e.g. lying on the floor).
    /// </summary>
    private bool TryGetPdaHolder(EntityUid pdaUid, out EntityUid holderUid)
    {
        holderUid = EntityUid.Invalid;
        var current = pdaUid;

        while (current != EntityUid.Invalid)
        {
            if (!_containerSystem.TryGetContainingContainer((current, null, null), out var container))
                return false;

            var owner = container.Owner;
            if (TryComp<Robust.Shared.Player.ActorComponent>(owner, out _))
            {
                holderUid = owner;
                return true;
            }

            current = owner;
        }

        return false;
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

            if (name == session.IdentityName)
                continue;

            session.Peers[name] = new PeerCache(address, expiry);
        }

        if (packet.Data.TryGetValue(MalinovMessengerConstants.MutedNamesKey, out var mutedObj)
            && mutedObj is string[] mutedNames
            && !string.IsNullOrWhiteSpace(session.IdentityName))
        {
            session.IsMuted = Array.IndexOf(mutedNames, session.IdentityName) >= 0;
        }
    }

    /// <summary>
    ///     Rolls back the outgoing message referenced by a relay delivery-error reply.
    ///     The relay is the delivery authority: an error means the message never reached
    ///     the recipient, so it is removed from the sender's history. The id is
    ///     authoritative; when it is missing, the most recent outgoing message is rolled
    ///     back instead as a best-effort fallback.
    /// </summary>
    private void RollbackPendingOutgoing(MalinovMessengerCartridgeSessionComponent session, long messageId)
    {
        var sessions = GetSessionDictionary(session);

        if (messageId != 0)
        {
            foreach (var (_, history) in sessions)
            {
                for (var i = history.Count - 1; i >= 0; i--)
                {
                    if (history[i].Outgoing && history[i].Id == messageId)
                    {
                        history.RemoveAt(i);
                        return;
                    }
                }
            }

            // The id was not found (e.g. already trimmed); fall through to the newest outgoing.
        }

        MalinovMessengerMessage? newest = null;
        List<MalinovMessengerMessage>? newestHistory = null;
        var newestIndex = -1;

        foreach (var (_, history) in sessions)
        {
            for (var i = history.Count - 1; i >= 0; i--)
            {
                if (!history[i].Outgoing)
                    continue;

                if (newest == null || history[i].Timestamp > newest.Timestamp)
                {
                    newest = history[i];
                    newestHistory = history;
                    newestIndex = i;
                }
            }
        }

        if (newest != null && newestHistory != null && newestIndex >= 0)
            newestHistory.RemoveAt(newestIndex);
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
                if (entry.Name == session.IdentityName)
                    continue;

                contacts.Add(new MalinovMessengerContact(entry.Name));
            }
        }

        // A previously selected self-contact should no longer stay selected after the filter is applied.
        if (session.SelectedContact == session.IdentityName)
            session.SelectedContact = null;

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

        var state = new MalinovMessengerUiState(contacts, status, serverStatus, session.SelectedContact, messages, onlineNames, session.IsMuted);
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
