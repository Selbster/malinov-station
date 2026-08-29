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
    ///     Localization key describing the server relay mode
    ///     (e.g. "via server" or "server unavailable").
    /// </summary>
    public string ServerStatus;

    /// <summary>
    ///     Name of the currently selected contact.
    /// </summary>
    public string? SelectedContact;

    /// <summary>
    ///     Messages of the currently selected conversation.
    /// </summary>
    public List<MalinovMessengerMessage> Messages;

    /// <summary>
    ///     Names of contacts currently considered online.
    /// </summary>
    public HashSet<string> OnlineNames;

    /// <summary>
    ///     Whether the account identity of this cartridge is muted by the relay.
    /// </summary>
    public bool IsMuted;

    public MalinovMessengerUiState(
        List<MalinovMessengerContact>? contacts = null,
        string? status = null,
        string? serverStatus = null,
        string? selectedContact = null,
        List<MalinovMessengerMessage>? messages = null,
        HashSet<string>? onlineNames = null,
        bool isMuted = false)
    {
        Contacts = contacts ?? new List<MalinovMessengerContact>();
        Status = status ?? string.Empty;
        ServerStatus = serverStatus ?? string.Empty;
        SelectedContact = selectedContact;
        Messages = messages ?? new List<MalinovMessengerMessage>();
        OnlineNames = onlineNames ?? new HashSet<string>();
        IsMuted = isMuted;
    }
}
