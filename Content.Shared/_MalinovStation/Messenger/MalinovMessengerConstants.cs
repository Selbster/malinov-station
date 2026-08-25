using Content.Shared.DeviceNetwork;

namespace Content.Shared._MalinovStation.Messenger;

/// <summary>
/// Constants for the Malinov Messenger device network protocol.
/// </summary>
public static class MalinovMessengerConstants
{
    /// <summary>
    ///     Device network frequency used by the messenger P2P channel.
    /// </summary>
    public const uint Frequency = 2210;

    public const string CommandAnnounce = "announce";
    public const string CommandMessage = "msg";
    public const string CommandDirectoryRequest = "directory_req";
    public const string CommandDirectory = "directory";

    /// <summary>
    ///     Payload key for the command name. Matches <see cref="DeviceNetworkConstants.Command"/>.
    /// </summary>
    public const string CommandKey = "command";

    /// <summary>
    ///     Payload key for the human-readable sender name.
    /// </summary>
    public const string SenderNameKey = "sender_name";

    /// <summary>
    ///     Payload key for the message text.
    /// </summary>
    public const string TextKey = "text";

    /// <summary>
    ///     Payload key for the intended recipient name (used by future server logic).
    /// </summary>
    public const string TargetKey = "target";
}
