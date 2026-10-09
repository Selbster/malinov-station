namespace Content.Shared._MalinovStation.Lobby;

/// <summary>
/// Round phase as the lobby sees it. Lobby tiles choose in which phases they are shown.
/// </summary>
public enum MalinovLobbyPhase : byte
{
    /// <summary>
    /// The round has not started and the start countdown is paused.
    /// </summary>
    PreRound,

    /// <summary>
    /// The round has not started and the countdown is running (or has just run out).
    /// </summary>
    Countdown,

    /// <summary>
    /// The round is running; players can late-join or observe.
    /// </summary>
    InRound,
}
