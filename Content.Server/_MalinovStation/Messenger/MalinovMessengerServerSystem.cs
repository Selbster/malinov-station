using Content.Server.DeviceNetwork.Components;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.Power.Components;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.DeviceNetwork;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.DeviceNetwork.Events;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Server-side relay for the Malinov Messenger.
///     Maintains a name-to-address directory and re-routes messages between clients.
/// </summary>
public sealed partial class MalinovMessengerServerSystem : EntitySystem
{
    [Dependency] private DeviceNetworkSystem _deviceNetwork = default!;

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
    }

    private void OnDisconnected(EntityUid uid, MalinovMessengerServerComponent component, ref DeviceNetServerDisconnectedEvent args)
    {
        component.Directory.Clear();
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
        };

        _deviceNetwork.QueuePacket(uid, args.SenderAddress, payload, MalinovMessengerConstants.Frequency);
    }

    private void HandleMessage(EntityUid uid, MalinovMessengerServerComponent component, DeviceNetworkPacketEvent args)
    {
        if (!args.Data.TryGetValue(MalinovMessengerConstants.TargetKey, out var targetObj) || targetObj is not string targetName)
        {
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-offline");
            return;
        }

        if (!component.Directory.TryGetValue(targetName, out var targetAddress))
        {
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-offline");
            return;
        }

        if (!args.Data.TryGetValue(MalinovMessengerConstants.SenderNameKey, out var senderObj) || senderObj is not string senderName)
        {
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-offline");
            return;
        }

        if (!args.Data.TryGetValue(MalinovMessengerConstants.TextKey, out var textObj) || textObj is not string text)
        {
            ReplyError(uid, args.SenderAddress, "malinov-messenger-error-offline");
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

    private void ReplyError(EntityUid uid, string senderAddress, string errorKey)
    {
        var payload = new NetworkPayload
        {
            [MalinovMessengerConstants.CommandKey] = MalinovMessengerConstants.CommandMessage,
            [MalinovMessengerConstants.SenderNameKey] = string.Empty,
            [MalinovMessengerConstants.TextKey] = errorKey,
        };

        _deviceNetwork.QueuePacket(uid, senderAddress, payload, MalinovMessengerConstants.Frequency);
    }
}
