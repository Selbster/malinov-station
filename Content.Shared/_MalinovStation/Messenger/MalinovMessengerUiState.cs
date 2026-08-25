using Robust.Shared.Serialization;

namespace Content.Shared._MalinovStation.Messenger;

/// <summary>
/// UI state for the Malinov Messenger cartridge.
/// </summary>
[Serializable, NetSerializable]
public sealed class MalinovMessengerUiState : BoundUserInterfaceState
{
    /// <summary>
    ///     Catalog of station crew contacts shown in the messenger.
    /// </summary>
    public List<MalinovMessengerContact> Contacts;

    /// <summary>
    ///     Localization key for a status message (e.g. manifest unavailable or send error).
    ///     Empty when no status should be shown.
    /// </summary>
    public string Status;

    /// <summary>
    ///     Name of the currently selected contact.
    /// </summary>
    public string? SelectedContact;

    /// <summary>
    ///     Lines of the chat session with the selected contact.
    /// </summary>
    public List<string> SessionLines;

    /// <summary>
    ///     Names of contacts currently considered online.
    /// </summary>
    public HashSet<string> OnlineNames;

    public MalinovMessengerUiState(
        List<MalinovMessengerContact>? contacts = null,
        string? status = null,
        string? selectedContact = null,
        List<string>? sessionLines = null,
        HashSet<string>? onlineNames = null)
    {
        Contacts = contacts ?? new List<MalinovMessengerContact>();
        Status = status ?? string.Empty;
        SelectedContact = selectedContact;
        SessionLines = sessionLines ?? new List<string>();
        OnlineNames = onlineNames ?? new HashSet<string>();
    }
}
