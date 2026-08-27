using Content.Shared._MalinovStation.Messenger;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Server-only account data tied to an ID card.
///     Holds the messenger history so it follows the card, not the PDA.
/// </summary>
[RegisterComponent]
public sealed partial class MalinovMessengerAccountComponent : Component
{
    /// <summary>
    ///     Per-contact chat history ordered from oldest to newest.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, List<MalinovMessengerMessage>> Sessions = new();
}
