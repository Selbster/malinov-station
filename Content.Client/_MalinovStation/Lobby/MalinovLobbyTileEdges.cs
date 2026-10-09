namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// Edges of a lobby tile that a resize handle moves: one for a side, two for a corner.
/// </summary>
[Flags]
public enum MalinovLobbyTileEdges : byte
{
    None = 0,
    Left = 1 << 0,
    Top = 1 << 1,
    Right = 1 << 2,
    Bottom = 1 << 3,
    TopLeft = Top | Left,
    TopRight = Top | Right,
    BottomLeft = Bottom | Left,
    BottomRight = Bottom | Right,
}
