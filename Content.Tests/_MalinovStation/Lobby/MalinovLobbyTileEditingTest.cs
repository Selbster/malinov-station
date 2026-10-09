#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.Client._MalinovStation.Lobby;
using Content.Client.Stylesheets;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML.Proxy;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.Tests._MalinovStation.Lobby;

/// <remarks>
/// No locale is loaded in unit tests, so localized texts come back as their keys.
/// </remarks>
[TestFixture]
[TestOf(typeof(MalinovLobbyTileControl))]
public sealed class MalinovLobbyTileEditingTest : RobustUnitTest
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
    public void EditingTurnsTheHeaderIntoADragHandle()
    {
        var tile = Tile("Chat");

        tile.Editing = true;
        var editingFilter = tile.Header.MouseFilter;
        var editingCursor = tile.Header.DefaultCursorShape;
        tile.Editing = false;

        Assert.Multiple(() =>
        {
            Assert.That(editingFilter, Is.EqualTo(Control.MouseFilterMode.Stop));
            Assert.That(editingCursor, Is.EqualTo(Control.CursorShape.Move));
            Assert.That(tile.Header.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore),
                "Outside of editing the header lets clicks through like before.");
        });
    }

    [Test]
    public void EditingShowsTheHeaderOfUntitledTiles()
    {
        var tile = Tile(string.Empty);

        var hiddenAtFirst = !tile.Header.Visible;
        tile.Editing = true;
        var shownWhileEditing = tile.Header.Visible;
        tile.Editing = false;

        Assert.Multiple(() =>
        {
            Assert.That(hiddenAtFirst, Is.True);
            Assert.That(shownWhileEditing, Is.True, "Every tile needs a handle to be dragged by.");
            Assert.That(tile.Header.Visible, Is.False);
        });
    }

    [Test]
    public void OnlyHideableTilesOfferToHide()
    {
        var chat = Tile("Chat");
        var ready = Tile("Ready");
        ready.Hideable = false;

        chat.Editing = true;
        ready.Editing = true;

        Assert.Multiple(() =>
        {
            Assert.That(chat.HideButton.Visible, Is.True);
            Assert.That(chat.HideButton.Text, Is.EqualTo("malinov-lobby-tile-hide-button"));
            Assert.That(ready.HideButton.Visible, Is.False);
        });
    }

    [Test]
    public void HideButtonIsOnlyThereWhileEditing()
    {
        var tile = Tile("Chat");

        tile.Editing = true;
        tile.Editing = false;

        Assert.That(tile.HideButton.Visible, Is.False);
    }

    [Test]
    public void HiddenTileFadesWhileEditingAndOffersToComeBack()
    {
        var tile = Tile("Chat");

        tile.UserHidden = true;
        tile.Editing = true;
        var fadedAlpha = tile.ContentSlot.Modulate.A;
        var showText = tile.HideButton.Text;
        tile.UserHidden = false;

        Assert.Multiple(() =>
        {
            Assert.That(fadedAlpha, Is.LessThan(1f));
            Assert.That(showText, Is.EqualTo("malinov-lobby-tile-show-button"));
            Assert.That(tile.ContentSlot.Modulate.A, Is.EqualTo(1f));
            Assert.That(tile.HideButton.Text, Is.EqualTo("malinov-lobby-tile-hide-button"));
        });
    }

    [Test]
    public void HideButtonAsksTheOwnerToToggle()
    {
        var tile = Tile("Chat");
        tile.Editing = true;
        MalinovLobbyTileControl? asked = null;
        tile.HidePressed += pressed => asked = pressed;

        MalinovLobbyTestInput.Click(tile.HideButton);

        Assert.That(asked, Is.SameAs(tile));
    }

    [Test]
    public void HeaderPressAndReleaseAreReportedWhileEditing()
    {
        var tile = Tile("Chat");
        var pressed = 0;
        var released = 0;
        tile.DragPressed += _ => pressed++;
        tile.DragReleased += _ => released++;

        MalinovLobbyTestInput.Press(tile.Header);
        MalinovLobbyTestInput.Release(tile.Header);
        tile.Editing = true;
        MalinovLobbyTestInput.Press(tile.Header);
        MalinovLobbyTestInput.Release(tile.Header);

        Assert.Multiple(() =>
        {
            Assert.That(pressed, Is.EqualTo(1), "Only an editing tile can be dragged.");
            Assert.That(released, Is.EqualTo(1));
        });
    }

    [Test]
    public void DraggedTileFadesAndTheDropTargetIsHighlighted()
    {
        var dragged = Tile("Chat");
        var target = Tile("Round");

        dragged.Dragged = true;
        target.DropTarget = true;
        var draggedAlpha = dragged.Modulate.A;
        var highlighted = target.HasStyleClass(StyleClass.PanelDropTarget);
        dragged.Dragged = false;
        target.DropTarget = false;

        Assert.Multiple(() =>
        {
            Assert.That(draggedAlpha, Is.LessThan(1f));
            Assert.That(highlighted, Is.True);
            Assert.That(dragged.Modulate.A, Is.EqualTo(1f));
            Assert.That(target.HasStyleClass(StyleClass.PanelDropTarget), Is.False);
        });
    }

    [Test]
    public void ResizableTileOffersEveryEdgeAndCornerWhileEditing()
    {
        var chat = ResizableTile();
        var round = Tile("Round");

        var hiddenAtFirst = chat.ResizeHandles.Values.All(handle => !handle.Visible);
        chat.Editing = true;
        round.Editing = true;
        var shownWhileEditing = chat.ResizeHandles.Values.All(handle => handle.Visible);
        chat.Editing = false;

        Assert.Multiple(() =>
        {
            Assert.That(chat.ResizeHandles.Keys, Is.EquivalentTo(new[]
            {
                MalinovLobbyTileEdges.Left, MalinovLobbyTileEdges.Top, MalinovLobbyTileEdges.Right, MalinovLobbyTileEdges.Bottom,
                MalinovLobbyTileEdges.TopLeft, MalinovLobbyTileEdges.TopRight,
                MalinovLobbyTileEdges.BottomLeft, MalinovLobbyTileEdges.BottomRight,
            }));
            Assert.That(chat.Resizable, Is.True);
            Assert.That(round.Resizable, Is.False, "A tile without limits keeps its size.");
            Assert.That(hiddenAtFirst, Is.True);
            Assert.That(shownWhileEditing, Is.True);
            Assert.That(round.ResizeHandles.Values.All(handle => !handle.Visible), Is.True);
            Assert.That(chat.ResizeHandles.Values.All(handle => !handle.Visible), Is.True);
        });
    }

    [Test]
    public void TileThatOnlyChangesItsHeightOffersOnlyTheTopAndBottomEdges()
    {
        var tile = Tile("Menu");
        tile.MinTileSize = new Vector2i(2, 1);
        tile.MaxTileSize = new Vector2i(2, 3);

        tile.Editing = true;

        var shown = tile.ResizeHandles.Where(pair => pair.Value.Visible).Select(pair => pair.Key);
        Assert.That(shown, Is.EquivalentTo(new[] { MalinovLobbyTileEdges.Top, MalinovLobbyTileEdges.Bottom }));
    }

    [TestCase(MalinovLobbyTileEdges.Left)]
    [TestCase(MalinovLobbyTileEdges.Top)]
    [TestCase(MalinovLobbyTileEdges.Right)]
    [TestCase(MalinovLobbyTileEdges.Bottom)]
    [TestCase(MalinovLobbyTileEdges.TopLeft)]
    [TestCase(MalinovLobbyTileEdges.TopRight)]
    [TestCase(MalinovLobbyTileEdges.BottomLeft)]
    [TestCase(MalinovLobbyTileEdges.BottomRight)]
    public void HandleReportsTheEdgesItMovesWhileEditing(MalinovLobbyTileEdges edges)
    {
        var tile = ResizableTile();
        var handle = tile.ResizeHandles[edges];
        var pressed = new List<MalinovLobbyTileEdges>();
        var released = 0;
        tile.ResizePressed += (_, taken) => pressed.Add(taken);
        tile.ResizeReleased += _ => released++;

        MalinovLobbyTestInput.Press(handle);
        MalinovLobbyTestInput.Release(handle);
        tile.Editing = true;
        MalinovLobbyTestInput.Press(handle);
        MalinovLobbyTestInput.Release(handle);

        Assert.Multiple(() =>
        {
            Assert.That(pressed, Is.EqualTo(new[] { edges }), "Only an editing tile can be resized.");
            Assert.That(released, Is.EqualTo(1));
        });
    }

    [TestCase(MalinovLobbyTileEdges.Left, Control.CursorShape.HResize)]
    [TestCase(MalinovLobbyTileEdges.Right, Control.CursorShape.HResize)]
    [TestCase(MalinovLobbyTileEdges.Top, Control.CursorShape.VResize)]
    [TestCase(MalinovLobbyTileEdges.Bottom, Control.CursorShape.VResize)]
    [TestCase(MalinovLobbyTileEdges.TopLeft, Control.CursorShape.NWSEResize)]
    [TestCase(MalinovLobbyTileEdges.BottomRight, Control.CursorShape.NWSEResize)]
    [TestCase(MalinovLobbyTileEdges.TopRight, Control.CursorShape.NESWResize)]
    [TestCase(MalinovLobbyTileEdges.BottomLeft, Control.CursorShape.NESWResize)]
    public void CursorShowsTheWayAHandleResizes(MalinovLobbyTileEdges edges, Control.CursorShape cursor)
    {
        Assert.That(ResizableTile().ResizeHandles[edges].DefaultCursorShape, Is.EqualTo(cursor));
    }

    [Test]
    public void HandleLightsUpOnlyWhileThePointerIsOverIt()
    {
        var tile = ResizableTile();
        tile.Editing = true;
        var handle = tile.ResizeHandles[MalinovLobbyTileEdges.Left];

        var litAtFirst = handle.HasStyleClass(MalinovLobbyTileControl.StyleClassResizeHandleHovered);
        MalinovLobbyTestInput.Hover(handle);
        var litOnHover = handle.HasStyleClass(MalinovLobbyTileControl.StyleClassResizeHandleHovered);
        MalinovLobbyTestInput.Leave(handle);

        Assert.Multiple(() =>
        {
            Assert.That(litAtFirst, Is.False);
            Assert.That(litOnHover, Is.True);
            Assert.That(handle.HasStyleClass(MalinovLobbyTileControl.StyleClassResizeHandleHovered), Is.False);
            Assert.That(handle.HasStyleClass(MalinovLobbyTileControl.StyleClassResizeHandle), Is.True);
        });
    }

    private static MalinovLobbyTileControl ResizableTile()
    {
        var tile = Tile("Chat");
        tile.MinTileSize = new Vector2i(2, 2);
        tile.MaxTileSize = new Vector2i(6, 8);
        return tile;
    }

    private static MalinovLobbyTileControl Tile(string title)
    {
        var tile = new MalinovLobbyTileControl();
        tile.SetTitle(title);
        tile.Adopt(new Control());
        return tile;
    }
}
