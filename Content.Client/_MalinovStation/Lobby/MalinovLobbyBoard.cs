using Content.Shared._MalinovStation.Lobby;
using Robust.Shared.Prototypes;

namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// A lobby tile as the board sees it: its size until the player changes it, and how small and large it may get.
/// </summary>
public readonly record struct MalinovLobbyBoardTile(
    ProtoId<MalinovLobbyTilePrototype> Id,
    Vector2i Size,
    Vector2i SmallestSize,
    Vector2i LargestSize);

/// <summary>
/// Placement rules of the lobby board, kept free of controls so they can be unit tested.
/// </summary>
/// <remarks>
/// Tiles stay where they are put: the board never closes gaps by itself. A tile that has to make room
/// goes down, just below the tile in its way, and pushes the tiles under it the same way.
/// </remarks>
public static class MalinovLobbyBoard
{
    /// <summary>
    /// Places every tile of <paramref name="tiles"/> on the board.
    /// </summary>
    /// <remarks>
    /// Without placed tiles in <paramref name="layout"/>, the tiles go left to right and top to bottom in the order
    /// of <paramref name="tiles"/>. Otherwise the tiles take their saved places, fitted to their current limits,
    /// with overlaps pushed down; tiles the layout has no place for come below all of them, in the same order.
    /// </remarks>
    /// <param name="tiles">All tiles, in their default order.</param>
    /// <param name="layout">The player's layout, or <c>null</c> for the default one.</param>
    /// <param name="placements">Cleared and filled with a placement for every tile.</param>
    public static void Arrange(
        IReadOnlyList<MalinovLobbyBoardTile> tiles,
        MalinovLobbyLayout? layout,
        Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> placements)
    {
        placements.Clear();

        var unplaced = new List<MalinovLobbyBoardTile>();
        foreach (var tile in tiles)
        {
            if (layout != null && layout.Places.TryGetValue(tile.Id, out var saved))
                placements[tile.Id] = MalinovLobbyLayouts.Fit(saved, tile.SmallestSize, tile.LargestSize);
            else
                unplaced.Add(tile);
        }

        // Saved places can overlap once a tile got larger limits or a new default size.
        SettleInReadingOrder(placements, null, limitRows: false);

        var top = 0;
        foreach (var placement in placements.Values)
        {
            top = Math.Max(top, placement.Bottom);
        }

        var sizes = new List<Vector2i>(unplaced.Count);
        foreach (var tile in unplaced)
        {
            sizes.Add(tile.Size);
        }

        var flow = new List<MalinovLobbyTilePlacement>(unplaced.Count);
        MalinovLobbyTileLayout.Place(sizes, MalinovLobbyLayouts.BoardColumns, flow);
        for (var i = 0; i < unplaced.Count; i++)
        {
            placements[unplaced[i].Id] = flow[i] with { Row = flow[i].Row + top };
        }
    }

    /// <summary>
    /// Where a tile at <paramref name="placement"/> ends up when moved to <paramref name="position"/>: there, kept on the board.
    /// </summary>
    public static MalinovLobbyTilePlacement GetMoved(MalinovLobbyTilePlacement placement, Vector2i position)
    {
        return placement with
        {
            Column = Math.Clamp(position.X, 0, Math.Max(MalinovLobbyLayouts.BoardColumns - placement.Width, 0)),
            Row = Math.Clamp(position.Y, 0, Math.Max(MalinovLobbyLayouts.MaxRows - placement.Height, 0)),
        };
    }

    /// <summary>
    /// What a tile at <paramref name="placement"/> becomes when resized to <paramref name="size"/>: within the limits,
    /// with its top left corner kept unless even the smallest size does not fit beside it.
    /// </summary>
    public static MalinovLobbyTilePlacement GetResized(
        MalinovLobbyTilePlacement placement,
        Vector2i size,
        Vector2i smallest,
        Vector2i largest)
    {
        var corner = new Vector2i(placement.Column + size.X - 1, placement.Row + size.Y - 1);
        return GetResized(placement, MalinovLobbyTileEdges.BottomRight, corner, smallest, largest);
    }

