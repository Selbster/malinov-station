#nullable enable
using System;
using Content.Client._MalinovStation.Lobby;
using Content.Client._MalinovStation.Lobby.Tiles;
using NUnit.Framework;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.XAML.Proxy;
using Robust.Shared.IoC;
using Robust.UnitTesting;

namespace Content.Tests._MalinovStation.Lobby;

/// <remarks>
/// No locale is loaded in unit tests, so localized texts come back as their keys.
/// </remarks>
[TestFixture]
[TestOf(typeof(MalinovReadyTileWidget))]
[TestOf(typeof(MalinovLobbyTileControl))]
public sealed class MalinovReadyTileWidgetTest : RobustUnitTest
{
    public override UnitTestProject Project => UnitTestProject.Client;

    private static readonly TimeSpan Now = TimeSpan.FromMinutes(10);

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
    public void BeforeTheRoundReadyIsAToggle()
    {
        var widget = new MalinovReadyTileWidget(new MalinovRecordingLobbyActions());

        widget.Refresh(PreRound(ready: false));

        Assert.Multiple(() =>
        {
            Assert.That(widget.ReadyButton.ToggleMode, Is.True);
            Assert.That(widget.ReadyButton.Pressed, Is.False);
            Assert.That(widget.ReadyButton.Disabled, Is.False);
            Assert.That(widget.ReadyButton.Text, Is.EqualTo("lobby-state-player-status-not-ready"));
            Assert.That(widget.ObserveButton.Disabled, Is.True, "There is nothing to observe before the round.");
        });
    }

    [Test]
    public void ReadyStateComesFromTheServer()
    {
        var widget = new MalinovReadyTileWidget(new MalinovRecordingLobbyActions());

        widget.Refresh(PreRound(ready: true));

        Assert.Multiple(() =>
        {
            Assert.That(widget.ReadyButton.Pressed, Is.True);
            Assert.That(widget.ReadyButton.Text, Is.EqualTo("lobby-state-player-status-ready"));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DuringTheRoundReadyBecomesJoin(bool lateJoinDisallowed)
    {
        var widget = new MalinovReadyTileWidget(new MalinovRecordingLobbyActions());
        widget.Refresh(PreRound(ready: true));

        widget.Refresh(PreRound(ready: true) with { IsGameStarted = true, DisallowedLateJoin = lateJoinDisallowed });

        Assert.Multiple(() =>
        {
            Assert.That(widget.ReadyButton.ToggleMode, Is.False);
            Assert.That(widget.ReadyButton.Pressed, Is.False);
            Assert.That(widget.ReadyButton.Disabled, Is.EqualTo(lateJoinDisallowed));
            Assert.That(widget.ReadyButton.Text, Is.EqualTo("lobby-state-ready-button-join-state"));
            Assert.That(widget.ObserveButton.Disabled, Is.False);
            Assert.That(widget.CountdownLabel.Visible, Is.False, "A running round has no countdown.");
        });
    }

    [Test]
    public void CountdownFollowsTheTicker()
    {
        var widget = new MalinovReadyTileWidget(new MalinovRecordingLobbyActions());

        widget.Refresh(PreRound(ready: false));
        var running = widget.CountdownLabel.Text;
        widget.Refresh(PreRound(ready: false) with { StartTime = Now - TimeSpan.FromSeconds(1) });
        var soon = widget.CountdownLabel.Text;

        Assert.Multiple(() =>
        {
            Assert.That(widget.CountdownLabel.Visible, Is.True);
            Assert.That(running, Is.EqualTo("lobby-state-round-start-countdown-text"));
            Assert.That(soon, Is.EqualTo("lobby-state-soon"));
        });
    }

    [Test]
    public void ReadyStandsOutInTheLastMinuteUntilThePlayerIsReady()
    {
        var widget = new MalinovReadyTileWidget(new MalinovRecordingLobbyActions());
        var lastMinute = Now + TimeSpan.FromSeconds(30);

        widget.Refresh(PreRound(ready: false));
        var earlier = widget.Accented;
        widget.Refresh(PreRound(ready: false) with { StartTime = lastMinute });
        var notReady = widget.Accented;
        widget.Refresh(PreRound(ready: true) with { StartTime = lastMinute });

        Assert.Multiple(() =>
        {
            Assert.That(earlier, Is.False, "Two minutes before the start there is no hurry yet.");
            Assert.That(notReady, Is.True);
            Assert.That(widget.Accented, Is.False, "A ready player has nothing left to do.");
        });
    }

    [Test]
    public void TileStandsOutWhenItsWidgetAsksForIt()
    {
        var tile = new MalinovLobbyTileControl();
        tile.SetTitle("Ready");
        tile.Adopt(new MalinovReadyTileWidget(new MalinovRecordingLobbyActions()));

        tile.Refresh(PreRound(ready: false) with { StartTime = Now + TimeSpan.FromSeconds(30) });
        var accented = tile.Accented;
        tile.Refresh(PreRound(ready: true) with { StartTime = Now + TimeSpan.FromSeconds(30) });

        Assert.Multiple(() =>
        {
            Assert.That(accented, Is.True);
            Assert.That(tile.Accented, Is.False);
        });
    }

    [Test]
    public void TileWithPlainContentIgnoresRefreshes()
    {
        var content = new Control();
        var tile = new MalinovLobbyTileControl();
        tile.Adopt(content);

        tile.Refresh(PreRound(ready: false) with { StartTime = Now + TimeSpan.FromSeconds(30) });

        Assert.Multiple(() =>
        {
            Assert.That(tile.Accented, Is.False);
            Assert.That(tile.Content, Is.SameAs(content));
        });
    }

    [Test]
    public void PlaytimeCommentShowsAfterAnHour()
    {
        var widget = new MalinovReadyTileWidget(new MalinovRecordingLobbyActions());

        widget.Refresh(PreRound(ready: false) with { PlaytimeMinutesToday = 30 });
        var hiddenAtFirst = !widget.PlaytimeComment.Visible;
        widget.Refresh(PreRound(ready: false) with { PlaytimeMinutesToday = 200 });

        Assert.Multiple(() =>
        {
            Assert.That(hiddenAtFirst, Is.True);
            Assert.That(widget.PlaytimeComment.Visible, Is.True);
        });
    }

    [Test]
    public void TogglingReadyAsksTheController()
    {
        var actions = new MalinovRecordingLobbyActions();
        var widget = new MalinovReadyTileWidget(actions);
        widget.Refresh(PreRound(ready: false));

        MalinovLobbyTestInput.Click(widget.ReadyButton);

        Assert.Multiple(() =>
        {
            Assert.That(actions.ReadyRequests, Is.EqualTo(new[] { true }));
            Assert.That(actions.JoinRequests, Is.Zero, "Joining is only for a running round.");
        });
    }

    [Test]
    public void PressingJoinAsksTheController()
    {
        var actions = new MalinovRecordingLobbyActions();
        var widget = new MalinovReadyTileWidget(actions);
        widget.Refresh(PreRound(ready: false) with { IsGameStarted = true });

        MalinovLobbyTestInput.Click(widget.ReadyButton);

        Assert.Multiple(() =>
        {
            Assert.That(actions.JoinRequests, Is.EqualTo(1));
            Assert.That(actions.ReadyRequests, Is.Empty);
        });
    }

    private static MalinovLobbyTileContext PreRound(bool ready)
    {
        return new MalinovLobbyTileContext
        {
            AreWeReady = ready,
            StartTime = Now + TimeSpan.FromSeconds(129),
            CurTime = Now,
        };
    }
}
