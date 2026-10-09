#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Shared._MalinovStation.Lobby;
using NUnit.Framework;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;

namespace Content.Tests._MalinovStation.Lobby;

[TestFixture]
[TestOf(typeof(MalinovLobbyLayouts))]
public sealed class MalinovLobbyLayoutsTest : ContentUnitTest
{
    private const string Ready = "MalinovTestTileReady";
    private const string Chat = "MalinovTestTileChat";
    private const string Round = "MalinovTestTileRound";
    private const string Credits = "MalinovTestTileCredits";
    private const string Missing = "MalinovTestTileMissing";

    // The ready tile cannot be hidden: without it the player could not join a round.
    // Only the chat can be resized.
    private const string Prototypes = $@"
- type: malinovLobbyTile
  id: {Ready}
  widget: Ready
  size: 2, 1
  hideable: false

- type: malinovLobbyTile
  id: {Chat}
  widget: Chat
  size: 4, 4
  minSize: 2, 2
  maxSize: 6, 8

- type: malinovLobbyTile
  id: {Round}
  widget: Round
  size: 2, 1

- type: malinovLobbyTile
  id: {Credits}
  widget: Credits
  size: 2, 1
";

    private IPrototypeManager _prototypes = default!;

    [OneTimeSetUp]
    public void LoadPrototypes()
    {
        IoCManager.Resolve<ISerializationManager>().Initialize();
        _prototypes = IoCManager.Resolve<IPrototypeManager>();
        _prototypes.Initialize();
        _prototypes.LoadString(Prototypes);
        _prototypes.ResolveResults();
    }

    [Test]
    public void TileWithoutLimitsKeepsItsSize()
    {
        var round = _prototypes.Index<MalinovLobbyTilePrototype>(Round);

        Assert.Multiple(() =>
        {
            Assert.That(round.SmallestSize, Is.EqualTo(new Vector2i(2, 1)));
            Assert.That(round.LargestSize, Is.EqualTo(new Vector2i(2, 1)));
            Assert.That(round.Resizable, Is.False);
        });
    }

    [Test]
    public void LimitsMakeATileResizable()
    {
        var chat = _prototypes.Index<MalinovLobbyTilePrototype>(Chat);

        Assert.Multiple(() =>
        {
            Assert.That(chat.SmallestSize, Is.EqualTo(new Vector2i(2, 2)));
            Assert.That(chat.LargestSize, Is.EqualTo(new Vector2i(6, 8)));
            Assert.That(chat.Resizable, Is.True);
        });
    }

