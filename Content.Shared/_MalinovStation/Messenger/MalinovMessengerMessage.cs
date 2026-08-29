using Robust.Shared.Serialization;

namespace Content.Shared._MalinovStation.Messenger;

/// <summary>
///     A single message in a Malinov Messenger conversation.
/// </summary>
[Serializable, NetSerializable]
public sealed class MalinovMessengerMessage
{
    /// <summary>
    ///     Display name of the message sender.
    /// </summary>
    public string SenderName;

    /// <summary>
    ///     Message text.
    /// </summary>
    public string Text;

    /// <summary>
    ///     In-game timestamp when the message was sent.
    /// </summary>
    public TimeSpan Timestamp;

    /// <summary>
    ///     True if the message was sent by the owner of this device.
    /// </summary>
    public bool Outgoing;

    /// <summary>
    ///     Unique-per-session id of an outgoing message. Zero for incoming messages
    ///     and for messages that predate id tracking.
    /// </summary>
    public long Id;

    public MalinovMessengerMessage(string senderName, string text, TimeSpan timestamp, bool outgoing, long id = 0)
    {
        SenderName = senderName;
        Text = text;
        Timestamp = timestamp;
        Outgoing = outgoing;
        Id = id;
    }
}
