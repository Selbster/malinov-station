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
    public List<MalinovMessengerContact> Contacts { get; }

    /// <summary>
    ///     Localization key for a status message (e.g. manifest unavailable).
    ///     Empty when the catalog is available.
    /// </summary>
    public string Status { get; }

    public MalinovMessengerUiState(List<MalinovMessengerContact>? contacts = null, string? status = null)
    {
        Contacts = contacts ?? new List<MalinovMessengerContact>();
        Status = status ?? string.Empty;
    }
}
