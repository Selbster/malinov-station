namespace Content.Server._MalinovStation.Messenger;

/// <summary>
/// Server-only runtime state for a Malinov Messenger cartridge.
/// </summary>
[RegisterComponent]
public sealed partial class MalinovMessengerCartridgeSessionComponent : Component
{
    /// <summary>
    ///     Name of the contact currently selected in the UI.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public string? SelectedContact;

    /// <summary>
    ///     Per-contact chat session lines.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, List<string>> Sessions = new();

    /// <summary>
    ///     Known peers: display name -> cached address and expiry.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, PeerCache> Peers = new();

    /// <summary>
    ///     Localization key of the last error, if any.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public string? LastError;
}

/// <summary>
/// Cached device-network address of a peer plus its TTL.
/// </summary>
public sealed class PeerCache
{
    public string Address;
    public TimeSpan Expiry;

    public PeerCache(string address, TimeSpan expiry)
    {
        Address = address;
        Expiry = expiry;
    }
}
