#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._MalinovStation.Lobby;
using Content.Shared._MalinovStation.Lobby;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests._MalinovStation.Lobby;

[TestFixture]
[TestOf(typeof(MalinovLobbyTileLayout))]
[TestOf(typeof(MalinovLobbyPhases))]
public sealed class MalinovLobbyTileLayoutTest
{
    private const float Gap = 8f;
    private const float RowHeight = 100f;

    [Test]
    public void EmptyLayoutHasNoRows()
    {
        var placements = new List<MalinovLobbyTilePlacement> { new(1, 1, 1, 1) };
        var rows = MalinovLobbyTileLayout.Place(Array.Empty<Vector2i>(), 6, placements);

        Assert.Multiple(() =>
        {
            Assert.That(rows, Is.Zero);
            Assert.That(placements, Is.Empty, "Stale placements from a previous pass must be cleared.");
        });
    }

    [Test]
    public void TilesFillARowBeforeStartingTheNext()
    {
        var placements = new List<MalinovLobbyTilePlacement>();
        var rows = MalinovLobbyTileLayout.Place(
            new[] { new Vector2i(2, 1), new Vector2i(2, 1), new Vector2i(2, 1), new Vector2i(2, 1) },
            6,
            placements);

        Assert.That(rows, Is.EqualTo(2));
        Assert.That(placements, Is.EqualTo(new[]
        {
            new MalinovLobbyTilePlacement(0, 0, 2, 1),
            new MalinovLobbyTilePlacement(2, 0, 2, 1),
            new MalinovLobbyTilePlacement(4, 0, 2, 1),
            new MalinovLobbyTilePlacement(0, 1, 2, 1),
        }));
    }

    [Test]
    public void WrappedTileSkipsCellsOfTallerTiles()
    {
        var placements = new List<MalinovLobbyTilePlacement>();
        var rows = MalinovLobbyTileLayout.Place(
            new[] { new Vector2i(2, 2), new Vector2i(4, 1), new Vector2i(2, 1) },
            6,
            placements);

        Assert.That(rows, Is.EqualTo(2));
        Assert.That(placements, Is.EqualTo(new[]
        {
            new MalinovLobbyTilePlacement(0, 0, 2, 2),
            new MalinovLobbyTilePlacement(2, 0, 4, 1),
            // Row 1 column 0 is still covered by the tall first tile.
            new MalinovLobbyTilePlacement(2, 1, 2, 1),
        }));
    }

    [Test]
    public void GapsLeftBehindStayEmpty()
    {
        // Tiles keep reading order: the last tile does not jump back into the row the wide tile could not use.
        var placements = new List<MalinovLobbyTilePlacement>();
        MalinovLobbyTileLayout.Place(new[] { new Vector2i(2, 1), new Vector2i(6, 1), new Vector2i(2, 1) }, 6, placements);

        Assert.That(placements, Is.EqualTo(new[]
        {
            new MalinovLobbyTilePlacement(0, 0, 2, 1),
            new MalinovLobbyTilePlacement(0, 1, 6, 1),
            new MalinovLobbyTilePlacement(0, 2, 2, 1),
        }));
    }

    [Test]
    public void DefaultLobbyKeepsItsShape()
    {
        // Ready, players and round on top, the chat with character, menu and server beside it,
        // what's new and credits below.
        var placements = new List<MalinovLobbyTilePlacement>();
        MalinovLobbyTileLayout.Place(LobbyTiles(), 6, placements);

        Assert.That(placements, Is.EqualTo(new[]
        {
            new MalinovLobbyTilePlacement(0, 0, 2, 1),
            new MalinovLobbyTilePlacement(2, 0, 2, 1),
            new MalinovLobbyTilePlacement(4, 0, 2, 1),
            new MalinovLobbyTilePlacement(0, 1, 4, 4),
            new MalinovLobbyTilePlacement(4, 1, 2, 2),
            new MalinovLobbyTilePlacement(4, 3, 2, 1),
            new MalinovLobbyTilePlacement(4, 4, 2, 1),
            new MalinovLobbyTilePlacement(0, 5, 4, 2),
            new MalinovLobbyTilePlacement(4, 5, 2, 2),
        }));
    }

