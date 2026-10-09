#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client._MalinovStation.Lobby;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.IntegrationTests.Fixtures;
using Content.Server.Database;
using Content.Shared._MalinovStation.Lobby;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using ClientLayoutSystem = Content.Client._MalinovStation.Lobby.MalinovLobbyLayoutSystem;
using ServerLayoutSystem = Content.Server._MalinovStation.Lobby.MalinovLobbyLayoutSystem;

namespace Content.IntegrationTests.Tests._MalinovStation.Lobby;

/// <summary>
/// The player's own lobby board: editing it in the lobby, keeping it on the server and getting it back.
/// </summary>
/// <remarks>
/// Every test starts from the default board: ready, players and round on top, the chat with the character,
/// menu and server beside it, what's new and credits below.
/// </remarks>
[TestFixture]
[TestOf(typeof(MalinovLobbyTileUIController))]
[TestOf(typeof(ServerLayoutSystem))]
public sealed class MalinovLobbyLayoutTest : GameTest
{
    private static readonly ProtoId<MalinovLobbyTilePrototype> Ready = "MalinovLobbyTileReady";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Players = "MalinovLobbyTilePlayers";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Round = "MalinovLobbyTileRound";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Actions = "MalinovLobbyTileActions";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Chat = "MalinovLobbyTileChat";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Character = "MalinovLobbyTileCharacter";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Changelog = "MalinovLobbyTileChangelog";
    private static readonly ProtoId<MalinovLobbyTilePrototype> Credits = "MalinovLobbyTileCredits";

    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;

    public override PoolSettings PoolSettings => new() { InLobby = true };

    [Test]
    public async Task CustomizeButtonSwitchesEditing()
    {
        var controller = GetController();
        var loc = Client.ResolveDependency<ILocalizationManager>();

        try
        {
            await Client.WaitPost(() => Click(GetLobby().CustomizeButton));
            await Client.WaitAssertion(() =>
            {
                var lobby = GetLobby();
                Assert.Multiple(() =>
                {
                    Assert.That(controller.IsEditing, Is.True);
                    Assert.That(lobby.CustomizeButton.Text, Is.EqualTo(loc.GetString("malinov-lobby-customize-done-button")));
                    Assert.That(lobby.ResetLayoutButton.Visible, Is.True);
                    Assert.That(lobby.TileGrid.ShowCells, Is.True, "The free cells show while editing.");
                });
            });

            await Client.WaitPost(() => Click(GetLobby().CustomizeButton));
            await Client.WaitAssertion(() =>
            {
                var lobby = GetLobby();
                Assert.Multiple(() =>
                {
                    Assert.That(controller.IsEditing, Is.False);
                    Assert.That(lobby.CustomizeButton.Text, Is.EqualTo(loc.GetString("malinov-lobby-customize-button")));
                    Assert.That(lobby.ResetLayoutButton.Visible, Is.False);
                    Assert.That(lobby.TileGrid.ShowCells, Is.False);
                });
            });
        }
        finally
        {
            await Client.WaitPost(() => controller.TrySetEditing(false));
        }
    }