    [Test]
    public void SanitizeKeepsKnownPlacesAndHiddenTiles()
    {
        var layout = MalinovLobbyLayouts.Sanitize(
            Layout([(Round, Place(0, 0, 2, 1)), (Chat, Place(0, 2, 4, 4))], [Credits]),
            _prototypes);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Places, Is.EquivalentTo(new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>
            {
                [Round] = Place(0, 0, 2, 1),
                [Chat] = Place(0, 2, 4, 4),
            }));
            Assert.That(Ids(layout.Hidden), Is.EqualTo(new[] { Credits }));
        });
    }

    [Test]
    public void SanitizeDropsUnknownRepeatedAndOverlongTiles()
    {
        var overlong = new string('A', MalinovLobbyLayouts.MaxIdLength + 1);

        var layout = MalinovLobbyLayouts.Sanitize(
            Layout(
                [(Chat, Place(0, 0, 4, 4)), (Missing, Place(4, 0, 2, 1)), (overlong, Place(4, 1, 2, 1))],
                [Round, Round, Missing]),
            _prototypes);

        Assert.Multiple(() =>
        {
            Assert.That(Ids(layout.Places.Keys), Is.EqualTo(new[] { Chat }));
            Assert.That(Ids(layout.Hidden), Is.EqualTo(new[] { Round }));
        });
    }

    [Test]
    public void SanitizeLooksNoFurtherThanTheLimit()
    {
        // A client cannot make the server look through or store more entries than the limit.
        var tooMany = Enumerable.Range(0, MalinovLobbyLayouts.MaxTiles)
            .Select(i => $"{Missing}{i}")
            .Append(Chat)
            .ToList();

        var layout = MalinovLobbyLayouts.Sanitize(
            Layout(tooMany.Select(id => (id, Place(0, 0, 2, 2))).ToList(), tooMany),
            _prototypes);

        Assert.Multiple(() =>
        {
            Assert.That(layout.Places, Is.Empty);
            Assert.That(layout.Hidden, Is.Empty);
        });
    }

    [TestCase(1, 1, 2, 2)]
    [TestCase(9, 20, 6, 8)]
    [TestCase(3, 5, 3, 5)]
    public void SanitizeFitsSizesIntoTheTileLimits(int width, int height, int expectedWidth, int expectedHeight)
    {
        var layout = MalinovLobbyLayouts.Sanitize(Layout([(Chat, Place(0, 0, width, height))], []), _prototypes);

        Assert.That(layout.Places[Chat], Is.EqualTo(Place(0, 0, expectedWidth, expectedHeight)));
    }

    [Test]
    public void TilesOfAFixedSizeKeepIt()
    {
        var layout = MalinovLobbyLayouts.Sanitize(Layout([(Round, Place(0, 0, 4, 3))], []), _prototypes);

        Assert.That(layout.Places[Round], Is.EqualTo(Place(0, 0, 2, 1)));
    }

    [TestCase(5, 0, 2, 0)]
    [TestCase(-3, -2, 0, 0)]
    [TestCase(0, 1000, 0, MalinovLobbyLayouts.MaxRows - 4)]
    public void SanitizeKeepsTilesOnTheBoard(int column, int row, int expectedColumn, int expectedRow)
    {
        var layout = MalinovLobbyLayouts.Sanitize(Layout([(Chat, Place(column, row, 4, 4))], []), _prototypes);

        Assert.That(layout.Places[Chat], Is.EqualTo(Place(expectedColumn, expectedRow, 4, 4)));
    }

    [Test]
    public void SanitizeDropsTilesThatOverlapEarlierOnes()
    {
        // In reading order the round tile comes first, so the credits tile on top of it goes.
        var layout = MalinovLobbyLayouts.Sanitize(
            Layout([(Credits, Place(1, 0, 2, 1)), (Round, Place(0, 0, 2, 1)), (Ready, Place(4, 0, 2, 1))], []),
            _prototypes);

        Assert.That(Ids(layout.Places.Keys).OrderBy(id => id), Is.EqualTo(new[] { Ready, Round }.OrderBy(id => id)));
    }

    [Test]
    public void LockedTilesCannotBeHidden()
    {
        var layout = MalinovLobbyLayouts.Sanitize(Layout([], [Ready, Chat]), _prototypes);

        Assert.That(Ids(layout.Hidden), Is.EqualTo(new[] { Chat }));
    }

    [Test]
    public void LayoutMatchingTheDefaultsIsKeptEmpty()
    {
        // An empty layout follows later changes of the default one.
        var defaults = DefaultPlaces();

        var layout = MalinovLobbyLayouts.Create(new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>(defaults), [], defaults);

        Assert.That(layout.IsDefault, Is.True);
    }

    [Test]
    public void MovedTileKeepsTheWholeBoard()
    {
        var defaults = DefaultPlaces();
        var places = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>(defaults)
        {
            [Credits] = Place(2, 6, 2, 1),
        };

        var layout = MalinovLobbyLayouts.Create(places, [], defaults);

        Assert.Multiple(() =>
        {
            Assert.That(layout.IsDefault, Is.False);
            Assert.That(layout.Places, Is.EquivalentTo(places));
            Assert.That(layout.Hidden, Is.Empty);
        });
    }

    [Test]
    public void HiddenTileMakesTheLayoutThePlayersOwn()
    {
        var defaults = DefaultPlaces();

        var layout = MalinovLobbyLayouts.Create(
            new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>(defaults),
            [Credits],
            defaults);

        Assert.Multiple(() =>
        {
            Assert.That(layout.IsDefault, Is.False);
            Assert.That(layout.Places, Is.EquivalentTo(defaults));
            Assert.That(Ids(layout.Hidden), Is.EqualTo(new[] { Credits }));
        });
    }

    [TestCase(0, 0, 2, 2, 1, 1, 2, 2, true)]
    [TestCase(0, 0, 2, 2, 2, 0, 2, 2, false)]
    [TestCase(0, 0, 2, 2, 0, 2, 2, 2, false)]
    [TestCase(0, 0, 6, 1, 4, 0, 2, 3, true)]
    public void PlacementsOverlapOnlyWhenTheyShareACell(
        int column, int row, int width, int height,
        int otherColumn, int otherRow, int otherWidth, int otherHeight,
        bool expected)
    {
        var first = Place(column, row, width, height);
        var second = Place(otherColumn, otherRow, otherWidth, otherHeight);

        Assert.Multiple(() =>
        {
            Assert.That(first.Overlaps(second), Is.EqualTo(expected));
            Assert.That(second.Overlaps(first), Is.EqualTo(expected));
        });
    }

    private static Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> DefaultPlaces()
    {
        return new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>
        {
            [Ready] = Place(0, 0, 2, 1),
            [Round] = Place(2, 0, 2, 1),
            [Chat] = Place(0, 1, 4, 4),
            [Credits] = Place(4, 1, 2, 1),
        };
    }

    private static MalinovLobbyTilePlacement Place(int column, int row, int width, int height)
    {
        return new MalinovLobbyTilePlacement(column, row, width, height);
    }

    private static MalinovLobbyLayout Layout(List<(string Id, MalinovLobbyTilePlacement Place)> places, List<string> hidden)
    {
        var placesById = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>();
        foreach (var (id, place) in places)
        {
            placesById[id] = place;
        }

        return new MalinovLobbyLayout(placesById, hidden.Select(id => new ProtoId<MalinovLobbyTilePrototype>(id)).ToList());
    }

    private static List<string> Ids(IEnumerable<ProtoId<MalinovLobbyTilePrototype>> ids)
    {
        return ids.Select(id => id.Id).ToList();
    }
}
