#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._MalinovStation.Lobby;
using Content.Shared._MalinovStation.Lobby;
using NUnit.Framework;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.Tests._MalinovStation.Lobby;

[TestFixture]
[TestOf(typeof(MalinovLobbyBoard))]
public sealed class MalinovLobbyBoardTest
{
    private static readonly ProtoId<MalinovLobbyTilePrototype> Ready = "Ready";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Players = "Players";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Round = "Round";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Chat = "Chat";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Character = "Character";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Menu = "Menu";

    private static readonly Vector2i ChatSmallest = new(2, 2);
    private static readonly Vector2i ChatLargest = new(6, 8);

    // The lobby in miniature: a row of small tiles, a large chat on the left, the character and menu on its right.
    private static readonly MalinovLobbyBoardTile[] Tiles =
    [
        Fixed(Ready, 2, 1),
        Fixed(Players, 2, 1),
        Fixed(Round, 2, 1),
        new(Chat, new Vector2i(4, 4), new Vector2i(2, 2), new Vector2i(6, 8)),
        Fixed(Character, 2, 2),
        Fixed(Menu, 2, 1),
    ];

    [Test]
    public void DefaultBoardPlacesTilesInOrder()
    {
        var placements = Arrange(null);

        Assert.That(placements, Is.EquivalentTo(DefaultBoard()));
    }

    [Test]
    public void SavedPlacesAreKept()
    {
        var saved = DefaultBoard();
        saved[Menu] = Place(0, 6, 2, 1);
        saved[Chat] = Place(0, 1, 4, 2);

        var placements = Arrange(Layout(saved));

        Assert.That(placements, Is.EquivalentTo(saved));
    }

    [Test]
    public void TileMissingFromTheLayoutGoesBelowTheOthers()
    {
        // The menu tile came after the player arranged the board.
        var saved = DefaultBoard();
        saved.Remove(Menu);

        var placements = Arrange(Layout(saved));

        Assert.That(placements[Menu], Is.EqualTo(Place(0, 5, 2, 1)));
    }

    [Test]
    public void SavedPlacesFitTheCurrentLimits()
    {
        // The limits of the chat were lowered after the player made it larger; the round tile stopped being resizable.
        var saved = DefaultBoard();
        saved[Chat] = Place(0, 1, 6, 12);
        saved[Round] = Place(4, 0, 4, 1);

        var placements = Arrange(Layout(saved));

        Assert.Multiple(() =>
        {
            Assert.That(placements[Chat], Is.EqualTo(Place(0, 1, 6, 8)));
            Assert.That(placements[Round], Is.EqualTo(Place(4, 0, 2, 1)));
        });
    }

    [Test]
    public void OverlappingSavedPlacesArePushedDown()
    {
        // The chat grew and now covers the place of the character tile, which goes below it.
        var saved = DefaultBoard();
        saved[Chat] = Place(0, 1, 6, 4);

        var placements = Arrange(Layout(saved));

        Assert.Multiple(() =>
        {
            Assert.That(placements[Chat], Is.EqualTo(Place(0, 1, 6, 4)));
            Assert.That(placements[Character], Is.EqualTo(Place(4, 5, 2, 2)));
            Assert.That(placements[Menu], Is.EqualTo(Place(4, 7, 2, 1)));
            Assert.That(Overlaps(placements), Is.False);
        });
    }

