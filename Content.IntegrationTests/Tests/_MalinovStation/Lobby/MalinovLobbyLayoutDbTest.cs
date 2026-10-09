#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Database;
using Content.Shared._MalinovStation.Lobby;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests._MalinovStation.Lobby;

/// <summary>
/// Storage of lobby layouts in a fresh in-memory database, which also applies every migration.
/// </summary>
[TestFixture]
[TestOf(typeof(ServerDbBase))]
public sealed class MalinovLobbyLayoutDbTest : GameTest
{
    private const string Chat = "MalinovLobbyTileChat";
    private const string Ready = "MalinovLobbyTileReady";
    private const string Round = "MalinovLobbyTileRound";
    private const string Credits = "MalinovLobbyTileCredits";

    [Test]
    public async Task NoLayoutUntilOneIsSaved()
    {
        var db = GetDb(Server);

        Assert.That(await db.GetMalinovLobbyLayoutAsync(NewUserId(), default), Is.Null);
    }

    [Test]
    public async Task SavedLayoutComesBack()
    {
        var db = GetDb(Server);
        var user = NewUserId();
        var places = Places((Chat, Place(0, 1, 4, 4)), (Ready, Place(4, 0, 2, 1)));

        await db.SetMalinovLobbyLayoutAsync(user, Layout(places, [Credits]));
        var layout = await db.GetMalinovLobbyLayoutAsync(user, default);

        Assert.That(layout, Is.Not.Null);
        Assert.Multiple(() =>
        {
            Assert.That(layout!.Places, Is.EquivalentTo(places));
            Assert.That(Ids(layout.Hidden), Is.EqualTo(new[] { Credits }));
        });
    }

    [Test]
    public async Task SavingAgainMovesAddsAndRemovesPlaces()
    {
        var db = GetDb(Server);
        var user = NewUserId();

        await db.SetMalinovLobbyLayoutAsync(user, Layout(Places((Chat, Place(0, 1, 4, 4)), (Ready, Place(0, 0, 2, 1))), [Credits]));
        var newPlaces = Places((Chat, Place(0, 2, 6, 3)), (Round, Place(2, 0, 2, 1)));
        await db.SetMalinovLobbyLayoutAsync(user, Layout(newPlaces, []));
        var layout = await db.GetMalinovLobbyLayoutAsync(user, default);

        Assert.Multiple(() =>
        {
            Assert.That(layout!.Places, Is.EquivalentTo(newPlaces));
            Assert.That(layout.Hidden, Is.Empty);
        });
    }

    [Test]
    public async Task DefaultLayoutLeavesNothingBehind()
    {
        var db = GetDb(Server);
        var user = NewUserId();

        await db.SetMalinovLobbyLayoutAsync(user, Layout(Places((Chat, Place(0, 1, 4, 4))), [Credits]));
        await db.SetMalinovLobbyLayoutAsync(user, new MalinovLobbyLayout());

        Assert.That(await db.GetMalinovLobbyLayoutAsync(user, default), Is.Null);
        // A layout saved later starts from scratch: no place of the old one is left over.
        await db.SetMalinovLobbyLayoutAsync(user, Layout(Places((Round, Place(0, 0, 2, 1))), []));
        Assert.That((await db.GetMalinovLobbyLayoutAsync(user, default))!.Places.Keys, Is.EquivalentTo(new ProtoId<MalinovLobbyTilePrototype>[] { Round }));
    }

    [Test]
    public async Task LayoutsOfDifferentPlayersAreKeptApart()
    {
        var db = GetDb(Server);
        var first = NewUserId();
        var second = NewUserId();

        await db.SetMalinovLobbyLayoutAsync(first, Layout(Places((Chat, Place(0, 0, 4, 4))), []));
        await db.SetMalinovLobbyLayoutAsync(second, Layout(Places((Chat, Place(2, 3, 2, 2))), []));

        var firstLayout = await db.GetMalinovLobbyLayoutAsync(first, default);
        var secondLayout = await db.GetMalinovLobbyLayoutAsync(second, default);
        Assert.Multiple(() =>
        {
            Assert.That(firstLayout!.Places[Chat], Is.EqualTo(Place(0, 0, 4, 4)));
            Assert.That(secondLayout!.Places[Chat], Is.EqualTo(Place(2, 3, 2, 2)));
        });
    }

    private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server)
    {
        var cfg = server.ResolveDependency<IConfigurationManager>();
        var serialization = server.ResolveDependency<ISerializationManager>();
        var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
        var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
        var conn = new SqliteConnection("Data Source=:memory:");
        conn.Open();
        builder.UseSqlite(conn);
        return new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog, serialization);
    }

    private static Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> Places(
        params (string Id, MalinovLobbyTilePlacement Place)[] places)
    {
        return places.ToDictionary(entry => new ProtoId<MalinovLobbyTilePrototype>(entry.Id), entry => entry.Place);
    }

    private static MalinovLobbyLayout Layout(
        Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> places,
        List<string> hidden)
    {
        return new MalinovLobbyLayout(places, hidden.Select(id => new ProtoId<MalinovLobbyTilePrototype>(id)).ToList());
    }

    private static MalinovLobbyTilePlacement Place(int column, int row, int width, int height)
    {
        return new MalinovLobbyTilePlacement(column, row, width, height);
    }

    private static List<string> Ids(IEnumerable<ProtoId<MalinovLobbyTilePrototype>> ids)
    {
        return ids.Select(id => id.Id).ToList();
    }

    private static NetUserId NewUserId()
    {
        return new NetUserId(Guid.NewGuid());
    }
}
