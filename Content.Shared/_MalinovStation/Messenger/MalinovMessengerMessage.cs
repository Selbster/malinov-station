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

    public MalinovMessengerMessage(string senderName, string text, TimeSpan timestamp, bool outgoing)
    {
        SenderName = senderName;
        Text = text;
        Timestamp = timestamp;
        Outgoing = outgoing;
    }
}
