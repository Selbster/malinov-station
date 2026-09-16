namespace Content.Shared._MalinovStation.Messenger;

/// <summary>
///     Marker component for the station Malinov Messenger relay server.
/// </summary>
[RegisterComponent]
public sealed partial class MalinovMessengerServerComponent : Component
{
    /// <summary>
    ///     Round-local directory mapping the stable ID card id (with the card display name
    ///     as fallback when no card id is known) to a device network address. A card keeps
    ///     its identity regardless of which PDA holds it, so a stolen card never duplicates.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, string> Directory = new();

    /// <summary>
    ///     Round-local card id to display-name map, used for directory broadcasts and
    ///     lookup of the card display name behind a muted account.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, string> Names = new();

    /// <summary>
    ///     Round-local announce key (account name or, when absent, the display name) to card id
    ///     map. Used only for admin-log context: which card an account was last seen with.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, string> AccountCards = new();

    /// <summary>
    ///     Account names (SS14 launcher usernames) that are muted by admins and must be silently
    ///     dropped by the relay. Lives for the duration of the round only and survives ID card swaps.
    ///     A mute follows the account holding the PDA, not the card inside it.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public HashSet<string> Muted = new();
}