    /// <summary>
    /// What a tile at <paramref name="placement"/> becomes when its <paramref name="edges"/> are dragged to
    /// <paramref name="cell"/>: each moved edge takes the side of that cell, the other edges stay, and a moved edge
    /// stops where the tile would leave its limits or the board.
    /// </summary>
    public static MalinovLobbyTilePlacement GetResized(
        MalinovLobbyTilePlacement placement,
        MalinovLobbyTileEdges edges,
        Vector2i cell,
        Vector2i smallest,
        Vector2i largest)
    {
        var left = placement.Column;
        var top = placement.Row;
        var right = placement.Right;
        var bottom = placement.Bottom;

        // The bounds of a moved edge come from the edge across from it, which stays.
        if ((edges & MalinovLobbyTileEdges.Left) != 0)
            left = Bound(cell.X, Math.Max(0, right - largest.X), right - smallest.X);
        else if ((edges & MalinovLobbyTileEdges.Right) != 0)
            right = Bound(cell.X + 1, left + smallest.X, Math.Min(MalinovLobbyLayouts.BoardColumns, left + largest.X));

        if ((edges & MalinovLobbyTileEdges.Top) != 0)
            top = Bound(cell.Y, Math.Max(0, bottom - largest.Y), bottom - smallest.Y);
        else if ((edges & MalinovLobbyTileEdges.Bottom) != 0)
            bottom = Bound(cell.Y + 1, top + smallest.Y, Math.Min(MalinovLobbyLayouts.MaxRows, top + largest.Y));

        // Bounds that cannot all hold, such as a tile that already breaks its limits, are settled here.
        var resized = new MalinovLobbyTilePlacement(left, top, right - left, bottom - top);
        return MalinovLobbyLayouts.Fit(resized, smallest, largest);
    }

    /// <summary>
    /// Moves <paramref name="tile"/> so its top left cell is <paramref name="position"/>, kept on the board.
    /// A single tile in the way takes the moved tile's old place if it fits there; otherwise the tiles in the way go down.
    /// </summary>
    /// <param name="result">The whole board after the move, when the move is possible.</param>
    /// <returns>Whether the tile is on the board, moves somewhere else and leaves room for everything else.</returns>
    public static bool Move(
        IReadOnlyDictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> placements,
        ProtoId<MalinovLobbyTilePrototype> tile,
        Vector2i position,
        out Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> result)
    {
        result = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>(placements);
        if (!placements.TryGetValue(tile, out var from))
            return false;

        var to = GetMoved(from, position);
        if (to == from)
            return false;

        result[tile] = to;
        if (TrySwap(result, tile, from, to))
            return true;

        return SettleInReadingOrder(result, tile, limitRows: true);
    }

    /// <summary>
    /// Resizes <paramref name="tile"/> to <paramref name="size"/> from its top left corner; the tiles in the way go down.
    /// </summary>
    /// <param name="result">The whole board after the change, when the change is possible.</param>
    /// <returns>Whether the tile is on the board, changes its size and leaves room for everything else.</returns>
    public static bool Resize(
        IReadOnlyDictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> placements,
        ProtoId<MalinovLobbyTilePrototype> tile,
        Vector2i size,
        Vector2i smallest,
        Vector2i largest,
        out Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> result)
    {
        if (!placements.TryGetValue(tile, out var from))
        {
            result = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>(placements);
            return false;
        }

        return Resize(placements, tile, GetResized(from, size, smallest, largest), smallest, largest, out result);
    }

    /// <summary>
    /// Gives <paramref name="tile"/> the cells of <paramref name="area"/>, fitted to its limits and the board;
    /// the tiles in the way go down.
    /// </summary>
    /// <param name="result">The whole board after the change, when the change is possible.</param>
    /// <returns>Whether the tile is on the board, changes its cells and leaves room for everything else.</returns>
    public static bool Resize(
        IReadOnlyDictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> placements,
        ProtoId<MalinovLobbyTilePrototype> tile,
        MalinovLobbyTilePlacement area,
        Vector2i smallest,
        Vector2i largest,
        out Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> result)
    {
        result = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>(placements);
        if (!placements.TryGetValue(tile, out var from))
            return false;

        var to = MalinovLobbyLayouts.Fit(area, smallest, largest);
        if (to == from)
            return false;

        result[tile] = to;
        return SettleInReadingOrder(result, tile, limitRows: true);
    }

