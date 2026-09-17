using Content.Shared.DeviceNetwork;

namespace Content.Shared._MalinovStation.Messenger;

/// <summary>
/// Constants for the Malinov Messenger device network protocol.
/// </summary>
public static class MalinovMessengerConstants
{
    /// <summary>
    ///     Device network frequency used by the messenger P2P channel.
    ///     Must stay in sync with the <c>MalinovMessengerFrequency</c> deviceFrequency prototype
    ///     (<c>frequency</c> field).
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
    ///     Payload key for the stable sender account name (SS14 launcher username).
    ///     The mute identity; <see cref="SenderNameKey"/> is only a display label.
    ///     <see cref="SenderCardKey"/> is the routing/presence identity.
    /// </summary>
    public const string SenderAccountKey = "sender_account";

    /// <summary>
    ///     Payload key for the stable sender ID card id (round-local NetEntity of the card).
    ///     The routing/presence identity: the messenger account is the inserted card, so a
    ///     card keeps its identity independent of which PDA holds it and who carries that PDA.
    /// </summary>
    public const string SenderCardKey = "sender_card";

    /// <summary>
    ///     Payload key for the card id -> display-name map delivered with the directory.
    /// </summary>
    public const string DisplayNamesKey = "display_names";

    /// <summary>
    ///     Payload key for the message text.
    /// </summary>
    public const string TextKey = "text";

    /// <summary>
    ///     Payload key for the intended recipient card id (used by server relay logic).
    /// </summary>
    public const string TargetKey = "target";

    /// <summary>
    ///     Payload key for the directory entries returned by the server.
    /// </summary>
    public const string EntriesKey = "entries";

    /// <summary>
    ///     Payload key for the per-sender message id used to correlate relay delivery errors.
    /// </summary>
    public const string MessageIdKey = "msg_id";

    /// <summary>
    ///     Payload key for the list of sender names currently muted by the server relay.
    /// </summary>
    public const string MutedNamesKey = "muted_names";

    /// <summary>
    ///     Payload key carrying the localization key of a relay delivery error. Set on error
    ///     replies instead of overloading <see cref="TextKey"/>.
    /// </summary>
    public const string ErrorKey = "error";

    /// <summary>
    ///     Sound collection played for incoming messenger notifications.
    /// </summary>
    public const string NotificationSoundCollection = "malinov_messenger_notification";
}
