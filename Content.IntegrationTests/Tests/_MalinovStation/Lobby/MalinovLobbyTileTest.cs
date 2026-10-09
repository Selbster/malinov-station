#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._MalinovStation.Lobby;
using Content.Client._MalinovStation.Lobby.Tiles;
using Content.Client.GameTicking.Managers;
using Content.Client.Gameplay;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.GameTicking;
using Content.Shared._MalinovStation.Lobby;
using Content.Shared.CCVar;
using Robust.Client.State;
using Robust.Client.UserInterface;
using Robust.Shared.Localization;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._MalinovStation.Lobby;

[TestFixture]
[TestOf(typeof(MalinovLobbyTileUIController))]
public sealed class MalinovLobbyTileTest : GameTest
{
    public override PoolSettings PoolSettings => new() { InLobby = true };

    [Test]
    public async Task EveryTilePrototypeHasAWidget()
    {
        await Client.WaitAssertion(() =>
        {
            var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<MalinovLobbyTileUIController>();
            var prototypes = CProtoMan.EnumeratePrototypes<MalinovLobbyTilePrototype>().ToList();

            Assert.That(prototypes, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                foreach (var proto in prototypes)
                {
                    Assert.That(controller.HasWidget(proto.Widget), Is.True, $"Tile {proto.ID} uses unknown widget {proto.Widget}.");
                    Assert.That(proto.Size.X, Is.Positive, $"Tile {proto.ID} must be at least one cell wide.");
                    Assert.That(proto.Size.Y, Is.Positive, $"Tile {proto.ID} must be at least one cell tall.");
                }
            });
        });
    }

    [Test]
    public async Task EveryTileSizeFitsItsLimitsAndTheBoard()
    {
        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                foreach (var proto in CProtoMan.EnumeratePrototypes<MalinovLobbyTilePrototype>())
                {
                    var (smallest, size, largest) = (proto.SmallestSize, proto.Size, proto.LargestSize);
                    Assert.That(smallest.X >= 1 && smallest.Y >= 1, Is.True, $"Tile {proto.ID} must keep at least one cell.");
                    Assert.That(smallest.X <= size.X && smallest.Y <= size.Y, Is.True, $"Tile {proto.ID} is smaller than its smallest size.");
                    Assert.That(size.X <= largest.X && size.Y <= largest.Y, Is.True, $"Tile {proto.ID} is larger than its largest size.");
                    Assert.That(largest.X, Is.LessThanOrEqualTo(MalinovLobbyLayouts.BoardColumns), $"Tile {proto.ID} may grow wider than the board.");
                }
            });
        });
    }

    [Test]
    public async Task WhatsNewShowsTheLatestChanges()
    {
        // The changelog is read in the background when the tile is built.
        MalinovChangelogTileWidget? widget = null;
        for (var i = 0; i < 50; i++)
        {
            await Pair.RunTicksSync(1);
            await Client.WaitPost(() => widget = Widget<MalinovChangelogTileWidget>(GetLobby()));
            if (widget!.Latest.Count > 0)
                break;
        }

        await Client.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(widget!.Latest, Has.Count.InRange(1, MalinovChangelogTileWidget.MaxEntries));
                Assert.That(widget.Latest.Select(entry => entry.Id), Is.Ordered.Descending, "The newest changes come first.");
                Assert.That(widget.Entries.ChildCount, Is.Positive);
            });
        });
    }

    [Test]
    public async Task LobbyIsBuiltFromTilesInPrototypeOrder()
    {
        await Client.WaitAssertion(() => AssertLobbyBuilt(GetLobby()));
    }

    [Test]
    public async Task ReenteringTheLobbyRebuildsTilesWithoutDuplicates()
    {
        LobbyGui? firstLobby = null;
        await Client.WaitAssertion(() => firstLobby = GetLobby());

        await ReenterLobby();
        await Pair.RunTicksSync(5);

        await Client.WaitAssertion(() =>
        {
            var lobby = GetLobby();
            Assert.That(lobby, Is.SameAs(firstLobby), "Screens are cached, so the same LobbyGui is reused.");
            AssertLobbyBuilt(lobby);
        });
    }

    [Test]
    public async Task CharacterSetupHidesTheTileGrid()
    {
        try
        {
            await Client.WaitPost(() => GetLobbyState().SwitchState(LobbyGui.LobbyGuiState.CharacterSetup));
            await Pair.RunTicksSync(1);

            await Client.WaitAssertion(() =>
            {
                var lobby = GetLobby();
                Assert.Multiple(() =>
                {
                    Assert.That(lobby.TileHost.Visible, Is.False);
                    Assert.That(lobby.CharacterSetupState.Visible, Is.True);
                });
            });
        }
        finally
        {
            await Client.WaitPost(() => GetLobbyState().SwitchState(LobbyGui.LobbyGuiState.Default));
            await Pair.RunTicksSync(1);
        }

        await Client.WaitAssertion(() =>
        {
            var lobby = GetLobby();
            Assert.Multiple(() =>
            {
                Assert.That(lobby.TileHost.Visible, Is.True);
                Assert.That(lobby.CharacterSetupState.Visible, Is.False);
            });
        });
    }

    [Test]
    public async Task TilesShowTheLobbyData()
    {
        await Client.WaitAssertion(() =>
        {
            var lobby = GetLobby();
            var ready = Widget<MalinovReadyTileWidget>(lobby);
            var players = Widget<MalinovPlayersTileWidget>(lobby);
            var round = Widget<MalinovRoundTileWidget>(lobby);
            var server = Widget<MalinovServerTileWidget>(lobby);
            var credits = Widget<MalinovCreditsTileWidget>(lobby);

            // Before the round the countdown always says something: time left, paused or soon.
            Assert.Multiple(() =>
            {
                Assert.That(ready.CountdownLabel.Visible, Is.True);
                Assert.That(ready.CountdownLabel.Text, Is.Not.Empty);
                Assert.That(ready.ReadyButton.ToggleMode, Is.True);
                Assert.That(players.OnlineValue.Text, Is.EqualTo("1"), "The test pair has one player.");
                Assert.That(players.CountValue.Text, Is.EqualTo("0"), "Nobody is ready yet.");
                Assert.That(round.NumberLabel.Visible, Is.True);
                Assert.That(round.MapLabel.GetMessage(), Is.Not.Empty);
                Assert.That(round.RoundTimeLabel.Text, Is.Not.Empty);
                Assert.That(server.ServerName.Text, Is.Not.Empty);
                Assert.That(credits.SongLabel.GetMessage(), Is.Not.Empty);
                Assert.That(credits.BackgroundLabel.GetMessage(), Is.Not.Empty);
            });
        });
    }

    [Test]
    public async Task ReadyGoesThroughTheServer()
    {
        var controller = GetController();
        var ticker = CEntMan.System<ClientGameTicker>();

        try
        {
            var requested = false;
            await Client.WaitPost(() => requested = controller.TrySetReady(true));
            Assert.That(requested, Is.True);
            // Client -> server -> client round trip.
            await Pair.RunTicksSync(10);

            await Client.WaitAssertion(() =>
            {
                Assert.That(ticker.AreWeReady, Is.True);
                Assert.That(Widget<MalinovReadyTileWidget>(GetLobby()).ReadyButton.Pressed, Is.True);
                Assert.That(Widget<MalinovPlayersTileWidget>(GetLobby()).CountValue.Text, Is.EqualTo("1"),
                    "The ready count must follow the player's readiness.");
            });
        }
        finally
        {
            await Client.WaitPost(() => controller.TrySetReady(false));
            await Pair.RunTicksSync(10);
        }

        await Client.WaitAssertion(() =>
        {
            Assert.That(ticker.AreWeReady, Is.False);
            Assert.That(Widget<MalinovReadyTileWidget>(GetLobby()).ReadyButton.Pressed, Is.False);
        });
    }

    [Test]
    public async Task LobbyInfoComesFromTheServer()
    {
        var serverTicker = SEntMan.System<GameTicker>();
        var roundId = 0;
        string? modeTitle = null;
        await Server.WaitPost(() =>
        {
            roundId = serverTicker.RoundId;
            // Secret modes are announced by their decoy, like the lobby info text does.
            var preset = serverTicker.CurrentPreset ?? serverTicker.Preset;
            modeTitle = preset == null ? null : (serverTicker.Decoy ?? preset).ModeTitle;
        });

        await Client.WaitAssertion(() =>
        {
            var info = CEntMan.System<MalinovLobbyInfoSystem>().Info;
            Assert.That(info, Is.Not.Null, "The server sends lobby info when the player enters the lobby.");
            Assert.Multiple(() =>
            {
                Assert.That(info!.RoundId, Is.EqualTo(roundId));
                Assert.That(info.PlayerCount, Is.EqualTo(1));
                Assert.That(info.ReadyCount, Is.Zero);
                Assert.That(info.InGameCount, Is.Zero);
                Assert.That(info.ModeTitle, Is.EqualTo(modeTitle));
            });
        });
    }

    [Test]
    public async Task ReadyAllUpdatesTheReadyCount()
    {
        var serverTicker = SEntMan.System<GameTicker>();
        var lobbyInfo = CEntMan.System<MalinovLobbyInfoSystem>();

        try
        {
            await Server.WaitPost(() => serverTicker.ToggleReadyAll(true));
            await Pair.RunTicksSync(10);

            await Client.WaitAssertion(() => Assert.That(lobbyInfo.Info?.ReadyCount, Is.EqualTo(1)));
        }
        finally
        {
            await Server.WaitPost(() => serverTicker.ToggleReadyAll(false));
            await Pair.RunTicksSync(10);
        }

        await Client.WaitAssertion(() => Assert.That(lobbyInfo.Info?.ReadyCount, Is.Zero));
    }

    [Test]
    public async Task JoiningIsRefusedBeforeTheRound()
    {
        var joined = true;
        await Client.WaitPost(() => joined = GetController().TryJoinGame());
        Assert.That(joined, Is.False);
    }

    [Test]
    public async Task OpeningCharacterSetupLeavesTheReadyState()
    {
        var controller = GetController();
        var ticker = CEntMan.System<ClientGameTicker>();

        try
        {
            var opened = false;
            await Client.WaitPost(() => controller.TrySetReady(true));
            await Pair.RunTicksSync(10);
            await Client.WaitPost(() => opened = controller.TryOpenCharacterSetup());
            Assert.That(opened, Is.True);
            await Pair.RunTicksSync(10);

            await Client.WaitAssertion(() =>
            {
                var lobby = GetLobby();
                Assert.Multiple(() =>
                {
                    Assert.That(ticker.AreWeReady, Is.False, "Editing a character must not leave the player ready.");
                    Assert.That(lobby.CharacterSetupState.Visible, Is.True);
                    Assert.That(lobby.TileHost.Visible, Is.False);
                });
            });
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                controller.TrySetReady(false);
                GetLobbyState().SwitchState(LobbyGui.LobbyGuiState.Default);
            });
            await Pair.RunTicksSync(10);
        }
    }

    [Test]
    public async Task NoTileCutsOffItsContent()
    {
        var heights = new Dictionary<string, (float Needed, float Available)>();

        await Client.WaitPost(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            // Integration clients are headless (1280x720) but still lay out with real styles and fonts.
            // FrameUpdate is public on the internal UserInterfaceManager type.
            ui.GetType().GetMethod("FrameUpdate", BindingFlags.Instance | BindingFlags.Public)!
                .Invoke(ui, new object[] { new FrameEventArgs(1f / 60) });

            foreach (var tile in GetLobby().TileGrid.Children.OfType<MalinovLobbyTileControl>().Where(tile => tile.Visible))
            {
                // Natural height of the content at the width the tile gives it.
                var slot = tile.ContentSlot;
                tile.Content!.Measure(new Vector2(slot.Size.X, float.PositiveInfinity));
                heights[tile.TileId!.Value.Id] = (tile.Content.DesiredSize.Y, slot.Size.Y);
            }
        });

        await Client.WaitAssertion(() =>
        {
            Assert.That(heights, Is.Not.Empty);
            Assert.Multiple(() =>
            {
                foreach (var (id, (needed, available)) in heights)
                {
                    Assert.That(needed, Is.LessThanOrEqualTo(available + 0.5f), $"Tile {id} cuts off its content.");
                }
            });
        });
    }

    [Test]
    public async Task RunningRoundKeepsThePlayerInTheLobbyToJoin()
    {
        var serverTicker = SEntMan.System<GameTicker>();

        try
        {
            // Nobody is ready, so the round starts without the player, who stays in the lobby and may join later.
            await Server.WaitPost(() => serverTicker.StartRound(force: true));
            await Pair.RunTicksSync(10);

            await Client.WaitAssertion(() =>
            {
                var lobby = GetLobby();
                var loc = Client.ResolveDependency<ILocalizationManager>();
                var ready = Widget<MalinovReadyTileWidget>(lobby);
                var players = Widget<MalinovPlayersTileWidget>(lobby);

                Assert.Multiple(() =>
                {
                    Assert.That(GetController().Context.Phase, Is.EqualTo(MalinovLobbyPhase.InRound));
                    Assert.That(ready.CountdownLabel.Visible, Is.False, "A running round has no countdown.");
                    Assert.That(ready.ReadyButton.ToggleMode, Is.False);
                    Assert.That(ready.ReadyButton.Text, Is.EqualTo(loc.GetString("lobby-state-ready-button-join-state")));
                    Assert.That(players.CountCaption.Text, Is.EqualTo(loc.GetString("malinov-lobby-tile-players-in-game")));
                });
            });
        }
        finally
        {
            await Server.WaitPost(() => serverTicker.RestartRound());
            await Pair.RunTicksSync(10);
        }
    }

    [Test]
    public async Task TilesFadeInWhenTheLobbyOpens()
    {
        await ReenterLobby();
        // Fewer ticks than the shortest fade takes.
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var lobby = GetLobby();
            var tiles = lobby.TileGrid.Children.OfType<MalinovLobbyTileControl>().ToList();
            Assert.Multiple(() =>
            {
                Assert.That(lobby.TileGrid.Animated, Is.True);
                Assert.That(tiles.Any(tile => tile.HasRunningAnimation(MalinovLobbyTileControl.FadeInKey)), Is.True);
            });
        });
    }

    [Test]
    public async Task ReducedMotionKeepsTheLobbyStill()
    {
        await OverrideCVar(Side.Client, CCVars.ReducedMotion, true);

        await ReenterLobby();
        await Pair.RunTicksSync(2);

        await Client.WaitAssertion(() =>
        {
            var lobby = GetLobby();
            var tiles = lobby.TileGrid.Children.OfType<MalinovLobbyTileControl>().ToList();
            Assert.Multiple(() =>
            {
                Assert.That(lobby.TileGrid.Animated, Is.False, "Tiles jump to new places instead of sliding.");
                Assert.That(tiles.Any(tile => tile.HasRunningAnimation(MalinovLobbyTileControl.FadeInKey)), Is.False);
                Assert.That(tiles.All(tile => tile.Modulate.A >= 1f), Is.True, "Tiles show at once.");
            });
        });
    }

    private void AssertLobbyBuilt(LobbyGui lobby)
    {
        var controller = Client.ResolveDependency<IUserInterfaceManager>().GetUIController<MalinovLobbyTileUIController>();
        var expected = CProtoMan.EnumeratePrototypes<MalinovLobbyTilePrototype>()
            .OrderBy(p => p.Order)
            .ThenBy(p => p.ID)
            .Select(p => p.ID)
            .ToList();
        var tiles = lobby.TileGrid.Children.OfType<MalinovLobbyTileControl>().ToList();

        Assert.Multiple(() =>
        {
            Assert.That(tiles.Select(t => t.TileId?.Id), Is.EqualTo(expected), "One tile per prototype, ordered by `order`.");
            Assert.That(controller.Tiles, Is.EqualTo(tiles));
            Assert.That(tiles.All(t => t.Content != null), Is.True, "Every tile must have content.");
            Assert.That(lobby.LegacyPool.ChildCount, Is.Zero, "Every legacy lobby control must be shown in a tile.");

            // The chat registry of UIScreen must survive the reparenting into a tile.
            var chat = lobby.GetWidget<ChatBox>();
            Assert.That(chat, Is.SameAs(lobby.Chat));
            Assert.That(IsInsideTile(lobby.Chat), Is.True, "Chat must live in a tile.");
            Assert.That(IsInsideTile(lobby.AHelpButton), Is.True, "AHelpUIController reads this button from the lobby.");
            Assert.That(IsInsideTile(lobby.CharacterPreview), Is.True, "LobbyUIController reads the preview from the lobby.");
        });
    }

    /// <summary>
    /// Leaves the lobby for the game screen and comes back, as a player does who joins a round and returns.
    /// </summary>
    /// <remarks>
    /// Requesting the state the client is already in does nothing, so the lobby has to be left first.
    /// </remarks>
    private async Task ReenterLobby()
    {
        var states = Client.ResolveDependency<IStateManager>();
        await Client.WaitPost(() => states.RequestStateChange<GameplayState>());
        await Pair.RunTicksSync(1);
        await Client.WaitPost(() => states.RequestStateChange<LobbyState>());
    }

    private MalinovLobbyTileUIController GetController()
    {
        return Client.ResolveDependency<IUserInterfaceManager>().GetUIController<MalinovLobbyTileUIController>();
    }

    private static T Widget<T>(LobbyGui lobby) where T : Control
    {
        return lobby.TileGrid.Children
            .OfType<MalinovLobbyTileControl>()
            .Select(tile => tile.Content)
            .OfType<T>()
            .Single();
    }

    private static bool IsInsideTile(Control control)
    {
        for (var parent = control.Parent; parent != null; parent = parent.Parent)
        {
            if (parent is MalinovLobbyTileControl)
                return true;
        }

        return false;
    }

    /// <remarks>Throws instead of asserting, so it is also safe inside <c>WaitPost</c>.</remarks>
    private LobbyState GetLobbyState()
    {
        return (LobbyState) Client.ResolveDependency<IStateManager>().CurrentState;
    }

    private LobbyGui GetLobby()
    {
        return GetLobbyState().Lobby ?? throw new InvalidOperationException("Lobby screen is not loaded.");
    }
}
