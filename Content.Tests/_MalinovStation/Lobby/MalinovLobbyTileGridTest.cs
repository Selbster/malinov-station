#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._MalinovStation.Lobby;
using Content.Shared._MalinovStation.Lobby;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML.Proxy;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.Tests._MalinovStation.Lobby;

[TestFixture]
[TestOf(typeof(MalinovLobbyTileGrid))]
[TestOf(typeof(MalinovLobbyTileControl))]
public sealed class MalinovLobbyTileGridTest : RobustUnitTest
{
    public override UnitTestProject Project => UnitTestProject.Client;

    protected override void OverrideIoC()
    {
        base.OverrideIoC();
        var proxyInterface = typeof(XamlProxyManagerStub).Assembly
            .GetType("Robust.Client.UserInterface.XAML.Proxy.IXamlProxyManager")!;
        IoCManager.Instance!.Register(proxyInterface, typeof(XamlProxyManagerStub), overwrite: true);
    }

    [OneTimeSetUp]
    public void SetupUi()
    {
        IoCManager.Resolve<IUserInterfaceManager>().InitializeTesting();
    }

    [Test]
    public void AdoptMovesContentIntoTheTile()
    {
        var pool = new Control();
        var content = new Control();
        pool.AddChild(content);
        var tile = new MalinovLobbyTileControl();

        tile.Adopt(content);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Content, Is.SameAs(content));
            Assert.That(content.Parent, Is.SameAs(tile.ContentSlot));
            Assert.That(pool.ChildCount, Is.Zero);
        });
    }

    [Test]
    public void ReleaseReturnsContentToWhereItCameFrom()
    {
        var pool = new Control();
        var content = new Control();
        pool.AddChild(content);
        var tile = new MalinovLobbyTileControl();
        tile.Adopt(content);

        tile.Release();

        Assert.Multiple(() =>
        {
            Assert.That(tile.Content, Is.Null);
            Assert.That(content.Parent, Is.SameAs(pool));
            Assert.That(tile.ContentSlot.ChildCount, Is.Zero);
        });
    }

    [Test]
    public void AdoptingNewContentReleasesThePreviousOne()
    {
        var pool = new Control();
        var first = new Control();
        var second = new Control();
        pool.AddChild(first);
        pool.AddChild(second);
        var tile = new MalinovLobbyTileControl();

        tile.Adopt(first);
        tile.Adopt(second);

        Assert.Multiple(() =>
        {
            Assert.That(tile.Content, Is.SameAs(second));
            Assert.That(first.Parent, Is.SameAs(pool));
            Assert.That(second.Parent, Is.SameAs(tile.ContentSlot));
        });
    }

    [Test]
    public void ReleasingParentlessContentLeavesItOrphaned()
    {
        var content = new Control();
        var tile = new MalinovLobbyTileControl();
        tile.Adopt(content);

        tile.Release();

        Assert.That(content.Parent, Is.Null);
    }

    [Test]
    public void TileStandsOnItsCell()
    {
        var grid = new MalinovLobbyTileGrid();
        var tile = Tile(grid, 4, 2, 2, 1);

        Layout(grid, 1260f);

        // Six columns of 203.33 with gaps of 8; rows of 100 with gaps of 8.
        AssertBox(tile, 845.333f, 216f, 414.667f, MalinovLobbyTileGrid.RowHeight);
    }

    [Test]
    public void TallContentGrowsItsRowAndPushesLaterRowsDown()
    {
        var grid = new MalinovLobbyTileGrid();
        var tall = Tile(grid, 0, 0, 2, 1);
        var neighbour = Tile(grid, 2, 0, 2, 1);
        var below = Tile(grid, 0, 1, 6, 1);
        tall.Adopt(new Control { MinHeight = 250f });

        Layout(grid, 1260f);

        Assert.Multiple(() =>
        {
            Assert.That(tall.Size.Y, Is.GreaterThanOrEqualTo(250f), "The row must fit the tall content.");
            Assert.That(neighbour.Size.Y, Is.EqualTo(tall.Size.Y), "Tiles of one row share its height.");
            Assert.That(below.Position.Y, Is.EqualTo(tall.Size.Y + MalinovLobbyTileGrid.Gap));
            Assert.That(below.Size.Y, Is.EqualTo(MalinovLobbyTileGrid.RowHeight), "Rows with short content keep their height.");
            Assert.That(grid.DesiredSize.Y, Is.EqualTo(tall.Size.Y + MalinovLobbyTileGrid.Gap + MalinovLobbyTileGrid.RowHeight));
        });
    }

    [Test]
    public void GridDoesNotStretchRowsToTheAvailableHeight()
    {
        var (grid, small, tall, last) = MakeGrid();

        grid.Measure(new Vector2(1260f, float.PositiveInfinity));
        grid.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(1260f, 600f)));

        // Six columns of 203.33 (a two-cell tile is 2 * 203.33 + 8 = 414.67 wide).
        // Rows keep their height even though 600 is available: the rest stays lobby background.
        const float row = MalinovLobbyTileGrid.RowHeight;
        const float twoRows = 2 * row + MalinovLobbyTileGrid.Gap;
        Assert.Multiple(() =>
        {
            Assert.That(grid.DesiredSize.Y, Is.EqualTo(twoRows));
            AssertBox(small, 0f, 0f, 414.667f, row);
            AssertBox(tall, 422.667f, 0f, 414.667f, twoRows);
            AssertBox(last, 845.333f, 0f, 414.667f, row);
        });
    }

    [Test]
    public void NarrowWindowKeepsSixColumns()
    {
        var (grid, small, tall, last) = MakeGrid();

        Layout(grid, 1000f);

        // Six cells of 160: a two-cell tile is 328 wide, and nothing wraps to another row.
        const float row = MalinovLobbyTileGrid.RowHeight;
        Assert.Multiple(() =>
        {
            AssertBox(small, 0f, 0f, 328f, row);
            AssertBox(tall, 336f, 0f, 328f, 2 * row + MalinovLobbyTileGrid.Gap);
            AssertBox(last, 672f, 0f, 328f, row);
        });
    }

    [Test]
    public void VeryNarrowWindowScrollsSideways()
    {
        // As in the lobby: the board sits in a scroll container that scrolls both ways.
        var (grid, _, _, last) = MakeGrid();
        var scroll = new ScrollContainer { HScrollEnabled = true };
        scroll.AddChild(grid);

        scroll.Measure(new Vector2(600f, 400f));
        scroll.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(600f, 400f)));

        Assert.Multiple(() =>
        {
            Assert.That(grid.Size.X, Is.EqualTo(MalinovLobbyTileGrid.MinBoardWidth), "Cells stop shrinking at their smallest width.");
            Assert.That(last.Position.X + last.Size.X, Is.EqualTo(MalinovLobbyTileGrid.MinBoardWidth).Within(0.01f));
        });
    }

    [Test]
    public void ScrollingBoardIsMeasuredForTheWidthItGets()
    {
        // A scroll container that scrolls sideways gives no width to measure for, so the grid measures for the width
        // it was last laid out at, and measures again once it is laid out at another one.
        var grid = new MalinovLobbyTileGrid();
        var tile = Tile(grid, 0, 0, 2, 1);
        var probe = new WidthProbe();
        tile.Adopt(probe);
        var scroll = new ScrollContainer { HScrollEnabled = true };
        scroll.AddChild(grid);

        scroll.Measure(new Vector2(1280f, 400f));
        scroll.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(1280f, 400f)));
        var remeasureAsked = !grid.IsMeasureValid;
        grid.Measure(new Vector2(float.PositiveInfinity, float.PositiveInfinity));

        // Two cells of (1280 - 40) / 6 and the gap between them, less the margins of the content slot.
        var twoCells = 2 * (1280f - 5 * MalinovLobbyTileGrid.Gap) / 6 + MalinovLobbyTileGrid.Gap;
        Assert.Multiple(() =>
        {
            Assert.That(remeasureAsked, Is.True);
            Assert.That(probe.LastWidth, Is.EqualTo(twoCells - 8f).Within(0.01f));
        });
    }

    [Test]
    public void WideScreenBoardReachesBothEdges()
    {
        var (grid, small, _, last) = MakeGrid();
        const float screen = 2560f;

        grid.Measure(new Vector2(screen, float.PositiveInfinity));
        grid.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(screen, 600f)));

        Assert.Multiple(() =>
        {
            Assert.That(grid.DesiredSize.X, Is.EqualTo(screen));
            Assert.That(grid.Position.X, Is.Zero);
            Assert.That(grid.Size.X, Is.EqualTo(screen));
            Assert.That(small.Position.X, Is.Zero, "Tiles of the first column stand at the left edge of the screen.");
            Assert.That(last.Position.X + last.Size.X, Is.EqualTo(screen).Within(0.01f), "Tiles of the last column stand at the right edge.");
        });
    }

    [Test]
    public void AccentSwitchesTheHeaderStyle()
    {
        var tile = new MalinovLobbyTileControl();
        tile.SetTitle("Ready");

        tile.Accented = true;
        var accented = tile.Header.HasStyleClass(MalinovLobbyTileControl.StyleClassHeaderAccent);
        tile.Accented = false;

        Assert.Multiple(() =>
        {
            Assert.That(accented, Is.True);
            Assert.That(tile.Header.HasStyleClass(MalinovLobbyTileControl.StyleClassHeaderAccent), Is.False);
            Assert.That(tile.Header.HasStyleClass(MalinovLobbyTileControl.StyleClassHeader), Is.True,
                "The accent adds to the header style instead of replacing it.");
        });
    }

    [Test]
    public void TitleFollowsTheAccentBothWays()
    {
        var tile = new MalinovLobbyTileControl();
        tile.SetTitle("Ready");
        var title = tile.Header.Children.OfType<Label>().Single();
        // Stands in for the lobby sheetlet: the title takes its color from the header's accent class.
        tile.Stylesheet = new Stylesheet(new[]
        {
            new StyleRule(
                new SelectorChild(
                    new SelectorElement(typeof(PanelContainer), new[] { MalinovLobbyTileControl.StyleClassHeaderAccent }, null, null),
                    new SelectorElement(typeof(Label), null, null, null)),
                new[] { new StyleProperty(Label.StylePropertyFontColor, Color.Black) }),
        });
        tile.ForceRunStyleUpdate();

        tile.Accented = true;
        var accentColor = title.TryGetStyleProperty<Color>(Label.StylePropertyFontColor, out _);
        tile.Accented = false;

        // The engine restyles only the control whose class changed, so a stale title kept the accent color
        // on the plain dark header and could not be read.
        Assert.Multiple(() =>
        {
            Assert.That(accentColor, Is.True, "The accented header recolors its title.");
            Assert.That(title.TryGetStyleProperty<Color>(Label.StylePropertyFontColor, out _), Is.False,
                "The title gets its usual color back with the accent.");
        });
    }

    [Test]
    public void MovedTileIsLaidOutAgain()
    {
        var (grid, small, tall, last) = MakeGrid();
        Layout(grid, 1260f);

        last.TilePosition = new Vector2i(0, 2);
        Layout(grid, 1260f);

        Assert.Multiple(() =>
        {
            Assert.That(last.Position, Is.EqualTo(new Vector2(0f, 2 * (MalinovLobbyTileGrid.RowHeight + MalinovLobbyTileGrid.Gap))));
            Assert.That(small.Position.X, Is.Zero, "The other tiles stay where they are.");
            Assert.That(tall.Position.X, Is.EqualTo(422.667f).Within(0.01f));
        });
    }

    [Test]
    public void ResizedTileIsLaidOutAgain()
    {
        var (grid, small, _, _) = MakeGrid();
        Layout(grid, 1260f);

        small.TileSize = new Vector2i(1, 3);
        Layout(grid, 1260f);

        AssertBox(small, 0f, 0f, 203.333f, 3 * MalinovLobbyTileGrid.RowHeight + 2 * MalinovLobbyTileGrid.Gap);
    }

    [Test]
    public void TilesSlideToTheirNewPlace()
    {
        var (grid, _, _, last) = MakeGrid();
        grid.Animated = true;
        Layout(grid, 1260f);
        var startX = last.Position.X;

        last.TilePosition = new Vector2i(0, 2);
        Layout(grid, 1260f);
        var atStart = last.Position.X;
        MalinovLobbyTestInput.Frame(grid, MalinovLobbyTileGrid.SlideSeconds / 2);
        Layout(grid, 1260f);
        var halfway = last.Position.X;
        MalinovLobbyTestInput.Frame(grid, MalinovLobbyTileGrid.SlideSeconds);
        Layout(grid, 1260f);

        Assert.Multiple(() =>
        {
            Assert.That(atStart, Is.EqualTo(startX).Within(0.01f), "The slide starts where the tile was.");
            Assert.That(halfway, Is.LessThan(startX).And.GreaterThan(0f));
            Assert.That(last.Position.X, Is.Zero, "The slide ends at the new place.");
            Assert.That(last.Size.X, Is.EqualTo(414.667f).Within(0.01f), "Only the place moves; the size is the new one at once.");
        });
    }

    [Test]
    public void ShownTileAppearsInPlace()
    {
        var (grid, small, tall, _) = MakeGrid();
        grid.Animated = true;
        small.Visible = false;
        Layout(grid, 1260f);

        small.Visible = true;
        Layout(grid, 1260f);

        Assert.Multiple(() =>
        {
            Assert.That(small.Position.X, Is.Zero, "A tile that was not shown has no place to slide from.");
            Assert.That(tall.Position.X, Is.EqualTo(422.667f).Within(0.01f), "The other tiles do not make room.");
        });
    }

    [Test]
    public void StillGridMovesTilesAtOnce()
    {
        var (grid, _, _, last) = MakeGrid();
        Layout(grid, 1260f);

        last.TilePosition = new Vector2i(0, 2);
        Layout(grid, 1260f);

        Assert.That(last.Position.X, Is.Zero);
    }

    // Common windows at interface scale 1, and 1280 wide at scale 1.5.
    [TestCase(600f)]
    [TestCase(853f)]
    [TestCase(1280f)]
    [TestCase(1600f)]
    [TestCase(1920f)]
    [TestCase(2560f)]
    public void DefaultLobbyTilesNeverOverlap(float width)
    {
        var grid = new MalinovLobbyTileGrid();
        var sizes = new List<Vector2i>
        {
            new(2, 1), new(2, 1), new(2, 1), new(4, 4), new(2, 2), new(2, 1), new(2, 1), new(4, 2), new(2, 2),
        };
        var placements = new List<MalinovLobbyTilePlacement>();
        MalinovLobbyTileLayout.Place(sizes, MalinovLobbyLayouts.BoardColumns, placements);
        var tiles = placements.Select(place => Tile(grid, place.Column, place.Row, place.Width, place.Height)).ToList();

        Layout(grid, width);

        var gridWidth = Math.Max(width, MalinovLobbyTileGrid.MinBoardWidth);
        Assert.Multiple(() =>
        {
            for (var i = 0; i < tiles.Count; i++)
            {
                var box = UIBox2.FromDimensions(tiles[i].Position, tiles[i].Size);
                Assert.That(box.Left, Is.GreaterThanOrEqualTo(-0.01f), $"Tile {i} starts left of the grid.");
                Assert.That(box.Right, Is.LessThanOrEqualTo(gridWidth + 0.01f), $"Tile {i} ends right of the grid.");

                for (var j = i + 1; j < tiles.Count; j++)
                {
                    var other = UIBox2.FromDimensions(tiles[j].Position, tiles[j].Size);
                    var overlapWidth = Math.Min(box.Right, other.Right) - Math.Max(box.Left, other.Left);
                    var overlapHeight = Math.Min(box.Bottom, other.Bottom) - Math.Max(box.Top, other.Top);
                    Assert.That(overlapWidth <= 0.01f || overlapHeight <= 0.01f, Is.True,
                        $"Tiles {i} and {j} overlap at width {width}.");
                }
            }
        });
    }

    [Test]
    public void HiddenTileLeavesItsCellsEmpty()
    {
        var (grid, small, tall, last) = MakeGrid();
        small.Visible = false;

        Layout(grid, 1260f);

        Assert.Multiple(() =>
        {
            Assert.That(tall.Position.X, Is.EqualTo(422.667f).Within(0.01f));
            Assert.That(last.Position.X, Is.EqualTo(845.333f).Within(0.01f));
        });
    }

    [Test]
    public void EmptyRowsBetweenTilesKeepTheirHeight()
    {
        var grid = new MalinovLobbyTileGrid();
        Tile(grid, 0, 0, 2, 1);
        var low = Tile(grid, 0, 3, 2, 1);

        Layout(grid, 1260f);

        const float row = MalinovLobbyTileGrid.RowHeight + MalinovLobbyTileGrid.Gap;
        Assert.Multiple(() =>
        {
            Assert.That(low.Position.Y, Is.EqualTo(3 * row));
            Assert.That(grid.DesiredSize.Y, Is.EqualTo(4 * row - MalinovLobbyTileGrid.Gap));
        });
    }

    [Test]
    public void BoardEndsAtTheLowestShownTile()
    {
        var (grid, _, tall, _) = MakeGrid();
        tall.Visible = false;

        Layout(grid, 1260f);

        Assert.That(grid.DesiredSize.Y, Is.EqualTo(MalinovLobbyTileGrid.RowHeight));
    }

    [Test]
    public void CellsShowWhileEditingWithRowsToDropInto()
    {
        var (grid, _, _, _) = MakeGrid();
        Layout(grid, 1260f);
        var height = grid.DesiredSize.Y;

        grid.ShowCells = true;
        Layout(grid, 1260f);
        var editingHeight = grid.DesiredSize.Y;
        grid.ShowCells = false;
        Layout(grid, 1260f);

        const float row = MalinovLobbyTileGrid.RowHeight + MalinovLobbyTileGrid.Gap;
        Assert.Multiple(() =>
        {
            Assert.That(editingHeight, Is.EqualTo(height + MalinovLobbyTileGrid.EditingRows * row));
            Assert.That(grid.DesiredSize.Y, Is.EqualTo(height));
        });
    }

    [Test]
    public void DropPreviewShowsAboveTheTilesWithoutTakingCells()
    {
        var (grid, small, _, _) = MakeGrid();
        grid.DropPreview = new MalinovLobbyTilePlacement(0, 0, 4, 3);
        Layout(grid, 1260f);
        // A tile added later still goes under the preview.
        var added = Tile(grid, 0, 2, 2, 1);
        Layout(grid, 1260f);

        var preview = grid.Children.Last();
        const float row = MalinovLobbyTileGrid.RowHeight + MalinovLobbyTileGrid.Gap;
        Assert.Multiple(() =>
        {
            Assert.That(preview.HasStyleClass(MalinovLobbyTileGrid.StyleClassDropPreview), Is.True, "The preview is drawn last.");
            Assert.That(preview.Visible, Is.True);
            AssertBox(preview, 0f, 0f, 837.333f, 3 * row - MalinovLobbyTileGrid.Gap);
            Assert.That(small.Position.X, Is.Zero, "The preview takes no cell from the tiles.");
            Assert.That(added.Position.Y, Is.EqualTo(2 * row));
        });

        grid.DropPreview = null;
        Layout(grid, 1260f);
        Assert.That(preview.Visible, Is.False);
    }

    [Test]
    public void PreviewBelowTheTilesMakesRoomForItself()
    {
        var (grid, _, _, _) = MakeGrid();
        grid.DropPreview = new MalinovLobbyTilePlacement(0, 4, 2, 2);

        Layout(grid, 1260f);

        const float row = MalinovLobbyTileGrid.RowHeight + MalinovLobbyTileGrid.Gap;
        Assert.That(grid.DesiredSize.Y, Is.EqualTo(6 * row - MalinovLobbyTileGrid.Gap));
    }

    [TestCase(850f, 220f, 4, 2)]
    [TestCase(10f, 10f, 0, 0)]
    [TestCase(1259f, 99f, 5, 0)]
    [TestCase(10f, 1000f, 0, 9)] // Below the tiles the cells go on with rows of the usual height.
    public void CellUnderAPoint(float x, float y, int column, int row)
    {
        var (grid, _, _, _) = MakeGrid();
        Layout(grid, 1260f);

        Assert.That(grid.GetCellAt(new Vector2(x, y)), Is.EqualTo(new Vector2i(column, row)));
    }

    private static (MalinovLobbyTileGrid Grid, MalinovLobbyTileControl Small, MalinovLobbyTileControl Tall, MalinovLobbyTileControl Last) MakeGrid()
    {
        var grid = new MalinovLobbyTileGrid();
        var small = Tile(grid, 0, 0, 2, 1);
        var tall = Tile(grid, 2, 0, 2, 2);
        var last = Tile(grid, 4, 0, 2, 1);
        return (grid, small, tall, last);
    }

    private static MalinovLobbyTileControl Tile(MalinovLobbyTileGrid grid, int column, int row, int width, int height)
    {
        var tile = new MalinovLobbyTileControl
        {
            TilePosition = new Vector2i(column, row),
            TileSize = new Vector2i(width, height),
        };
        grid.AddChild(tile);
        return tile;
    }

    private static void Layout(MalinovLobbyTileGrid grid, float width)
    {
        grid.Measure(new Vector2(width, float.PositiveInfinity));
        grid.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(width, 1000f)));
    }

    private static void AssertBox(Control control, float x, float y, float width, float height)
    {
        Assert.That(control.Position.X, Is.EqualTo(x).Within(0.01f), "x");
        Assert.That(control.Position.Y, Is.EqualTo(y).Within(0.01f), "y");
        Assert.That(control.Size.X, Is.EqualTo(width).Within(0.01f), "width");
        Assert.That(control.Size.Y, Is.EqualTo(height).Within(0.01f), "height");
    }

    /// <summary>
    /// Remembers the width it was last measured with.
    /// </summary>
    private sealed class WidthProbe : Control
    {
        public float LastWidth;

        protected override Vector2 MeasureOverride(Vector2 availableSize)
        {
            LastWidth = availableSize.X;
            return Vector2.Zero;
        }
    }
}
