using Content.Shared._MalinovStation.Lobby;

namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// Derives <see cref="MalinovLobbyPhase"/> from the lobby state the client receives from the game ticker.
/// </summary>
public static class MalinovLobbyPhases
{
    /// <param name="isGameStarted">Whether the round is running.</param>
    /// <param name="paused">Whether the lobby countdown is paused.</param>
    public static MalinovLobbyPhase Get(bool isGameStarted, bool paused)
    {
        if (isGameStarted)
            return MalinovLobbyPhase.InRound;

        // A start time that has already passed still counts as the end of the countdown:
        // the round is about to start.
        return paused ? MalinovLobbyPhase.PreRound : MalinovLobbyPhase.Countdown;
    }
}
