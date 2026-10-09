using Robust.Shared.Prototypes;

namespace Content.Shared._MalinovStation.Lobby;

/// <summary>
/// Rules of player lobby layouts, shared by the server that checks and stores them and the client that applies them.
/// </summary>
public static class MalinovLobbyLayouts
{
    /// <summary>
    /// Columns of the lobby board. Every screen shows all of them; narrow ones make the cells narrower.
    /// </summary>
    public const int BoardColumns = 6;

    /// <summary>
    /// Rows a tile may reach down to; far below any board a player arranges,
    /// it bounds what a client can make the server keep.
    /// </summary>
    public const int MaxRows = 64;

    /// <summary>
    /// Entries of a layout looked at; far more than the lobby has tiles,
    /// it bounds what a client can make the server go through or store.
    /// </summary>
    public const int MaxTiles = 64;

    /// <summary>
    /// Longest tile id accepted from a client.
    /// </summary>
    public const int MaxIdLength = 64;

    /// <summary>
    /// What the server keeps of a layout sent by a client: known tiles only, each fitted to its size limits
    /// and the board, without tiles that overlap ones before them in reading order; only hideable tiles hidden.
    /// Both collections are cut at <see cref="MaxTiles"/> entries before anything else.
    /// </summary>
    public static MalinovLobbyLayout Sanitize(MalinovLobbyLayout layout, IPrototypeManager prototypes)
    {
        var sanitized = new MalinovLobbyLayout();
        AddKnownPlaces(layout.Places, sanitized.Places, prototypes);
        AddKnownHidden(layout.Hidden, sanitized.Hidden, prototypes);
        return sanitized;
    }

    /// <summary>
    /// Fits <paramref name="placement"/> to a tile: the size within <paramref name="smallest"/> and
    /// <paramref name="largest"/> and the board, then every cell on the board.
    /// </summary>
    public static MalinovLobbyTilePlacement Fit(MalinovLobbyTilePlacement placement, Vector2i smallest, Vector2i largest)
    {
        // Min and Max rather than Math.Clamp for the tile limits: limits a prototype got wrong
        // (smallest above largest) must not throw on data from a client.
        var width = Math.Clamp(Math.Max(Math.Min(placement.Width, largest.X), smallest.X), 1, BoardColumns);
        var height = Math.Clamp(Math.Max(Math.Min(placement.Height, largest.Y), smallest.Y), 1, MaxRows);
        var column = Math.Clamp(placement.Column, 0, BoardColumns - width);
        var row = Math.Clamp(placement.Row, 0, MaxRows - height);
        return new MalinovLobbyTilePlacement(column, row, width, height);
    }

    /// <summary>
    /// The layout to keep for tiles at <paramref name="places"/> with <paramref name="hidden"/> hidden.
    /// While they match the defaults it is the default layout, so later changes of the defaults still reach the player.
    /// </summary>
    public static MalinovLobbyLayout Create(
        IReadOnlyDictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> places,
        IEnumerable<ProtoId<MalinovLobbyTilePrototype>> hidden,
        IReadOnlyDictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> defaults)
    {
        var hiddenList = new List<ProtoId<MalinovLobbyTilePrototype>>(hidden);
        if (hiddenList.Count == 0 && SamePlaces(places, defaults))
            return new MalinovLobbyLayout();

        var placesCopy = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>(places);
        return new MalinovLobbyLayout(placesCopy, hiddenList);
    }

    /// <summary>
    /// Orders placements as the board is read: by row, then by column.
    /// </summary>
    public static int CompareReadingOrder(MalinovLobbyTilePlacement a, MalinovLobbyTilePlacement b)
    {
        var byRow = a.Row.CompareTo(b.Row);
        return byRow != 0 ? byRow : a.Column.CompareTo(b.Column);
    }

    private static void AddKnownPlaces(
        Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>? from,
        Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> to,
        IPrototypeManager prototypes)
    {
        // Data from a client may arrive with nulls where the types allow none.
        if (from == null)
            return;

        var known = new List<(ProtoId<MalinovLobbyTilePrototype> Id, MalinovLobbyTilePlacement Placement)>();
        var looked = 0;
        foreach (var (id, placement) in from)
        {
            if (looked++ >= MaxTiles)
                break;

            if (string.IsNullOrEmpty(id.Id) || id.Id.Length > MaxIdLength)
                continue;

            if (!prototypes.TryIndex(id, out var tile))
                continue;

            known.Add((id, Fit(placement, tile.SmallestSize, tile.LargestSize)));
        }

        // Of two overlapping tiles the one read first stays; the id settles ties, so the result never depends on
        // the order the client sent them in.
        known.Sort((a, b) =>
        {
            var byPlace = CompareReadingOrder(a.Placement, b.Placement);
            return byPlace != 0 ? byPlace : string.CompareOrdinal(a.Id.Id, b.Id.Id);
        });

        foreach (var (id, placement) in known)
        {
            if (!OverlapsAny(placement, to))
                to.Add(id, placement);
        }
    }

    private static void AddKnownHidden(
        List<ProtoId<MalinovLobbyTilePrototype>>? from,
        List<ProtoId<MalinovLobbyTilePrototype>> to,
        IPrototypeManager prototypes)
    {
        // Data from a client may arrive with nulls where the types allow none.
        if (from == null)
            return;

        var count = Math.Min(from.Count, MaxTiles);
        for (var i = 0; i < count; i++)
        {
            var id = from[i];
            if (string.IsNullOrEmpty(id.Id) || id.Id.Length > MaxIdLength || to.Contains(id))
                continue;

            if (!prototypes.TryIndex(id, out var tile) || !tile.Hideable)
                continue;

            to.Add(id);
        }
    }

    private static bool OverlapsAny(
        MalinovLobbyTilePlacement placement,
        Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> placed)
    {
        foreach (var other in placed.Values)
        {
            if (placement.Overlaps(other))
                return true;
        }

        return false;
    }

    private static bool SamePlaces(
        IReadOnlyDictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> places,
        IReadOnlyDictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> defaults)
    {
        if (places.Count != defaults.Count)
            return false;

        foreach (var (id, placement) in places)
        {
            if (!defaults.TryGetValue(id, out var defaultPlacement) || defaultPlacement != placement)
                return false;
        }

        return true;
    }
}
