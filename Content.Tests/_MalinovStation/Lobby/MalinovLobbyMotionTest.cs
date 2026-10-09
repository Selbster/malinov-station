#nullable enable
using Content.Client._MalinovStation.Lobby;
using NUnit.Framework;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML.Proxy;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.UnitTesting;

namespace Content.Tests._MalinovStation.Lobby;

/// <summary>
/// Tiles fading in and the lobby background fading from one picture to the next.
/// </summary>
[TestFixture]
[TestOf(typeof(MalinovLobbyTileControl))]
[TestOf(typeof(MalinovLobbyBackground))]
public sealed class MalinovLobbyMotionTest : RobustUnitTest
{
    public override UnitTestProject Project => UnitTestProject.Client;

    // A little more than a frame, so an animation surely gets past its last key.
    private const float Frame = 0.02f;

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
    public void TileFadesInFromTransparent()
    {
        var tile = new MalinovLobbyTileControl();

        tile.FadeIn(0f);
        var atStart = tile.Modulate.A;
        MalinovLobbyTestInput.Frame(tile, MalinovLobbyTileControl.FadeInSeconds / 2);
        var halfway = tile.Modulate.A;
        MalinovLobbyTestInput.Frame(tile, MalinovLobbyTileControl.FadeInSeconds / 2 + Frame);
        MalinovLobbyTestInput.Frame(tile, Frame);

        Assert.Multiple(() =>
        {
            Assert.That(atStart, Is.Zero);
            Assert.That(halfway, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(tile.Modulate.A, Is.EqualTo(1f));
            Assert.That(tile.HasRunningAnimation(MalinovLobbyTileControl.FadeInKey), Is.False);
        });
    }

    [Test]
    public void TileWaitsForItsTurnToFadeIn()
    {
        var tile = new MalinovLobbyTileControl();

        tile.FadeIn(0.1f);
        MalinovLobbyTestInput.Frame(tile, 0.05f);
        var waiting = tile.Modulate.A;
        MalinovLobbyTestInput.Frame(tile, 0.05f + MalinovLobbyTileControl.FadeInSeconds + Frame);
        MalinovLobbyTestInput.Frame(tile, Frame);

        Assert.Multiple(() =>
        {
            Assert.That(waiting, Is.Zero, "Tiles further in the order fade in after the ones before them.");
            Assert.That(tile.Modulate.A, Is.EqualTo(1f));
        });
    }

    [Test]
    public void DraggingStopsTheFadeIn()
    {
        var tile = new MalinovLobbyTileControl();
        tile.FadeIn(0f);

        tile.Dragged = true;
        MalinovLobbyTestInput.Frame(tile, MalinovLobbyTileControl.FadeInSeconds + Frame);

        Assert.Multiple(() =>
        {
            Assert.That(tile.HasRunningAnimation(MalinovLobbyTileControl.FadeInKey), Is.False);
            Assert.That(tile.Modulate.A, Is.LessThan(1f), "The dragged tile stays faded instead of being made opaque.");
        });
    }

    [Test]
    public void FirstBackgroundShowsAtOnce()
    {
        var background = new MalinovLobbyBackground();
        var texture = new TestTexture();

        background.SetTexture(texture, fade: true);

        Assert.Multiple(() =>
        {
            Assert.That(background.ShownLayer.Texture, Is.SameAs(texture), "There is nothing to fade from.");
            Assert.That(background.IncomingLayer.Visible, Is.False);
        });
    }

    [Test]
    public void NewBackgroundFadesInOverTheOld()
    {
        var background = new MalinovLobbyBackground();
        var old = new TestTexture();
        var next = new TestTexture();
        background.SetTexture(old, fade: true);

        background.SetTexture(next, fade: true);
        var shownWhileFading = background.ShownLayer.Texture;
        MalinovLobbyTestInput.Frame(background.IncomingLayer, MalinovLobbyBackground.FadeSeconds / 2);
        var halfway = background.IncomingLayer.Modulate.A;
        MalinovLobbyTestInput.Frame(background.IncomingLayer, MalinovLobbyBackground.FadeSeconds / 2 + Frame);
        MalinovLobbyTestInput.Frame(background.IncomingLayer, Frame);

        Assert.Multiple(() =>
        {
            Assert.That(shownWhileFading, Is.SameAs(old), "The old picture stays underneath while the new one fades in.");
            Assert.That(halfway, Is.GreaterThan(0f).And.LessThan(1f));
            Assert.That(background.ShownLayer.Texture, Is.SameAs(next));
            Assert.That(background.IncomingLayer.Visible, Is.False);
            Assert.That(background.Texture, Is.SameAs(next));
        });
    }

    [Test]
    public void WithoutFadingTheBackgroundChangesAtOnce()
    {
        var background = new MalinovLobbyBackground();
        var next = new TestTexture();
        background.SetTexture(new TestTexture(), fade: false);

        background.SetTexture(next, fade: false);

        Assert.Multiple(() =>
        {
            Assert.That(background.ShownLayer.Texture, Is.SameAs(next));
            Assert.That(background.IncomingLayer.Visible, Is.False);
        });
    }

    [Test]
    public void ChangeDuringAFadeStartsFromThePictureOnTheWay()
    {
        var background = new MalinovLobbyBackground();
        var first = new TestTexture();
        var second = new TestTexture();
        var third = new TestTexture();
        background.SetTexture(first, fade: true);
        background.SetTexture(second, fade: true);

        background.SetTexture(third, fade: true);

        Assert.Multiple(() =>
        {
            Assert.That(background.ShownLayer.Texture, Is.SameAs(second), "The picture that was fading in is now underneath.");
            Assert.That(background.IncomingLayer.Texture, Is.SameAs(third));
            Assert.That(background.Texture, Is.SameAs(third));
        });
    }

    [Test]
    public void SameBackgroundAgainChangesNothing()
    {
        var background = new MalinovLobbyBackground();
        var texture = new TestTexture();
        background.SetTexture(new TestTexture(), fade: true);
        background.SetTexture(texture, fade: true);

        background.SetTexture(texture, fade: true);

        Assert.That(background.IncomingLayer.HasRunningAnimation(MalinovLobbyBackground.FadeKey), Is.True,
            "The status the server sends again and again must not restart the fade.");
    }

    private sealed class TestTexture : Texture
    {
        public TestTexture() : base(new Vector2i(1, 1))
        {
        }

        public override Color GetPixel(int x, int y)
        {
            return Color.White;
        }
    }
}
