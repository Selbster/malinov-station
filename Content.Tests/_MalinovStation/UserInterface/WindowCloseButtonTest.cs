#nullable enable
using System;
using System.Numerics;
using System.Reflection;
using Content.Client._MalinovStation.UserInterface;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML.Proxy;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.Tests._MalinovStation.UserInterface;

[TestFixture]
public sealed class WindowCloseButtonTest : RobustUnitTest
{
    public override UnitTestProject Project => UnitTestProject.Client;

    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo ButtonDown = typeof(BaseButton).GetMethod("KeyBindDown", InstanceMembers)!;
    private static readonly MethodInfo ButtonUp = typeof(BaseButton).GetMethod("KeyBindUp", InstanceMembers)!;

    protected override void OverrideIoC()
    {
        base.OverrideIoC();
        var proxyInterface = typeof(XamlProxyManagerStub).Assembly.GetType("Robust.Client.UserInterface.XAML.Proxy.IXamlProxyManager")!;
        IoCManager.Instance!.Register(proxyInterface, typeof(XamlProxyManagerStub), overwrite: true);
    }

    [OneTimeSetUp]
    public void SetupUi()
    {
        IoCManager.Resolve<IUserInterfaceManager>().InitializeTesting();
        var controller = new MalinovWindowCloseUIController();
        IoCManager.InjectDependencies(controller);
        controller.Initialize();
    }

    [Test]
    public void HeaderClosesReopenedWindowExactlyOncePerClick()
    {
        using var window = new RecordingWindow();
        var closeEvents = 0;
        window.OnClose += () => closeEvents++;

        for (var cycle = 1; cycle <= 4; cycle++)
        {
            window.Open();
            Click(HeaderButton(window));

            Assert.Multiple(() =>
            {
                Assert.That(window.IsOpen, Is.False, $"Opening {cycle} must remain closable.");
                Assert.That(window.CloseCalls, Is.EqualTo(cycle), "A click must call virtual Close only once.");
                Assert.That(closeEvents, Is.EqualTo(cycle));
            });
        }
    }

    [Test]
    public void TileSpawnHeaderClosesAfterRepeatedReopening()
    {
        using var window = new TileSpawnWindow();
        var closeEvents = 0;
        window.OnClose += () => closeEvents++;

        for (var cycle = 1; cycle <= 3; cycle++)
        {
            window.Open();
            Click(HeaderButton(window));

            Assert.Multiple(() =>
            {
                Assert.That(window.IsOpen, Is.False, $"Tile spawn opening {cycle} must remain closable.");
                Assert.That(closeEvents, Is.EqualTo(cycle));
            });
        }
    }

    [Test]
    public void HeaderClosesAfterAnInitialProgrammaticOpenAndClose()
    {
        using var window = new RecordingWindow();
        var closeEvents = 0;
        window.OnClose += () => closeEvents++;

        // Sandbox pre-opens and closes its window before the user opens it.
        window.Open();
        window.Close();
        window.Open();
        Click(HeaderButton(window));

        Assert.Multiple(() =>
        {
            Assert.That(window.IsOpen, Is.False);
            Assert.That(window.CloseCalls, Is.EqualTo(2));
            Assert.That(closeEvents, Is.EqualTo(2));
        });
    }

    [Test]
    public void ReopeningPreservesHiddenAndDisabledHeaderState()
    {
        using var window = new RecordingWindow();
        var button = HeaderButton(window);
        button.Visible = false;
        button.Disabled = true;
        window.Open();
        window.Close();
        window.Open();

        Assert.Multiple(() =>
        {
            Assert.That(button.Visible, Is.False);
            Assert.That(button.Disabled, Is.True);
        });

        button.Visible = true;
        Click(button);
        Assert.Multiple(() =>
        {
            Assert.That(window.IsOpen, Is.True, "Disabled headers must reject ordinary button input.");
            Assert.That(window.CloseCalls, Is.EqualTo(1));
        });

        button.Disabled = false;
        Click(button);
        Assert.Multiple(() =>
        {
            Assert.That(window.IsOpen, Is.False);
            Assert.That(window.CloseCalls, Is.EqualTo(2));
        });
    }

    [Test]
    public void ContentDeleteButtonWithCloseStyleKeepsItsOwnAction()
    {
        using var window = new RecordingWindow();
        var row = new Button();
        var remove = new TextureButton { StyleClasses = { DefaultWindow.StyleClassWindowCloseButton } };
        window.Contents.AddChild(row);
        row.AddChild(remove);
        var removals = 0;
        remove.OnPressed += _ => removals++;

        window.Open();
        window.Close();
        window.Open();
        Click(remove);

        Assert.Multiple(() =>
        {
            Assert.That(removals, Is.EqualTo(1));
            Assert.That(window.IsOpen, Is.True, "VV uses the close-button style for component removal too.");
            Assert.That(window.CloseCalls, Is.EqualTo(1));
        });

        Click(HeaderButton(window));
        Assert.That(window.IsOpen, Is.False);
        Assert.That(window.CloseCalls, Is.EqualTo(2));
    }

    [Test]
    public void DifferentWindowsKeepIndependentCloseActions()
    {
        using var first = new RecordingWindow();
        using var second = new RecordingWindow();

        for (var cycle = 1; cycle <= 3; cycle++)
        {
            first.Open();
            second.Open();
            Click(HeaderButton(first));
            Assert.Multiple(() =>
            {
                Assert.That(first.IsOpen, Is.False);
                Assert.That(second.IsOpen, Is.True);
                Assert.That(first.CloseCalls, Is.EqualTo(cycle));
                Assert.That(second.CloseCalls, Is.EqualTo(cycle - 1));
            });

            Click(HeaderButton(second));
            Assert.That(second.IsOpen, Is.False);
            Assert.That(second.CloseCalls, Is.EqualTo(cycle));
        }
    }

    private static TextureButton HeaderButton(DefaultWindow window)
    {
        return window.FindControl<TextureButton>("CloseButton");
    }

    private static void Click(BaseButton button)
    {
        // The dummy theme has no header texture; give the actual button a hit-testable area.
        button.MuteSounds = true;
        button.MinSize = new Vector2(32);
        button.Measure(new Vector2(32));
        button.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(32)));
        var relative = button.Size / 2;
        var pointer = new ScreenCoordinates(button.GlobalPixelPosition + relative * button.UIScale, WindowId.Main);

        // Invoke the protected input entry points, preserving disabled checks and press/release semantics.
        ButtonDown.Invoke(button, new object[]
        {
            new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, pointer, true, relative, relative * button.UIScale),
        });
        ButtonUp.Invoke(button, new object[]
        {
            new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, pointer, true, relative, relative * button.UIScale),
        });
    }

    private sealed class RecordingWindow : DefaultWindow
    {
        public int CloseCalls { get; private set; }

        public override void Close()
        {
            CloseCalls++;
            base.Close();
        }
    }
}
