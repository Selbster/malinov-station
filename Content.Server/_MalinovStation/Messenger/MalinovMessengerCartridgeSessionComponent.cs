using Content.Shared._MalinovStation.Messenger;
using Content.Shared.CCVar;

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
    ///     Stable account name (SS14 launcher username) of the player currently holding the PDA.
    ///     This is the mute identity; it survives ID card swaps. Null when the PDA is not held
    ///     by a player (e.g. lying on the floor).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public string? AccountName;

    /// <summary>
    ///     Stable card id (round-local NetEntity of the linked ID card) providing the messenger
    ///     presence/routing identity. Unlike <see cref="AccountName"/> it never changes when the
    ///     card is moved between PDAs or picked up by another player. Null when no card is linked.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public string? CardId;

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
    ///     Known peers: card id -> cached address, expiry and display name.
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
    public string? ServerAddress;

    /// <summary>
    ///     Whether the messenger UI is currently active (opened) on the PDA.
    ///     Used to suppress notifications for the active conversation.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public bool IsProgramActive;

    /// <summary>
    ///     Contacts with messages the player has not read. Cleared when the conversation
    ///     is opened (or already visible in the open messenger window).
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public HashSet<string> UnreadContacts = new();

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
    ///     Timestamps of recently sent messages, used to enforce the outbound portion of
    ///     the rate-limit window on the sender side before a message ever leaves the device.
    /// </summary>
    public List<TimeSpan> SentTimestamps = new();

    /// <summary>
    ///     Moment in round time until which the sender is blocked from sending (a fixed
    ///     <see cref="CCVars.MalinovMessengerRateWindowSeconds"/> cooldown measured from when
    ///     the limit was hit). While <c>now &lt; RateLimitUntil</c> outbound sends are rejected
    ///     on the sender side. Enforced only in round memory; not persisted between rounds.
    /// </summary>
    public TimeSpan? RateLimitUntil;

    /// <summary>
    ///     Integer seconds last pushed to the client during a rate-limit cooldown. Used to
    ///     throttle per-second UI state updates so the client receives a fresh deadline each
    ///     second without spamming one every tick.
    /// </summary>
    public int LastDisplayedRateLimitSecond;

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
/// Cached device-network address of a peer plus its TTL and display name.
/// </summary>
public sealed class PeerCache
{
    public string Address;
    public TimeSpan Expiry;
    public string Name;

    public PeerCache(string address, TimeSpan expiry, string name)
    {
        Address = address;
        Expiry = expiry;
        Name = name;
    }
}
