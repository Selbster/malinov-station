namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// Tile content that displays lobby data.
/// </summary>
public interface IMalinovLobbyTileWidget
{
    /// <summary>
    /// Whether the tile should stand out because the player is expected to act on it now.
    /// Read after every <see cref="Refresh"/>.
    /// </summary>
    bool Accented => false;

    /// <summary>
    /// Shows <paramref name="context"/>. Called when lobby data changes and once per second.
    /// </summary>
    void Refresh(in MalinovLobbyTileContext context);
}
