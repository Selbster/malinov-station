using Content.Shared._MalinovStation.Messenger;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Server-only runtime state for a Malinov Messenger cartridge.
/// </summary>
[RegisterComponent]
public sealed partial class MalinovMessengerCartridgeSessionComponent : Component
{
    /// <summary>
    ///     ID card currently providing the messenger account. Null when no card is inserted.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public EntityUid? LinkedAccount;

    /// <summary>
    ///     Display name taken from the linked ID card. Null when no card is inserted.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public string? IdentityName;

    /// <summary>
    ///     Name of the contact currently selected in the UI.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public string? SelectedContact;

    /// <summary>
    ///     Local per-contact chat history used when no ID card is linked.
    ///     Messages are ordered from oldest to newest.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, List<MalinovMessengerMessage>> LocalSessions = new();

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

    /// <summary>
    ///     Whether the station messenger server is currently reachable.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool ServerAvailable;

    /// <summary>
    ///     Cached address of the active station messenger server.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public string? ServerAddress;

    /// <summary>
    ///     Whether the messenger UI is currently active (opened) on the PDA.
    ///     Used to suppress notifications for the active conversation.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool IsProgramActive;

    /// <summary>
    ///     Whether this account identity is currently muted by the relay. The mute list is
    ///     delivered with directory broadcasts and mirrored by the relay error reply.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool IsMuted;

    /// <summary>
    ///     Monotonic counter used to generate unique ids for outgoing messages,
    ///     allowing delivery-error replies to correlate and roll back the right message.
    /// </summary>
    public long NextMessageId;

    /// <summary>
    ///     Last time an announce was sent to the server.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan LastAnnounceTime;

    /// <summary>
    ///     Last time a directory request was sent to the server.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public TimeSpan LastDirectoryRequestTime;
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
