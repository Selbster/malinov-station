#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client.UserInterface.Systems.Sandbox.Windows;
using Content.IntegrationTests.Fixtures;
using Robust.Client.Input;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers.Implementations;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Serilog.Events;

namespace Content.IntegrationTests.Tests._MalinovStation.Client;

public sealed class SandboxWindowCloseTest : GameTest
{
    [Test]
    public async Task SandboxAndTileWindowsKeepHeaderAndEscapeClosingAfterReuse()
    {
        SandboxWindow? sandbox = null;
        TileSpawnWindow? tiles = null;
        TileSpawningUIController? tileController = null;
        var sandboxCloses = 0;
        var tileCloses = 0;
        Action onSandboxClose = () => sandboxCloses++;
        Action onTileClose = () => tileCloses++;
        Action<BaseButton.ButtonEventArgs> openTiles = _ => tileController!.ToggleWindow();

        Pair.ClientLogHandler.JudgeLog += AllowExistingTileTextureWarning;
        try
        {
            await Client.WaitPost(() =>
            {
                var ui = Client.ResolveDependency<IUserInterfaceManager>();
                tileController = ui.GetUIController<TileSpawningUIController>();
                tileController.CloseWindow();
                sandbox = ui.CreateWindow<SandboxWindow>();

                // SandboxUIController.EnsureWindow pre-centers this way, before its first visible use.
                sandbox.OpenCentered();
                sandbox.Close();
                sandbox.OnClose += onSandboxClose;
                sandbox.SpawnTilesButton.OnPressed += openTiles;
                sandbox.Open();
            });
            await Click(sandbox!.SpawnTilesButton);
            await Client.WaitPost(() =>
            {
                tiles = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<TileSpawnWindow>().Single();
                tiles.OnClose += onTileClose;
            });
            await AssertWindows(true, true, 0, 0);
            await AssertTileSearchFocused();

            // The search field must not intercept a click on the other window's header.
            await Click(HeaderCloseButton(sandbox));
            await AssertWindows(false, true, 1, 0);
            await Click(HeaderCloseButton(tiles!));
            await AssertWindows(false, false, 1, 1);

            await ReopenBoth();
            await AssertTileSearchFocused();
            await CloseRecentWindow();
            await AssertWindows(true, false, 1, 2);
            await CloseRecentWindow();
            await AssertWindows(false, false, 2, 2);

            // Escape must leave the same cached windows usable, including both header callbacks.
            await ReopenBoth();
            await Click(HeaderCloseButton(tiles!));
            await AssertWindows(true, false, 2, 3);
            await Click(HeaderCloseButton(sandbox));
            await AssertWindows(false, false, 3, 3);
        }
        finally
        {
            Pair.ClientLogHandler.JudgeLog -= AllowExistingTileTextureWarning;
            await Client.WaitPost(() =>
            {
                if (tiles != null)
                    tiles.OnClose -= onTileClose;
                tileController?.CloseWindow();
                if (sandbox != null)
                {
                    sandbox.OnClose -= onSandboxClose;
                    sandbox.SpawnTilesButton.OnPressed -= openTiles;
                    sandbox.Close();
                }
            });
        }

        async Task ReopenBoth()
        {
            await Client.WaitPost(() => sandbox!.Open());
            await Click(sandbox!.SpawnTilesButton);
            await Client.WaitAssertion(() =>
            {
                var visibleTiles = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.OfType<TileSpawnWindow>().Single();
                Assert.That(visibleTiles, Is.SameAs(tiles), "The real tile controller must reuse its existing window.");
                Assert.That(sandbox.IsOpen, Is.True);
            });
        }

        Task AssertWindows(bool sandboxOpen, bool tilesOpen, int expectedSandboxCloses, int expectedTileCloses) =>
            Client.WaitAssertion(() =>
            {
                Assert.Multiple(() =>
                {
                    Assert.That(sandbox!.IsOpen, Is.EqualTo(sandboxOpen));
                    Assert.That(tiles!.IsOpen, Is.EqualTo(tilesOpen));
                    Assert.That(sandboxCloses, Is.EqualTo(expectedSandboxCloses), "Every Sandbox close must fire exactly once.");
                    Assert.That(tileCloses, Is.EqualTo(expectedTileCloses), "Closing one window must not close the other.");
                });
            });

        Task AssertTileSearchFocused() => Client.WaitAssertion(() =>
            Assert.That(Client.ResolveDependency<IUserInterfaceManager>().KeyboardFocused, Is.SameAs(tiles!.SearchBar)));
    }

    private async Task CloseRecentWindow()
    {
        await Client.WaitPost(() =>
            Client.ResolveDependency<IInputManager>().GetInputCommand(EngineKeyFunctions.WindowCloseRecent)!.Enabled(null));
        await Pair.RunTicksSync(1);
    }

    private static bool AllowExistingTileTextureWarning(string sawmill, LogEvent message)
    {
        // The existing FloorPlanetDirt tile prototype points at a PNG within an RSI. Opening the real
        // tile picker loads it directly; allow only that known resource warning in this UI test.
        return sawmill == "res" && message.Level == LogEventLevel.Warning && message.Exception == null &&
               message.RenderMessage() == "Loading raw texture inside RSI: \"/Textures/Tiles/Planet/dirt.rsi/dirt.png\". Refer to the RSI state instead of the raw PNG.";
    }

    private async Task Click(Control control)
    {
        ScreenCoordinates pointer = default;
        await Client.WaitPost(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            // Integration clients are headless, but still use real styles, layout and screen coordinates.
            InvokeUi(ui, "FrameUpdate", new FrameEventArgs(1f / 60));
            pointer = new ScreenCoordinates(control.GlobalPixelPosition + control.PixelSize / 2, control.Window!.Id);
        });
        await Client.WaitAssertion(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            Assert.That(control.PixelWidth, Is.GreaterThan(0));
            Assert.That(control.PixelHeight, Is.GreaterThan(0));
            Assert.That(ui.MouseGetControl(pointer), Is.SameAs(control), "The click must hit the visible control, not bypass another window.");
        });
        await Client.WaitPost(() =>
        {
            var ui = Client.ResolveDependency<IUserInterfaceManager>();
            // These methods are public on the internal UIManager type. Use the normal focus path,
            // so a focused tile-search LineEdit cannot divert the click to itself.
            InvokeUi(ui, "HandleCanFocusDown", pointer, null);
            try
            {
                InvokeUi(ui, "KeyBindDown", new BoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, pointer, true));
                InvokeUi(ui, "KeyBindUp", new BoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, pointer, true));
            }
            finally
            {
                InvokeUi(ui, "HandleCanFocusUp");
            }
        });
        await Pair.RunTicksSync(1);
    }

    private static void InvokeUi(IUserInterfaceManager ui, string method, params object?[] args)
    {
        ui.GetType().GetMethod(method, BindingFlags.Instance | BindingFlags.Public)!.Invoke(ui, args);
    }

    private static TextureButton HeaderCloseButton(DefaultWindow window) =>
        Descendants(window).OfType<TextureButton>().Single(button => button.HasStyleClass(DefaultWindow.StyleClassWindowCloseButton));

    private static IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}
