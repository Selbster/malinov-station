using Content.Shared._MalinovStation.Lobby;
using Content.Shared.GameTicking.Prototypes;

namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// Snapshot of the lobby data that tiles display.
/// <see cref="MalinovLobbyTileUIController"/> rebuilds it when lobby data changes and once per second,
/// so countdowns stay current.
/// </summary>
public readonly record struct MalinovLobbyTileContext
{
    /// <summary>
    /// Whether the round runs.
    /// </summary>
    public bool IsGameStarted { get; init; }

    /// <summary>
    /// Whether the round start countdown is paused.
    /// </summary>
    public bool Paused { get; init; }

    /// <summary>
    /// Whether the server considers this player ready.
    /// </summary>
    public bool AreWeReady { get; init; }

    /// <summary>
    /// Whether joining the running round is forbidden.
    /// </summary>
    public bool DisallowedLateJoin { get; init; }

    /// <summary>
    /// Game time at which the round is due to start.
    /// </summary>
    public TimeSpan StartTime { get; init; }

    /// <summary>
    /// Game time at which the running round started.
    /// </summary>
    public TimeSpan RoundStartTime { get; init; }

    /// <summary>
    /// Current game time.
    /// </summary>
    public TimeSpan CurTime { get; init; }

    /// <summary>
    /// Lobby title: the lobby name set by the server, or its server name.
    /// </summary>
    public string? ServerTitle { get; init; }

    /// <summary>
    /// Round information from the server: players, map and game mode. <c>null</c> until the server sends it.
    /// </summary>
    public MalinovLobbyInfoEvent? LobbyInfo { get; init; }

    public float PlaytimeMinutesToday { get; init; }

    /// <summary>
    /// Lobby soundtrack playing now, or <c>null</c> when music is off.
    /// </summary>
    public MalinovLobbySong? Song { get; init; }

    /// <summary>
    /// Current lobby background, or <c>null</c> when there is none.
    /// </summary>
    public LobbyBackgroundPrototype? Background { get; init; }

    public MalinovLobbyPhase Phase => MalinovLobbyPhases.Get(IsGameStarted, Paused);
}

/// <summary>
/// Title and artist of a lobby soundtrack, as stored in the audio file; either may be missing.
/// </summary>
public readonly record struct MalinovLobbySong(string? Title, string? Artist);
