using System.Linq;
using Content.Server.DeviceNetwork.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Power.Components;
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
///     Maintains a name-to-address directory and re-routes messages between clients.
/// </summary>
public sealed partial class MalinovMessengerServerSystem : EntitySystem
{
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>
    ///     Per-sender-name timestamps of messages routed through the relay, used to duplicate
    ///     the sender-side rate-limit and protect against clients that bypass it.
    /// </summary>
    private readonly Dictionary<string, List<TimeSpan>> _rateWindows = new();

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
        component.Muted.Clear();
        _rateWindows.Clear();
    }

    private void OnDisconnected(EntityUid uid, MalinovMessengerServerComponent component, ref DeviceNetServerDisconnectedEvent args)
    {
        component.Directory.Clear();
        component.Muted.Clear();
        _rateWindows.Clear();
    }

    private void HandleAnnounce(EntityUid uid, MalinovMessengerServerComponent component, DeviceNetworkPacketEvent args)
    {
        if (!args.Data.TryGetValue(MalinovMessengerConstants.SenderNameKey, out var nameObj) || nameObj is not string name)
            return;

        if (string.IsNullOrWhiteSpace(name))
            return;

        component.Directory[name] = args.SenderAddress;
        BroadcastDirectory(uid, component);
    }

    private void BroadcastDirectory(EntityUid uid, MalinovMessengerServerComponent component)
    {
        var entries = new Dictionary<string, string>(component.Directory);

        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandDirectory,
            [MalinovMessengerConstants.EntriesKey] = entries,
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

        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandDirectory,
            [MalinovMessengerConstants.EntriesKey] = entries,
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
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-offline", messageId);
            return;
        }

        if (!component.Directory.TryGetValue(targetName, out var targetAddress))
        {
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-offline", messageId);
            return;
        }

        if (!args.Data.TryGetValue(MalinovMessengerConstants.SenderNameKey, out var senderObj) || senderObj is not string senderName)
        {
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-offline", messageId);
            return;
        }

        if (component.Muted.Contains(senderName))
        {
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-muted", messageId);
            return;
        }

        if (IsRelayRateLimited(senderName))
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
            [MalinovMessengerConstants.TextKey] = text,
        };

        _deviceNetwork.QueuePacket(uid, targetAddress, payload, MalinovMessengerConstants.Frequency);
    }

    /// <summary>
    ///     Mutes a sender name for the rest of the round. Muted senders are silently dropped
    ///     by the relay and notified with the mute error.
    /// </summary>
    public void Mute(EntityUid uid, string name)
    {
        if (!TryComp<MalinovMessengerServerComponent>(uid, out var component))
            return;

        component.Muted.Add(name);
        BroadcastDirectory(uid, component);
    }

    /// <summary>
    ///     Unmutes a previously muted sender name, restoring their message delivery.
    /// </summary>
    public void Unmute(EntityUid uid, string name)
    {
        if (!TryComp<MalinovMessengerServerComponent>(uid, out var component))
            return;

        component.Muted.Remove(name);
        BroadcastDirectory(uid, component);
    }

    /// <summary>
    ///     Server-side duplication of the sender rate-limit, guarding against clients that
    ///     bypass their local check. Prunes stale entries per sender.
    /// </summary>
    private bool IsRelayRateLimited(string senderName)
    {
        var window = TimeSpan.FromSeconds(_cfg.GetCVar(CCVars.MalinovMessengerRateWindowSeconds));
        var maxMessages = _cfg.GetCVar(CCVars.MalinovMessengerRateMaxMessages);
        var now = _timing.CurTime;

        if (!_rateWindows.TryGetValue(senderName, out var timestamps))
        {
            timestamps = new List<TimeSpan>();
            _rateWindows[senderName] = timestamps;
        }

        timestamps.RemoveAll(t => now - t > window);

        if (timestamps.Count >= maxMessages)
            return true;

        timestamps.Add(now);
        return false;
    }

    private void ReplyError(EntityUid uid, string senderAddress, string errorKey, long messageId = 0)
    {
        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandMessage,
            [MalinovMessengerConstants.SenderNameKey] = string.Empty,
            [MalinovMessengerConstants.TextKey] = errorKey,
            [MalinovMessengerConstants.MessageIdKey] = messageId,
        };

        _deviceNetwork.QueuePacket(uid, senderAddress, payload, MalinovMessengerConstants.Frequency);
    }
}