    [Test]
    public void MovingOntoFreeCellsLeavesTheOldPlaceEmpty()
    {
        var board = DefaultBoard();

        var moved = MalinovLobbyBoard.Move(board, Players, new Vector2i(2, 6), out var result);

        var expected = DefaultBoard();
        expected[Players] = Place(2, 6, 2, 1);
        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(result, Is.EquivalentTo(expected));
        });
    }

    [Test]
    public void TileDroppedOnATileThatFitsTheOldPlaceSwapsWithIt()
    {
        var board = DefaultBoard();

        var moved = MalinovLobbyBoard.Move(board, Ready, new Vector2i(4, 0), out var result);

        var expected = DefaultBoard();
        expected[Ready] = Place(4, 0, 2, 1);
        expected[Round] = Place(0, 0, 2, 1);
        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(result, Is.EquivalentTo(expected));
        });
    }

    [Test]
    public void TileThatDoesNotFitTheOldPlaceIsPushedDown()
    {
        // The chat would not fit where the ready tile was, so it goes down below the ready tile.
        var board = DefaultBoard();

        var moved = MalinovLobbyBoard.Move(board, Ready, new Vector2i(0, 1), out var result);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(result[Ready], Is.EqualTo(Place(0, 1, 2, 1)));
            Assert.That(result[Chat], Is.EqualTo(Place(0, 2, 4, 4)));
            Assert.That(result[Players], Is.EqualTo(Place(2, 0, 2, 1)), "Tiles out of the way stay.");
            Assert.That(result[Character], Is.EqualTo(Place(4, 1, 2, 2)));
            Assert.That(Overlaps(result), Is.False);
        });
    }

    [Test]
    public void PushedTilesPushTheTilesBelowThem()
    {
        // The round tile lands on the character tile, which then lands on the menu tile.
        var board = DefaultBoard();

        var moved = MalinovLobbyBoard.Move(board, Round, new Vector2i(4, 1), out var result);

        Assert.Multiple(() =>
        {
            Assert.That(moved, Is.True);
            Assert.That(result[Round], Is.EqualTo(Place(4, 1, 2, 1)));
            Assert.That(result[Character], Is.EqualTo(Place(4, 2, 2, 2)));
            Assert.That(result[Menu], Is.EqualTo(Place(4, 4, 2, 1)));
            Assert.That(Overlaps(result), Is.False);
        });
    }

    [TestCase(10, 0, 4, 0)]
    [TestCase(-5, -5, 0, 0)]
    public void MovedTileStaysOnTheBoard(int column, int row, int expectedColumn, int expectedRow)
    {
        var board = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>
        {
            [Round] = Place(2, 3, 2, 1),
        };

        MalinovLobbyBoard.Move(board, Round, new Vector2i(column, row), out var result);

        Assert.That(result[Round], Is.EqualTo(Place(expectedColumn, expectedRow, 2, 1)));
    }

    [Test]
    public void MoveToTheSamePlaceChangesNothing()
    {
        var moved = MalinovLobbyBoard.Move(DefaultBoard(), Chat, new Vector2i(0, 1), out _);

        Assert.That(moved, Is.False);
    }

    [Test]
    public void MoveOfATileNotOnTheBoardChangesNothing()
    {
        var moved = MalinovLobbyBoard.Move(DefaultBoard(), "Missing", new Vector2i(0, 8), out _);

        Assert.That(moved, Is.False);
    }

    [Test]
    public void MoveThatWouldPushPastTheLastRowIsRefused()
    {
        // The wide bottom tile cannot swap into the ready tile's place, as the players tile is there too,
        // and below the ready tile there is no row left.
        var last = MalinovLobbyLayouts.MaxRows - 1;
        var board = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>
        {
            [Ready] = Place(0, 0, 2, 1),
            [Players] = Place(2, 0, 2, 1),
            [Chat] = Place(0, last, 6, 1),
        };

        var moved = MalinovLobbyBoard.Move(board, Ready, new Vector2i(0, last), out _);

        Assert.That(moved, Is.False);
    }

    [Test]
    public void GrowingATilePushesTheTilesBelowIt()
    {
        var board = DefaultBoard();

        var resized = MalinovLobbyBoard.Resize(board, Chat, new Vector2i(6, 4), new Vector2i(2, 2), new Vector2i(6, 8), out var result);

        Assert.Multiple(() =>
        {
            Assert.That(resized, Is.True);
            Assert.That(result[Chat], Is.EqualTo(Place(0, 1, 6, 4)));
            Assert.That(result[Character], Is.EqualTo(Place(4, 5, 2, 2)));
            Assert.That(result[Menu], Is.EqualTo(Place(4, 7, 2, 1)));
            Assert.That(Overlaps(result), Is.False);
        });
    }

    [Test]
    public void ShrinkingATileLeavesEmptyCells()
    {
        var board = DefaultBoard();

        var resized = MalinovLobbyBoard.Resize(board, Chat, new Vector2i(2, 2), new Vector2i(2, 2), new Vector2i(6, 8), out var result);

        var expected = DefaultBoard();
        expected[Chat] = Place(0, 1, 2, 2);
        Assert.Multiple(() =>
        {
            Assert.That(resized, Is.True);
            Assert.That(result, Is.EquivalentTo(expected));
        });
    }

    [TestCase(1, 1, 2, 2)]
    [TestCase(4, 30, 4, 8)]
    [TestCase(9, 3, 6, 3)]
    public void ResizeStaysWithinTheLimits(int width, int height, int expectedWidth, int expectedHeight)
    {
        var board = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>
        {
            [Chat] = Place(0, 0, 4, 4),
        };

        MalinovLobbyBoard.Resize(board, Chat, new Vector2i(width, height), new Vector2i(2, 2), new Vector2i(6, 8), out var result);

        Assert.That(result[Chat], Is.EqualTo(Place(0, 0, expectedWidth, expectedHeight)));
    }

    [Test]
    public void ResizeKeepsTheTopLeftCorner()
    {
        // Only two columns are left right of the chat, so it cannot get wider than that.
        var board = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>
        {
            [Chat] = Place(4, 2, 2, 2),
        };

        MalinovLobbyBoard.Resize(board, Chat, new Vector2i(5, 3), new Vector2i(2, 2), new Vector2i(6, 8), out var result);

        Assert.That(result[Chat], Is.EqualTo(Place(4, 2, 2, 3)));
    }

    // The moved edges follow the cell under the pointer; the other edges stay where they are.
    [TestCase(MalinovLobbyTileEdges.Left, 0, 5, 0, 2, 4, 2)]
    [TestCase(MalinovLobbyTileEdges.Right, 4, 0, 2, 2, 3, 2)]
    [TestCase(MalinovLobbyTileEdges.Top, 3, 0, 2, 0, 2, 4)]
    [TestCase(MalinovLobbyTileEdges.Bottom, 0, 5, 2, 2, 2, 4)]
    [TestCase(MalinovLobbyTileEdges.TopLeft, 0, 0, 0, 0, 4, 4)]
    [TestCase(MalinovLobbyTileEdges.BottomRight, 5, 4, 2, 2, 4, 3)]
    [TestCase(MalinovLobbyTileEdges.TopRight, 5, 1, 2, 1, 4, 3)]
    [TestCase(MalinovLobbyTileEdges.BottomLeft, 1, 5, 1, 2, 3, 4)]
    public void HandleMovesItsEdgesToTheCellUnderThePointer(
        MalinovLobbyTileEdges edges, int cellColumn, int cellRow,
        int column, int row, int width, int height)
    {
        var resized = MalinovLobbyBoard.GetResized(
            Place(2, 2, 2, 2), edges, new Vector2i(cellColumn, cellRow), ChatSmallest, ChatLargest);

        Assert.That(resized, Is.EqualTo(Place(column, row, width, height)));
    }

    [Test]
    public void ShrinkingEdgeStopsAtTheSmallestSize()
    {
        // The left edge is dragged past the right one; the tile keeps its smallest width at its right edge.
        var resized = MalinovLobbyBoard.GetResized(
            Place(0, 1, 4, 2), MalinovLobbyTileEdges.Left, new Vector2i(5, 1), ChatSmallest, ChatLargest);

        Assert.That(resized, Is.EqualTo(Place(2, 1, 2, 2)));
    }

    [Test]
    public void GrowingEdgeStopsAtTheLargestSize()
    {
        var resized = MalinovLobbyBoard.GetResized(
            Place(3, 0, 3, 3), MalinovLobbyTileEdges.Left, new Vector2i(0, 0), new Vector2i(2, 2), new Vector2i(3, 3));

        Assert.That(resized, Is.EqualTo(Place(3, 0, 3, 3)));
    }

    [TestCase(MalinovLobbyTileEdges.Left, -5, 1, 0, 1, 4, 2)]
    [TestCase(MalinovLobbyTileEdges.Top, 2, -3, 2, 0, 2, 3)]
    public void GrowingEdgeStopsAtTheBoard(
        MalinovLobbyTileEdges edges, int cellColumn, int cellRow,
        int column, int row, int width, int height)
    {
        var resized = MalinovLobbyBoard.GetResized(
            Place(2, 1, 2, 2), edges, new Vector2i(cellColumn, cellRow), ChatSmallest, ChatLargest);

        Assert.That(resized, Is.EqualTo(Place(column, row, width, height)));
    }

    [Test]
    public void GrowingUpPushesTheTilesAboveDown()
    {
        // The chat takes the row of ready and players; both go down below it, in their columns.
        var board = DefaultBoard();
        var area = MalinovLobbyBoard.GetResized(board[Chat], MalinovLobbyTileEdges.Top, new Vector2i(0, 0), ChatSmallest, ChatLargest);

        var resized = MalinovLobbyBoard.Resize(board, Chat, area, ChatSmallest, ChatLargest, out var result);

        Assert.Multiple(() =>
        {
            Assert.That(resized, Is.True);
            Assert.That(result[Chat], Is.EqualTo(Place(0, 0, 4, 5)));
            Assert.That(result[Ready], Is.EqualTo(Place(0, 5, 2, 1)));
            Assert.That(result[Players], Is.EqualTo(Place(2, 5, 2, 1)));
            Assert.That(result[Round], Is.EqualTo(Place(4, 0, 2, 1)), "Tiles out of the way stay.");
            Assert.That(Overlaps(result), Is.False);
        });
    }

    [Test]
    public void TilesInTheWayAreTheOnesUnderThePlacement()
    {
        var board = DefaultBoard();
        var inTheWay = new List<ProtoId<MalinovLobbyTilePrototype>>();

        // The players tile itself is under the place too, but it is the one being moved.
        MalinovLobbyBoard.GetInTheWay(board, Players, Place(2, 0, 4, 2), inTheWay);

        Assert.That(inTheWay, Is.EquivalentTo(new[] { Round, Character, Chat }));
    }

    private static Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> DefaultBoard()
    {
        return new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>
        {
            [Ready] = Place(0, 0, 2, 1),
            [Players] = Place(2, 0, 2, 1),
            [Round] = Place(4, 0, 2, 1),
            [Chat] = Place(0, 1, 4, 4),
            [Character] = Place(4, 1, 2, 2),
            [Menu] = Place(4, 3, 2, 1),
        };
    }

    private static Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> Arrange(MalinovLobbyLayout? layout)
    {
        var placements = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>();
        MalinovLobbyBoard.Arrange(Tiles, layout, placements);
        return placements;
    }

    private static MalinovLobbyLayout Layout(Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> places)
    {
        return new MalinovLobbyLayout(places, new List<ProtoId<MalinovLobbyTilePrototype>>());
    }

    private static MalinovLobbyBoardTile Fixed(ProtoId<MalinovLobbyTilePrototype> id, int width, int height)
    {
        var size = new Vector2i(width, height);
        return new MalinovLobbyBoardTile(id, size, size, size);
    }

    private static MalinovLobbyTilePlacement Place(int column, int row, int width, int height)
    {
        return new MalinovLobbyTilePlacement(column, row, width, height);
    }

    private static bool Overlaps(Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> placements)
    {
        var list = placements.Values.ToList();
        for (var i = 0; i < list.Count; i++)
        {
            for (var j = i + 1; j < list.Count; j++)
            {
                if (list[i].Overlaps(list[j]))
                    return true;
            }
        }

        return false;
    }
}
