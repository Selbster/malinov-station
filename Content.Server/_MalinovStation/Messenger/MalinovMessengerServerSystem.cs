using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.DeviceNetwork.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Power.Components;
using Content.Shared.Database;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.CCVar;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Events;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Server-side relay for the Malinov Messenger.
///     Maintains a card-id-to-address directory and re-routes messages between clients.
///     Presence follows the inserted ID card; mutes follow the account holding the PDA.
/// </summary>
public sealed partial class MalinovMessengerServerSystem : EntitySystem
{
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IAdminLogManager _adminLog = default!;

    /// <summary>
    ///     Per-sender timestamps of messages routed through the relay, used to duplicate
    ///     the sender-side rate-limit and protect against clients that bypass it.
    /// </summary>
    private readonly Dictionary<string, List<TimeSpan>> _rateWindows = new();

    /// <summary>
    ///     Last announce time per directory key, used to prune entries whose card has not
    ///     re-announced within the peer TTL.
    /// </summary>
    private readonly Dictionary<string, TimeSpan> _lastSeen = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MalinovMessengerServerComponent, DeviceNetworkPacketEvent>(OnPacketReceived);
        SubscribeLocalEvent<MalinovMessengerServerComponent, ComponentRemove>(OnRemove);
        SubscribeLocalEvent<MalinovMessengerServerComponent, DeviceNetServerDisconnectedEvent>(OnDisconnected);
    }

    private void OnPacketReceived(EntityUid uid, MalinovMessengerServerComponent component, DeviceNetworkPacketEvent args)
    {
        if (args.Frequency != MalinovMessengerConstants.Frequency)
            return;

        if (!TryComp<ApcPowerReceiverComponent>(uid, out var power) || !power.Powered)
            return;

        if (!args.Data.TryGetValue(MalinovMessengerConstants.CommandKey, out var cmdObj) || cmdObj is not string cmd)
            return;

        switch (cmd)
        {
            case MalinovMessengerConstants.CommandAnnounce:
                HandleAnnounce(uid, component, args);
                break;

            case MalinovMessengerConstants.CommandDirectoryRequest:
                HandleDirectoryRequest(uid, component, args);
                break;

            case MalinovMessengerConstants.CommandMessage:
                HandleMessage(uid, component, args);
                break;
        }
    }

    private void OnRemove(EntityUid uid, MalinovMessengerServerComponent component, ComponentRemove args)
    {
        component.Directory.Clear();
        component.Names.Clear();
        component.AccountCards.Clear();
        component.Muted.Clear();
        _rateWindows.Clear();
        _lastSeen.Clear();
    }

    private void OnDisconnected(EntityUid uid, MalinovMessengerServerComponent component, ref DeviceNetServerDisconnectedEvent args)
    {
        component.Directory.Clear();
        component.Names.Clear();
        component.AccountCards.Clear();
        component.Muted.Clear();
        _rateWindows.Clear();
        _lastSeen.Clear();
    }

    private void HandleAnnounce(EntityUid uid, MalinovMessengerServerComponent component, DeviceNetworkPacketEvent args)
    {
        if (!args.Data.TryGetValue(MalinovMessengerConstants.SenderNameKey, out var nameObj) || nameObj is not string name)
            return;

        if (string.IsNullOrWhiteSpace(name))
            return;

        var cardId = args.Data.TryGetValue(MalinovMessengerConstants.SenderCardKey, out var cardObj) && cardObj is string cardStr
            ? cardStr
            : null;

        var account = args.Data.TryGetValue(MalinovMessengerConstants.SenderAccountKey, out var accountObj) && accountObj is string accountStr
            ? accountStr
            : null;

        // The card id is the stable routing key: a card keeps its identity regardless of
        // which PDA holds it, so a stolen card never shows up twice. The display name is
        // only a fallback key when the announce carries no card id.
        var key = string.IsNullOrWhiteSpace(cardId) ? name : cardId;

        PruneDirectory(component);

        component.Directory[key] = args.SenderAddress;
        component.Names[key] = name;
        _lastSeen[key] = _timing.CurTime;

        // Track which card the account was last seen with, for admin-log context only.
        // When the account is absent (card on the floor) the display name is the context key.
        var contextKey = string.IsNullOrWhiteSpace(account) ? name : account;
        component.AccountCards[contextKey] = key;

        BroadcastDirectory(uid, component);
    }

    /// <summary>
    ///     Removes directory entries whose card has not re-announced within the peer TTL.
    /// </summary>
    private void PruneDirectory(MalinovMessengerServerComponent component)
    {
        var ttl = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.MalinovMessengerPeerTtlSeconds));
        var now = _timing.CurTime;

        List<string>? expired = null;
        foreach (var (key, lastSeen) in _lastSeen)
        {
            if (now - lastSeen <= ttl)
                continue;

            (expired ??= new List<string>()).Add(key);
        }

        if (expired == null)
            return;

        foreach (var key in expired)
        {
            component.Directory.Remove(key);
            component.Names.Remove(key);
            _lastSeen.Remove(key);
        }
    }

    private void BroadcastDirectory(EntityUid uid, MalinovMessengerServerComponent component)
    {
        var entries = new Dictionary<string, string>(component.Directory);
        var names = new Dictionary<string, string>(component.Names);

        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandDirectory,
            [MalinovMessengerConstants.EntriesKey] = entries,
            [MalinovMessengerConstants.DisplayNamesKey] = names,
            [MalinovMessengerConstants.MutedNamesKey] = component.Muted.ToArray(),
        };

        foreach (var address in component.Directory.Values)
        {
            _deviceNetwork.QueuePacket(uid, address, payload, MalinovMessengerConstants.Frequency);
        }
    }

    private void HandleDirectoryRequest(EntityUid uid, MalinovMessengerServerComponent component, DeviceNetworkPacketEvent args)
    {
        var entries = new Dictionary<string, string>(component.Directory);
        var names = new Dictionary<string, string>(component.Names);

        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandDirectory,
            [MalinovMessengerConstants.EntriesKey] = entries,
            [MalinovMessengerConstants.DisplayNamesKey] = names,
            [MalinovMessengerConstants.MutedNamesKey] = component.Muted.ToArray(),
        };

        _deviceNetwork.QueuePacket(uid, args.SenderAddress, payload, MalinovMessengerConstants.Frequency);
    }

    private void HandleMessage(EntityUid uid, MalinovMessengerServerComponent component, DeviceNetworkPacketEvent args)
    {
        var messageId = args.Data.TryGetValue(MalinovMessengerConstants.MessageIdKey, out var idObj) && idObj is long idValue
            ? idValue
            : 0L;

        if (!args.Data.TryGetValue(MalinovMessengerConstants.TargetKey, out var targetObj) || targetObj is not string targetName)
        {
            _adminLog.Add(LogType.MalinovMessengerDeliveryFail, LogImpact.Low, $"Messenger delivery failed: no recipient specified");
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-offline", messageId);
            return;
        }

        if (!component.Directory.TryGetValue(targetName, out var targetAddress))
        {
            _adminLog.Add(LogType.MalinovMessengerDeliveryFail, LogImpact.Low, $"Messenger delivery failed: recipient '{targetName}' not found in directory");
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-offline", messageId);
            return;
        }

        if (!args.Data.TryGetValue(MalinovMessengerConstants.SenderNameKey, out var senderObj) || senderObj is not string senderName)
        {
            _adminLog.Add(LogType.MalinovMessengerDeliveryFail, LogImpact.Low, $"Messenger delivery failed: no sender name specified");
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-offline", messageId);
            return;
        }

        var senderAccount = args.Data.TryGetValue(MalinovMessengerConstants.SenderAccountKey, out var accountObj) && accountObj is string accountStr
            ? accountStr
            : null;

        var senderCard = args.Data.TryGetValue(MalinovMessengerConstants.SenderCardKey, out var cardObj) && cardObj is string cardStr
            ? cardStr
            : null;

        // Account is the authoritative mute identity: a mute follows the account holding the
        // PDA, so it survives card swaps and never blocks a different card with the same name.
        // A card without a held account (lying on the floor) has no muteable identity.
        if (senderAccount != null && component.Muted.Contains(senderAccount))
        {
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-muted", messageId);
            return;
        }

        // Rate-limit keying falls back from the account to the card id so an anti-bypass
        // window is still enforced for unheld cards.
        var blockKey = senderAccount ?? senderCard ?? senderName;
        if (IsRelayRateLimited(blockKey))
        {
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-rate-limited", messageId);
            return;
        }

        if (!args.Data.TryGetValue(MalinovMessengerConstants.TextKey, out var textObj) || textObj is not string text)
        {
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-offline", messageId);
            return;
        }

        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandMessage,
            [MalinovMessengerConstants.SenderNameKey] = senderName,
            [MalinovMessengerConstants.SenderAccountKey] = senderAccount,
            [MalinovMessengerConstants.SenderCardKey] = senderCard,
            [MalinovMessengerConstants.TextKey] = text,
        };

        _deviceNetwork.QueuePacket(uid, targetAddress, payload, MalinovMessengerConstants.Frequency);
    }

    /// <summary>
    ///     Mutes a sender account (launcher username) for the rest of the round. Muted accounts are
    ///     silently dropped by the relay and notified with the mute error. Survives ID card swaps.
    /// </summary>
    public void Mute(EntityUid uid, string accountName, string? admin = null)
    {
        if (!TryComp<MalinovMessengerServerComponent>(uid, out var component))
            return;

        component.Muted.Add(accountName);
        _adminLog.Add(LogType.MalinovMessengerMute, LogImpact.Medium,
            $"Messenger account '{accountName}' muted by '{admin ?? "an administrator"}'{GetCardContext(component, accountName)}");
        BroadcastDirectory(uid, component);
    }

    /// <summary>
    ///     Unmutes a previously muted sender account, restoring their message delivery.
    /// </summary>
    public void Unmute(EntityUid uid, string accountName, string? admin = null)
    {
        if (!TryComp<MalinovMessengerServerComponent>(uid, out var component))
            return;

        component.Muted.Remove(accountName);
        _adminLog.Add(LogType.MalinovMessengerMute, LogImpact.Medium,
            $"Messenger account '{accountName}' unmuted by '{admin ?? "an administrator"}'{GetCardContext(component, accountName)}");
        BroadcastDirectory(uid, component);
    }

    /// <summary>
    ///     Appends the last known display name (ID card name) behind an account to admin-log lines.
    ///     The account may be a launcher username or, when the PDA was never held, the display name.
    /// </summary>
    private static string GetCardContext(MalinovMessengerServerComponent component, string accountName)
    {
        if (!component.AccountCards.TryGetValue(accountName, out var cardId))
            return string.Empty;

        return component.Names.TryGetValue(cardId, out var cardName) && !string.IsNullOrWhiteSpace(cardName)
            ? $" (card: '{cardName}')"
            : string.Empty;
    }

    /// <summary>
    ///     Server-side duplication of the sender rate-limit, guarding against clients that
    ///     bypass their local check. Prunes stale entries per sender identity.
    /// </summary>
    private bool IsRelayRateLimited(string senderAccount)
    {
        var window = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.MalinovMessengerRateWindowSeconds));
        var maxMessages = _cfg.GetCVar(CCVars.MalinovMessengerRateMaxMessages);
        var now = _timing.CurTime;

        if (!_rateWindows.TryGetValue(senderAccount, out var timestamps))
        {
            timestamps = new List<TimeSpan>();
            _rateWindows[senderAccount] = timestamps;
        }

        timestamps.RemoveAll(t => now - t > window);

        if (timestamps.Count >= maxMessages)
        {
            _adminLog.Add(LogType.MalinovMessengerRateLimited, LogImpact.Medium,
                $"Messenger sender '{senderAccount}' was rate-limited (window: {_cfg.GetCVar(CCVars.MalinovMessengerRateWindowSeconds)}s, limit: {maxMessages})");
            return true;
        }

        timestamps.Add(now);
        return false;
    }

    private void ReplyError(EntityUid uid, string senderAddress, string errorKey, long messageId = 0)
    {
        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandMessage,
            [MalinovMessengerConstants.SenderNameKey] = string.Empty,
            [MalinovMessengerConstants.ErrorKey] = errorKey,
            [MalinovMessengerConstants.MessageIdKey] = messageId,
        };

        _deviceNetwork.QueuePacket(uid, senderAddress, payload, MalinovMessengerConstants.Frequency);
    }
}
