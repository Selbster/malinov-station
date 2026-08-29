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
    ///     Messages of the currently selected conversation.
    /// </summary>
    public List<MalinovMessengerMessage> Messages;

    /// <summary>
    ///     Whether the account identity of this cartridge is muted by the relay.
    /// </summary>
    public bool IsMuted;

    public MalinovMessengerUiState(
        List<MalinovMessengerContact>? contacts = null,
        string? status = null,
        string? selectedContact = null,
        List<MalinovMessengerMessage>? messages = null,
        bool isMuted = false)
    {
        Contacts = contacts ?? new List<MalinovMessengerContact>();
        Status = status ?? string.Empty;
        SelectedContact = selectedContact;
        Messages = messages ?? new List<MalinovMessengerMessage>();
        IsMuted = isMuted;
    }
}
