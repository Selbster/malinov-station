using Robust.Shared.Serialization;

namespace Content.Shared._MalinovStation.Messenger;

/// <summary>
///     A single contact entry in the Malinov Messenger catalog.
/// </summary>
[Serializable, NetSerializable]
public sealed class MalinovMessengerContact
{
    /// <summary>
    ///     Display name of the contact.
    /// </summary>
    public string Name;

    /// <summary>
    ///     True when the contact has messages the player has not read yet.
    /// </summary>
    public bool HasUnread;

    public MalinovMessengerContact(string name, bool hasUnread = false)
    {
        Name = name;
        HasUnread = hasUnread;
    }
}