    /// <summary>
    /// Sizes of the default lobby tiles in their default order.
    /// </summary>
    private static List<Vector2i> LobbyTiles()
    {
        return new List<Vector2i>
        {
            new(2, 1), // ready
            new(2, 1), // players
            new(2, 1), // round
            new(4, 4), // chat
            new(2, 2), // character
            new(2, 1), // menu
            new(2, 1), // server
            new(4, 2), // what's new
            new(2, 2), // credits
        };
    }

    [Test]
    public void TileWiderThanTheGridIsClampedToItsWidth()
    {
        var placements = new List<MalinovLobbyTilePlacement>();
        var rows = MalinovLobbyTileLayout.Place(new[] { new Vector2i(4, 3) }, 2, placements);

        Assert.Multiple(() =>
        {
            Assert.That(rows, Is.EqualTo(3));
            Assert.That(placements, Is.EqualTo(new[] { new MalinovLobbyTilePlacement(0, 0, 2, 3) }));
        });
    }

    [Test]
    public void NonPositiveSizesBecomeOneCell()
    {
        var placements = new List<MalinovLobbyTilePlacement>();
        MalinovLobbyTileLayout.Place(new[] { new Vector2i(0, -2) }, 4, placements);

        Assert.That(placements, Is.EqualTo(new[] { new MalinovLobbyTilePlacement(0, 0, 1, 1) }));
    }

    [Test]
    public void FewerColumnsWrapTheSameTilesIntoMoreRows()
    {
        var sizes = new[] { new Vector2i(2, 1), new Vector2i(2, 1), new Vector2i(2, 1) };
        var placements = new List<MalinovLobbyTilePlacement>();

        var wide = MalinovLobbyTileLayout.Place(sizes, 6, placements);
        var narrow = MalinovLobbyTileLayout.Place(sizes, 2, placements);

        Assert.Multiple(() =>
        {
            Assert.That(wide, Is.EqualTo(1));
            Assert.That(narrow, Is.EqualTo(3));
            Assert.That(placements[2], Is.EqualTo(new MalinovLobbyTilePlacement(0, 2, 2, 1)));
        });
    }

    [Test]
    public void CellWidthSplitsSpaceBetweenColumnsAndGaps()
    {
        Assert.That(MalinovLobbyTileLayout.GetCellWidth(1260f, 6, Gap), Is.EqualTo(203.333f).Within(0.01f));
    }

    [Test]
    public void RectSpansCellsRowsAndTheGapsBetweenThem()
    {
        var rows = new List<float> { 150f, 100f, 120f };
        var rect = MalinovLobbyTileLayout.GetRect(new MalinovLobbyTilePlacement(2, 1, 2, 2), 100f, rows, Gap);

        Assert.Multiple(() =>
        {
            Assert.That(rect.Left, Is.EqualTo(216f));
            Assert.That(rect.Top, Is.EqualTo(158f)); // First row and one gap.
            Assert.That(rect.Width, Is.EqualTo(208f));
            Assert.That(rect.Height, Is.EqualTo(228f)); // 100 + 120 and the gap between them.
            Assert.That(MalinovLobbyTileLayout.GetSpanHeight(rows, 0, 3, Gap), Is.EqualTo(386f));
        });
    }

    [TestCase(221f, 163f, 2, 1)]
    [TestCase(107f, 10f, 0, 0)] // The gap after a cell counts to that cell.
    [TestCase(0f, 157f, 0, 0)]
    [TestCase(0f, 158f, 0, 1)]
    public void CellUnderAPointFollowsColumnsAndRows(float x, float y, int column, int row)
    {
        var rows = new List<float> { 150f, 100f };

        var cell = MalinovLobbyTileLayout.GetCell(new Vector2(x, y), 100f, rows, RowHeight, Gap, 6, 64);

        Assert.That(cell, Is.EqualTo(new Vector2i(column, row)));
    }

    [Test]
    public void CellsBelowTheRowsHaveTheRowHeight()
    {
        // 150 + 8 for the only row, then rows of 100 + 8.
        var rows = new List<float> { 150f };

        var cell = MalinovLobbyTileLayout.GetCell(new Vector2(0f, 316f), 100f, rows, RowHeight, Gap, 6, 64);

        Assert.That(cell, Is.EqualTo(new Vector2i(0, 2)));
    }

    [TestCase(-50f, -50f, 0, 0)]
    [TestCase(5000f, 100000f, 5, 63)]
    public void CellIsClampedToTheBoard(float x, float y, int column, int row)
    {
        var cell = MalinovLobbyTileLayout.GetCell(new Vector2(x, y), 100f, new List<float>(), RowHeight, Gap, 6, 64);

        Assert.That(cell, Is.EqualTo(new Vector2i(column, row)));
    }

