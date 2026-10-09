namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// Lobby actions that tiles may ask for. Implemented by <see cref="MalinovLobbyTileUIController"/>.
/// </summary>
public interface IMalinovLobbyActions
{
    /// <summary>
    /// Asks the server to mark the player (not) ready for the next round.
    /// </summary>
    /// <returns><c>false</c> if the round already runs.</returns>
    bool TrySetReady(bool ready);

    /// <summary>
    /// Opens the late join window.
    /// </summary>
    /// <returns><c>false</c> if no round runs or late joining is forbidden.</returns>
    bool TryJoinGame();
}
