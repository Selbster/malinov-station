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
    public string Name { get; }

    /// <summary>
    ///     Job title of the contact.
    /// </summary>
    public string JobTitle { get; }

    public MalinovMessengerContact(string name, string jobTitle)
    {
        Name = name;
        JobTitle = jobTitle;
    }
}
