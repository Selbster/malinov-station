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
    ///     Stable card id used for routing (round-local NetEntity of the recipient's ID card).
    ///     Distinct even when two contacts share <see cref="Name"/>. Null when the
    ///     contact is only a crew-manifest entry with no online peer.
    /// </summary>
    public string? Id;

    /// <summary>
    ///     True when the contact has messages the player has not read yet.
    /// </summary>
    public bool HasUnread;

    public MalinovMessengerContact(string name, string? id = null, bool hasUnread = false)
    {
        Name = name;
        Id = id;
        HasUnread = hasUnread;
    }
}
