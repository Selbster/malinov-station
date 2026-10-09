using System.Numerics;
using Content.Shared._MalinovStation.Lobby;

namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// Pure layout math of the lobby board, kept free of controls so it can be unit tested.
/// </summary>
public static class MalinovLobbyTileLayout
{
    /// <summary>
    /// Places tiles in reading order: each tile takes the first free area it fits in at or after the place
    /// of the tile before it, so gaps left behind stay empty and a tile never shows up before an earlier one.
    /// Moving a tile in the order therefore always shows. Tiles wider than the grid are clamped to its width;
    /// non-positive sizes become one cell.
    /// </summary>
    /// <param name="sizes">Tile sizes in cells (columns, rows), in placement order.</param>
    /// <param name="columns">Number of grid columns.</param>
    /// <param name="placements">Cleared and filled with one placement per size.</param>
    /// <returns>Number of rows used.</returns>
    public static int Place(IReadOnlyList<Vector2i> sizes, int columns, List<MalinovLobbyTilePlacement> placements)
    {
        placements.Clear();
        columns = Math.Max(columns, 1);

        // Row-major occupancy; grows by whole rows as tiles need them.
        var occupied = new List<bool>();
        var rows = 0;
        // Where the search for the next tile starts: right after the previous tile, in its row.
        var cursorRow = 0;
        var cursorColumn = 0;

        foreach (var size in sizes)
        {
            var width = Math.Clamp(size.X, 1, columns);
            var height = Math.Max(size.Y, 1);

            var row = cursorRow;
            var column = cursorColumn;
            while (column + width > columns || !IsFree(occupied, columns, column, row, width, height))
            {
                column++;
                if (column + width <= columns)
                    continue;

                row++;
                column = 0;
            }

            var needed = (row + height) * columns;
            while (occupied.Count < needed)
            {
                occupied.Add(false);
            }

            for (var y = row; y < row + height; y++)
            {
                for (var x = column; x < column + width; x++)
                {
                    occupied[y * columns + x] = true;
                }
            }

            placements.Add(new MalinovLobbyTilePlacement(column, row, width, height));
            rows = Math.Max(rows, row + height);
            cursorRow = row;
            cursorColumn = column + width;
        }

        return rows;
    }

    /// <summary>
    /// Width of one cell when <paramref name="columns"/> cells and the gaps between them share the width.
    /// </summary>
    public static float GetCellWidth(float availableWidth, int columns, float gap)
    {
        columns = Math.Max(columns, 1);
        return Math.Max(0f, (availableWidth - gap * (columns - 1)) / columns);
    }

    /// <summary>
    /// Width of <paramref name="span"/> cells and the gaps between them.
    /// </summary>
    public static float GetSpanWidth(int span, float cellWidth, float gap)
    {
        return span * cellWidth + (span - 1) * gap;
    }

    /// <summary>
    /// Height of <paramref name="span"/> rows starting at <paramref name="row"/>, with the gaps between them.
    /// </summary>
    public static float GetSpanHeight(IReadOnlyList<float> rowHeights, int row, int span, float gap)
    {
        var height = (span - 1) * gap;
        for (var i = row; i < row + span; i++)
        {
            height += rowHeights[i];
        }

        return height;
    }

    /// <summary>
    /// Fits row heights to the tiles: every row is at least <paramref name="minRowHeight"/> tall,
    /// and every tile gets at least its content height over the rows it spans. Rows never grow further,
    /// so space below the tiles is left free.
    /// </summary>
    /// <param name="placements">Tile placements, as returned by <see cref="Place"/>.</param>
    /// <param name="contentHeights">Natural height of each placed tile, in the same order.</param>
    /// <param name="rows">Number of rows used by the placements.</param>
    /// <param name="rowHeights">Cleared and filled with one height per row.</param>
    public static void FitRowHeights(
        IReadOnlyList<MalinovLobbyTilePlacement> placements,
        IReadOnlyList<float> contentHeights,
        int rows,
        float minRowHeight,
        float gap,
        List<float> rowHeights)
    {
        rowHeights.Clear();
        for (var i = 0; i < rows; i++)
        {
            rowHeights.Add(minRowHeight);
        }

        var maxSpan = 0;
        foreach (var placement in placements)
        {
            maxSpan = Math.Max(maxSpan, placement.Height);
        }

        // Shorter spans first: a tall one-row tile grows its own row, and multi-row tiles then
        // spread only what is still missing over all of their rows.
        for (var span = 1; span <= maxSpan; span++)
        {
            for (var i = 0; i < placements.Count; i++)
            {
                var placement = placements[i];
                if (placement.Height != span)
                    continue;

                var missing = contentHeights[i] - GetSpanHeight(rowHeights, placement.Row, span, gap);
                if (missing <= 0f)
                    continue;

                for (var row = placement.Row; row < placement.Row + span; row++)
                {
                    rowHeights[row] += missing / span;
                }
            }
        }
    }

    /// <summary>
    /// Rectangle covered by <paramref name="placement"/>, including the gaps inside its span.
    /// </summary>
    public static UIBox2 GetRect(MalinovLobbyTilePlacement placement, float cellWidth, IReadOnlyList<float> rowHeights, float gap)
    {
        var top = 0f;
        for (var row = 0; row < placement.Row; row++)
        {
            top += rowHeights[row] + gap;
        }

        var position = new Vector2(placement.Column * (cellWidth + gap), top);
        var size = new Vector2(
            GetSpanWidth(placement.Width, cellWidth, gap),
            GetSpanHeight(rowHeights, placement.Row, placement.Height, gap));

        return UIBox2.FromDimensions(position, size);
    }

    /// <summary>
    /// The cell under <paramref name="position"/>. Gaps count to the cell before them; rows past
    /// <paramref name="rowHeights"/> are <paramref name="rowHeight"/> tall, so the cells below the tiles can be found too.
    /// The cell is clamped to <paramref name="columns"/> columns and <paramref name="maxRows"/> rows.
    /// </summary>
    public static Vector2i GetCell(
        Vector2 position,
        float cellWidth,
        IReadOnlyList<float> rowHeights,
        float rowHeight,
        float gap,
        int columns,
        int maxRows)
    {
        var column = (int) MathF.Floor(position.X / (cellWidth + gap));
        column = Math.Clamp(column, 0, Math.Max(columns - 1, 0));

        var row = 0;
        var bottom = 0f;
        while (row < maxRows - 1)
        {
            bottom += (row < rowHeights.Count ? rowHeights[row] : rowHeight) + gap;
            if (position.Y < bottom)
                break;

            row++;
        }

        return new Vector2i(column, row);
    }

    /// <summary>
    /// Where a tile sliding from <paramref name="from"/> to <paramref name="to"/> is shown at <paramref name="progress"/>
    /// (0 to 1; beyond is clamped). The place eases out; the size is the new one from the start,
    /// so the content is not laid out again on every frame of the slide.
    /// </summary>
    public static UIBox2 Slide(UIBox2 from, UIBox2 to, float progress)
    {
        var eased = Easings.OutCubic(Math.Clamp(progress, 0f, 1f));
        return UIBox2.FromDimensions(Vector2.Lerp(from.TopLeft, to.TopLeft, eased), to.Size);
    }

    private static bool IsFree(List<bool> occupied, int columns, int column, int row, int width, int height)
    {
        for (var y = row; y < row + height; y++)
        {
            for (var x = column; x < column + width; x++)
            {
                var index = y * columns + x;
                if (index < occupied.Count && occupied[index])
                    return false;
            }
        }

        return true;
    }
}
