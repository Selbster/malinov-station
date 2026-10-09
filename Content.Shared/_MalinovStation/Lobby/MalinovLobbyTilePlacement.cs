using Robust.Shared.Serialization;

namespace Content.Shared._MalinovStation.Lobby;

/// <summary>
/// Cells of the lobby board covered by one tile: its top left cell and its size in cells.
/// </summary>
[Serializable, NetSerializable]
public readonly record struct MalinovLobbyTilePlacement(int Column, int Row, int Width, int Height)
{
    /// <summary>
    /// First column to the right of the tile.
    /// </summary>
    public int Right => Column + Width;

    /// <summary>
    /// First row below the tile.
    /// </summary>
    public int Bottom => Row + Height;

    public Vector2i Position => new(Column, Row);

    public Vector2i Size => new(Width, Height);

    public MalinovLobbyTilePlacement(Vector2i position, Vector2i size) : this(position.X, position.Y, size.X, size.Y)
    {
    }

    /// <summary>
    /// Whether both placements cover at least one common cell.
    /// </summary>
    public bool Overlaps(MalinovLobbyTilePlacement other)
    {
        return Column < other.Right && other.Column < Right && Row < other.Bottom && other.Row < Bottom;
    }
}