    /// <summary>
    /// Fills <paramref name="inTheWay"/> with the tiles other than <paramref name="tile"/> that cover a cell of <paramref name="placement"/>.
    /// </summary>
    public static void GetInTheWay(
        IReadOnlyDictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> placements,
        ProtoId<MalinovLobbyTilePrototype> tile,
        MalinovLobbyTilePlacement placement,
        List<ProtoId<MalinovLobbyTilePrototype>> inTheWay)
    {
        inTheWay.Clear();
        foreach (var (other, otherPlacement) in placements)
        {
            if (other != tile && otherPlacement.Overlaps(placement))
                inTheWay.Add(other);
        }
    }

    /// <summary>
    /// Puts the only tile in the way of the moved tile into the moved tile's old place, if it fits there.
    /// </summary>
    private static bool TrySwap(
        Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> board,
        ProtoId<MalinovLobbyTilePrototype> moved,
        MalinovLobbyTilePlacement from,
        MalinovLobbyTilePlacement to)
    {
        ProtoId<MalinovLobbyTilePrototype>? only = null;
        foreach (var (other, placement) in board)
        {
            if (other == moved || !placement.Overlaps(to))
                continue;

            if (only != null)
                return false;

            only = other;
        }

        if (only is not { } swapped)
            return false;

        var swappedTo = board[swapped] with { Column = from.Column, Row = from.Row };
        if (swappedTo.Right > MalinovLobbyLayouts.BoardColumns
            || swappedTo.Bottom > MalinovLobbyLayouts.MaxRows
            || swappedTo.Overlaps(to))
        {
            return false;
        }

        foreach (var (other, placement) in board)
        {
            if (other != moved && other != swapped && placement.Overlaps(swappedTo))
                return false;
        }

        board[swapped] = swappedTo;
        return true;
    }

    /// <summary>
    /// Goes through the tiles in reading order, starting with <paramref name="first"/> if given, and moves every tile
    /// that overlaps an earlier one down below it.
    /// </summary>
    /// <returns><c>false</c> when <paramref name="limitRows"/> is set and a tile would go past <see cref="MalinovLobbyLayouts.MaxRows"/>.</returns>
    private static bool SettleInReadingOrder(
        Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> board,
        ProtoId<MalinovLobbyTilePrototype>? first,
        bool limitRows)
    {
        var order = new List<(ProtoId<MalinovLobbyTilePrototype> Id, MalinovLobbyTilePlacement Placement)>(board.Count);
        foreach (var (id, placement) in board)
        {
            if (id != first)
                order.Add((id, placement));
        }

        // The id settles ties, so the result never depends on the order of the dictionary.
        order.Sort((a, b) =>
        {
            var byPlace = MalinovLobbyLayouts.CompareReadingOrder(a.Placement, b.Placement);
            return byPlace != 0 ? byPlace : string.CompareOrdinal(a.Id.Id, b.Id.Id);
        });

        var settled = new List<MalinovLobbyTilePlacement>(board.Count);
        if (first is { } firstId)
            settled.Add(board[firstId]);

        foreach (var (id, start) in order)
        {
            var placement = start;
            while (GetLowestOverlapped(placement, settled) is { } below)
            {
                placement = placement with { Row = below };
            }

            if (limitRows && placement.Bottom > MalinovLobbyLayouts.MaxRows)
                return false;

            board[id] = placement;
            settled.Add(placement);
        }

        return true;
    }

    /// <summary>
    /// <paramref name="value"/> within <paramref name="min"/> and <paramref name="max"/>; when they cross, <paramref name="min"/>
    /// wins and <see cref="MalinovLobbyLayouts.Fit"/> settles the rest.
    /// </summary>
    private static int Bound(int value, int min, int max)
    {
        return Math.Max(min, Math.Min(value, max));
    }

    /// <returns>The row below the lowest of <paramref name="settled"/> that <paramref name="placement"/> overlaps, if any.</returns>
    private static int? GetLowestOverlapped(MalinovLobbyTilePlacement placement, List<MalinovLobbyTilePlacement> settled)
    {
        int? below = null;
        foreach (var other in settled)
        {
            if (placement.Overlaps(other))
                below = Math.Max(below ?? 0, other.Bottom);
        }

        return below;
    }
}