    [Test]
    public async Task TileMovedOntoFreeCellsLeavesItsPlaceEmpty()
    {
        var controller = GetController();
        var before = await Places();

        try
        {
            var moved = false;
            await Client.WaitPost(() => moved = controller.TrySetEditing(true) && controller.TryMoveTile(Players, new Vector2i(2, 8)));
            Assert.That(moved, Is.True);

            var after = await Places();
            var expected = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>(before)
            {
                [Players] = before[Players] with { Column = 2, Row = 8 },
            };
            Assert.That(after, Is.EquivalentTo(expected), "Nothing fills the cells the tile left.");
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task TileDroppedOnAnotherOfItsSizeSwapsWithIt()
    {
        var controller = GetController();
        var before = await Places();

        try
        {
            await Client.WaitPost(() =>
            {
                controller.TrySetEditing(true);
                controller.TryMoveTile(Ready, before[Round].Position);
            });

            var after = await Places();
            Assert.Multiple(() =>
            {
                Assert.That(after[Ready], Is.EqualTo(before[Round]));
                Assert.That(after[Round], Is.EqualTo(before[Ready]));
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task TilesInTheWayGoDown()
    {
        // The credits tile goes on top of the chat; the chat cannot take the credits' place beside the board edge,
        // so it goes down below the credits, and pushes what's new down below itself.
        var controller = GetController();
        var before = await Places();

        try
        {
            await Client.WaitPost(() =>
            {
                controller.TrySetEditing(true);
                controller.TryMoveTile(Credits, before[Chat].Position);
            });

            var after = await Places();
            Assert.Multiple(() =>
            {
                Assert.That(after[Credits].Position, Is.EqualTo(before[Chat].Position));
                Assert.That(after[Chat].Row, Is.EqualTo(after[Credits].Bottom));
                Assert.That(after[Changelog].Row, Is.EqualTo(after[Chat].Bottom));
                Assert.That(Overlap(after), Is.False);
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task ResizedTilePushesTheTilesBelowIt()
    {
        var controller = GetController();
        var before = await Places();

        try
        {
            var resized = false;
            await Client.WaitPost(() =>
            {
                resized = controller.TrySetEditing(true)
                          && controller.TryResizeTile(Chat, before[Chat].Size + new Vector2i(0, 1));
            });

            var after = await Places();
            Assert.Multiple(() =>
            {
                Assert.That(resized, Is.True);
                Assert.That(after[Chat], Is.EqualTo(before[Chat] with { Height = before[Chat].Height + 1 }));
                Assert.That(after[Changelog].Row, Is.EqualTo(after[Chat].Bottom));
                Assert.That(after[Ready], Is.EqualTo(before[Ready]), "Tiles out of the way stay.");
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task TileResizedByItsLeftEdgeKeepsItsRightEdge()
    {
        // The character tile grows to the left over the chat, which goes down below it.
        var controller = GetController();
        var before = await Places();
        var character = before[Character];

        try
        {
            var resized = false;
            await Client.WaitPost(() =>
            {
                resized = controller.TrySetEditing(true)
                          && controller.TryResizeTile(Character, character with { Column = character.Column - 2, Width = character.Width + 2 });
            });

            var after = await Places();
            Assert.Multiple(() =>
            {
                Assert.That(resized, Is.True);
                Assert.That(after[Character].Right, Is.EqualTo(character.Right));
                Assert.That(after[Character].Column, Is.EqualTo(character.Column - 2));
                Assert.That(after[Chat].Row, Is.EqualTo(after[Character].Bottom));
                Assert.That(Overlap(after), Is.False);
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task ResizedTileStaysWithinItsLimits()
    {
        var controller = GetController();
        var chat = CProtoMan.Index(Chat);

        try
        {
            var largest = Vector2i.Zero;
            var smallest = Vector2i.Zero;
            await Client.WaitPost(() =>
            {
                controller.TrySetEditing(true);
                controller.TryResizeTile(Chat, new Vector2i(20, 20));
                largest = Tile(Chat).TileSize;
                controller.TryResizeTile(Chat, Vector2i.One);
                smallest = Tile(Chat).TileSize;
            });

            Assert.Multiple(() =>
            {
                Assert.That(largest, Is.EqualTo(chat.LargestSize));
                Assert.That(smallest, Is.EqualTo(chat.SmallestSize));
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task HiddenTileKeepsItsPlaceEmpty()
    {
        var controller = GetController();
        var before = await Places();

        try
        {
            await Client.WaitPost(() =>
            {
                controller.TrySetEditing(true);
                controller.TrySetTileHidden(Players, true);
            });
            await Client.WaitAssertion(() =>
            {
                Assert.That(Tile(Players).Visible, Is.True, "Hidden tiles stay in sight while editing.");
                Assert.That(Tile(Players).UserHidden, Is.True);
            });

            await Client.WaitPost(() => controller.TrySetEditing(false));
            await Pair.RunTicksSync(2);

            await Client.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(Tile(Players).Visible, Is.False);
                    Assert.That(Tile(Round).TilePosition, Is.EqualTo(before[Round].Position), "The other tiles do not close the gap.");
                });
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task ReadyAndMenuCannotBeHidden()
    {
        var controller = GetController();

        try
        {
            var hidReady = true;
            var hidMenu = true;
            await Client.WaitPost(() =>
            {
                controller.TrySetEditing(true);
                hidReady = controller.TrySetTileHidden(Ready, true);
                hidMenu = controller.TrySetTileHidden(Actions, true);
            });

            Assert.Multiple(() =>
            {
                Assert.That(hidReady, Is.False);
                Assert.That(hidMenu, Is.False);
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task LayoutIsSavedForTheAccount()
    {
        var controller = GetController();

        try
        {
            await Client.WaitPost(() =>
            {
                controller.TrySetEditing(true);
                controller.TryMoveTile(Players, new Vector2i(2, 8));
                controller.TryResizeTile(Chat, new Vector2i(4, 3));
                controller.TrySetTileHidden(Credits, true);
                controller.TrySetEditing(false);
            });
            await Pair.RunTicksSync(SaveTicks());

            var places = await Places();
            var layout = await GetSavedLayout();
            Assert.That(layout, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(layout!.Places, Is.EquivalentTo(places), "The whole board is kept.");
                Assert.That(layout.Places[Players].Position, Is.EqualTo(new Vector2i(2, 8)));
                Assert.That(layout.Places[Chat].Size, Is.EqualTo(new Vector2i(4, 3)));
                Assert.That(layout.Hidden, Is.EqualTo(new[] { Credits }));
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task LayoutComesBackAfterReconnecting()
    {
        var controller = GetController();

        try
        {
            await Client.WaitPost(() =>
            {
                controller.TrySetEditing(true);
                controller.TryMoveTile(Players, new Vector2i(2, 8));
                controller.TryResizeTile(Chat, new Vector2i(4, 3));
                controller.TrySetTileHidden(Credits, true);
                controller.TrySetEditing(false);
            });
            await Pair.RunTicksSync(SaveTicks());

            await Reconnect();

            await Client.WaitAssertion(() =>
            {
                var layout = CEntMan.System<ClientLayoutSystem>().Layout;
                Assert.That(layout, Is.Not.Null, "The server sends the saved layout once the player's data is loaded.");
                Assert.Multiple(() =>
                {
                    Assert.That(Tile(Players).TilePosition, Is.EqualTo(new Vector2i(2, 8)));
                    Assert.That(Tile(Chat).TileSize, Is.EqualTo(new Vector2i(4, 3)));
                    Assert.That(Tile(Credits).Visible, Is.False);
                });
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task ResetBringsBackTheDefaultLayout()
    {
        var controller = GetController();
        var before = await Places();

        try
        {
            await Client.WaitPost(() =>
            {
                controller.TrySetEditing(true);
                controller.TryMoveTile(Credits, new Vector2i(0, 9));
                controller.TryResizeTile(Chat, new Vector2i(2, 2));
                controller.TrySetTileHidden(Players, true);
            });
            await Pair.RunTicksSync(SaveTicks());

            var reset = false;
            await Client.WaitPost(() => reset = controller.TryResetLayout());
            await Pair.RunTicksSync(SaveTicks());

            Assert.That(reset, Is.True);
            var after = await Places();
            await Client.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(after, Is.EquivalentTo(before));
                    Assert.That(GetLobby().TileGrid.Children.OfType<MalinovLobbyTileControl>().Any(tile => tile.UserHidden), Is.False);
                });
            });
            Assert.That(await GetSavedLayout(), Is.Null, "The default layout leaves nothing in the database.");
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task ServerKeepsOnlyWhatItCanCheck()
    {
        try
        {
            // A modified client could send anything: the server keeps known tiles within their limits, drops the ones
            // on top of others and never hides the locked ones.
            await Client.WaitPost(() => CEntMan.System<ClientLayoutSystem>().SetLayout(new MalinovLobbyLayout(
                new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>
                {
                    [Chat] = new(0, 0, 9, 20),
                    ["MalinovLobbyTileNoSuchTile"] = new(0, 10, 2, 1),
                    [Round] = new(1, 2, 2, 1),
                },
                [Ready, Credits])));
            await Pair.RunTicksSync(SaveTicks());

            var chat = CProtoMan.Index(Chat);
            var layout = await GetSavedLayout();
            Assert.That(layout, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(layout!.Places, Is.EquivalentTo(new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>
                {
                    [Chat] = new(Vector2i.Zero, chat.LargestSize),
                }));
                Assert.That(layout.Hidden, Is.EqualTo(new[] { Credits }));
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task NewestOfChangesSentTogetherWins()
    {
        var layouts = CEntMan.System<ClientLayoutSystem>();
        var serverLayouts = SEntMan.System<ServerLayoutSystem>();

        try
        {
            // Messages sent within one client tick reach the server in no particular order.
            await Client.WaitPost(() =>
            {
                layouts.SetLayout(ChatAt(0));
                layouts.SetLayout(ChatAt(1));
                layouts.SetLayout(ChatAt(2));
            });
            await Pair.RunTicksSync(5);

            await Server.WaitAssertion(() =>
            {
                Assert.That(serverLayouts.GetLayout(Client.User!.Value)?.Places[Chat].Row, Is.EqualTo(2));
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    [Test]
    public async Task SavesAreSpacedOut()
    {
        var layouts = CEntMan.System<ClientLayoutSystem>();
        // A test before this one on the same pair may have saved moments ago; its interval has to pass first.
        await Pair.RunTicksSync(SaveTicks());

        try
        {
            await Client.WaitPost(() => layouts.SetLayout(ChatAt(0)));
            await Pair.RunTicksSync(5);
            var first = await GetSavedLayout();

            await Client.WaitPost(() =>
            {
                layouts.SetLayout(ChatAt(1));
                layouts.SetLayout(ChatAt(2));
            });
            await Pair.RunTicksSync(5);
            var meanwhile = await GetSavedLayout();

            await Pair.RunTicksSync(SaveTicks());
            var last = await GetSavedLayout();

            Assert.Multiple(() =>
            {
                Assert.That(first?.Places[Chat].Row, Is.EqualTo(0), "The first change is saved right away.");
                Assert.That(meanwhile?.Places[Chat].Row, Is.EqualTo(0), "Changes right after a save wait for the interval.");
                Assert.That(last?.Places[Chat].Row, Is.EqualTo(2), "Only the newest of the waiting changes is saved.");
            });
        }
        finally
        {
            await ResetLayout();
        }
    }

    /// <summary>
    /// A layout with only the chat placed, at <paramref name="row"/>.
    /// </summary>
    private static MalinovLobbyLayout ChatAt(int row)
    {
        return new MalinovLobbyLayout(
            new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> { [Chat] = new(0, row, 4, 4) },
            new List<ProtoId<MalinovLobbyTilePrototype>>());
    }

    /// <summary>
    /// Leaves the default layout behind, on the client and in the database, for the next test on this pair.
    /// </summary>
    private async Task ResetLayout()
    {
        var controller = GetController();
        var reset = false;
        await Client.WaitPost(() =>
        {
            reset = controller.TrySetEditing(true) && controller.TryResetLayout();
            controller.TrySetEditing(false);
        });
        await Pair.RunTicksSync(SaveTicks());

        var left = await GetSavedLayout();
        Assert.Multiple(() =>
        {
            Assert.That(reset, Is.True, "The layout could not be reset.");
            Assert.That(left, Is.Null, left == null
                ? string.Empty
                : $"A layout was left in the database for the next test: {left.Places.Count} places; hidden {string.Join(", ", left.Hidden)}.");
        });
    }

    private async Task Reconnect()
    {
        var net = Client.ResolveDependency<IClientNetManager>();
        var name = ServerSession!.Name;

        await Client.WaitPost(() => net.ClientDisconnect("Reconnecting for a test"));
        await Pair.RunTicksSync(20);

        Client.SetConnectTarget(Server);
        await Client.WaitPost(() => net.ClientConnect(null!, 0, name));
        await Pair.RunTicksSync(20);
    }

    private async Task<MalinovLobbyLayout?> GetSavedLayout()
    {
        var db = Server.ResolveDependency<IServerDbManager>();
        return await db.GetMalinovLobbyLayoutAsync(Client.User!.Value);
    }

    /// <summary>
    /// Ticks for one save interval of the server and a little more for the messages to arrive.
    /// </summary>
    private int SaveTicks()
    {
        return (int) Math.Ceiling(ServerLayoutSystem.SaveInterval.TotalSeconds * SGameTiming.TickRate) + 5;
    }

    /// <summary>
    /// Where the tiles of the lobby stand now.
    /// </summary>
    private async Task<Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>> Places()
    {
        var places = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>();
        await Client.WaitPost(() =>
        {
            foreach (var tile in GetLobby().TileGrid.Children.OfType<MalinovLobbyTileControl>())
            {
                places[tile.TileId!.Value] = new MalinovLobbyTilePlacement(tile.TilePosition, tile.TileSize);
            }
        });

        return places;
    }

    private static bool Overlap(Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> places)
    {
        var list = places.Values.ToList();
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

    private MalinovLobbyTileControl Tile(ProtoId<MalinovLobbyTilePrototype> id)
    {
        return GetLobby().TileGrid.Children
            .OfType<MalinovLobbyTileControl>()
            .Single(tile => tile.TileId == id);
    }

    private MalinovLobbyTileUIController GetController()
    {
        return Client.ResolveDependency<IUserInterfaceManager>().GetUIController<MalinovLobbyTileUIController>();
    }

    private LobbyGui GetLobby()
    {
        var state = (LobbyState) Client.ResolveDependency<IStateManager>().CurrentState;
        return state.Lobby ?? throw new InvalidOperationException("Lobby screen is not loaded.");
    }

    /// <summary>
    /// Clicks a button through its own input handlers, so its disabled checks and press semantics stay real.
    /// </summary>
    private static void Click(BaseButton button)
    {
        var relative = button.Size / 2;
        var pointer = new ScreenCoordinates(button.GlobalPixelPosition + relative * button.UIScale, WindowId.Main);
        foreach (var (method, state) in new[] { ("KeyBindDown", BoundKeyState.Down), ("KeyBindUp", BoundKeyState.Up) })
        {
            typeof(BaseButton).GetMethod(method, InstanceMembers)!.Invoke(button, new object[]
            {
                new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, pointer, true, relative, relative * button.UIScale),
            });
        }
    }
}