    [Test]
    public void SlideMovesThePlaceAndKeepsTheNewSize()
    {
        var from = UIBox2.FromDimensions(new Vector2(100f, 0f), new Vector2(200f, 100f));
        var to = UIBox2.FromDimensions(new Vector2(500f, 200f), new Vector2(300f, 150f));

        var start = MalinovLobbyTileLayout.Slide(from, to, 0f);
        var middle = MalinovLobbyTileLayout.Slide(from, to, 0.5f);
        var end = MalinovLobbyTileLayout.Slide(from, to, 1f);
        var past = MalinovLobbyTileLayout.Slide(from, to, 2f);

        Assert.Multiple(() =>
        {
            Assert.That(start.TopLeft, Is.EqualTo(from.TopLeft));
            Assert.That(start.Size, Is.EqualTo(to.Size));
            // Easing out: past halfway by half the time.
            Assert.That(middle.Left, Is.GreaterThan(300f).And.LessThan(500f));
            Assert.That(middle.Top, Is.GreaterThan(100f).And.LessThan(200f));
            Assert.That(end, Is.EqualTo(to));
            Assert.That(past, Is.EqualTo(to));
        });
    }

    [Test]
    public void ShortContentKeepsRowsAtTheirHeight()
    {
        var rows = FitRows(new[] { new MalinovLobbyTilePlacement(0, 0, 2, 1), new MalinovLobbyTilePlacement(2, 0, 2, 2) },
            new[] { 40f, 50f },
            2);

        Assert.That(rows, Is.EqualTo(new[] { RowHeight, RowHeight }));
    }

    [Test]
    public void TallSingleRowTileGrowsOnlyItsOwnRow()
    {
        var rows = FitRows(new[] { new MalinovLobbyTilePlacement(0, 0, 2, 1), new MalinovLobbyTilePlacement(0, 1, 2, 1) },
            new[] { 150f, 30f },
            2);

        Assert.That(rows, Is.EqualTo(new[] { 150f, RowHeight }));
    }

    [Test]
    public void MultiRowTileSpreadsMissingHeightOverItsRows()
    {
        // Two rows give 100 + 8 + 100 = 208; the missing 52 is split between them.
        var rows = FitRows(new[] { new MalinovLobbyTilePlacement(0, 0, 2, 2) }, new[] { 260f }, 2);

        Assert.That(rows, Is.EqualTo(new[] { 126f, 126f }));
    }

    [Test]
    public void MultiRowTileCountsHeightItsRowsAlreadyGot()
    {
        // The first row already grew to 150 for its own tile, so 150 + 8 + 100 = 258 covers the 250.
        var rows = FitRows(new[] { new MalinovLobbyTilePlacement(0, 0, 2, 1), new MalinovLobbyTilePlacement(2, 0, 2, 2) },
            new[] { 150f, 250f },
            2);

        Assert.That(rows, Is.EqualTo(new[] { 150f, RowHeight }));
    }

    [Test]
    public void NoTilesMeanNoRows()
    {
        var rows = new List<float> { 1f, 2f };
        MalinovLobbyTileLayout.FitRowHeights(Array.Empty<MalinovLobbyTilePlacement>(), Array.Empty<float>(), 0, RowHeight, Gap, rows);

        Assert.That(rows, Is.Empty, "Stale row heights from a previous pass must be cleared.");
    }

    private static List<float> FitRows(MalinovLobbyTilePlacement[] placements, float[] heights, int rowCount)
    {
        var rows = new List<float>();
        MalinovLobbyTileLayout.FitRowHeights(placements, heights, rowCount, RowHeight, Gap, rows);
        return rows;
    }

    [TestCase(true, false, MalinovLobbyPhase.InRound)]
    [TestCase(true, true, MalinovLobbyPhase.InRound)]
    [TestCase(false, true, MalinovLobbyPhase.PreRound)]
    [TestCase(false, false, MalinovLobbyPhase.Countdown)]
    public void PhaseFollowsTickerState(bool started, bool paused, MalinovLobbyPhase expected)
    {
        Assert.That(MalinovLobbyPhases.Get(started, paused), Is.EqualTo(expected));
    }
}
