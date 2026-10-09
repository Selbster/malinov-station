#nullable enable
using System;
using Content.Client._MalinovStation.Lobby;
using Content.Client._MalinovStation.Lobby.Tiles;
using Content.Shared._MalinovStation.Lobby;
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
[TestOf(typeof(MalinovPlayersTileWidget))]
[TestOf(typeof(MalinovRoundTileWidget))]
public sealed class MalinovInfoTileWidgetsTest : RobustUnitTest
{
    public override UnitTestProject Project => UnitTestProject.Client;

    private static readonly TimeSpan Now = TimeSpan.FromMinutes(70);

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
    public void PlayersAreUnknownUntilTheServerTells()
    {
        var widget = new MalinovPlayersTileWidget();

        widget.Refresh(new MalinovLobbyTileContext { CurTime = Now });

        Assert.Multiple(() =>
        {
            Assert.That(widget.OnlineValue.Text, Is.EqualTo("malinov-lobby-tile-value-unknown"));
            Assert.That(widget.CountValue.Text, Is.EqualTo("malinov-lobby-tile-value-unknown"));
            Assert.That(widget.CountCaption.Text, Is.EqualTo("malinov-lobby-tile-players-ready"));
        });
    }

    [Test]
    public void BeforeTheRoundPlayersShowOnlineAndReady()
    {
        var widget = new MalinovPlayersTileWidget();

        widget.Refresh(new MalinovLobbyTileContext { CurTime = Now, LobbyInfo = Info() });

        Assert.Multiple(() =>
        {
            Assert.That(widget.OnlineValue.Text, Is.EqualTo("12"));
            Assert.That(widget.CountValue.Text, Is.EqualTo("3"));
            Assert.That(widget.CountCaption.Text, Is.EqualTo("malinov-lobby-tile-players-ready"));
        });
    }

    [Test]
    public void DuringTheRoundPlayersShowOnlineAndInGame()
    {
        var widget = new MalinovPlayersTileWidget();

        widget.Refresh(new MalinovLobbyTileContext { CurTime = Now, IsGameStarted = true, LobbyInfo = Info() });

        Assert.Multiple(() =>
        {
            Assert.That(widget.OnlineValue.Text, Is.EqualTo("12"));
            Assert.That(widget.CountValue.Text, Is.EqualTo("9"));
            Assert.That(widget.CountCaption.Text, Is.EqualTo("malinov-lobby-tile-players-in-game"));
        });
    }

    [Test]
    public void RoundShowsOnlyTimeUntilTheServerTells()
    {
        var widget = new MalinovRoundTileWidget();

        widget.Refresh(new MalinovLobbyTileContext { CurTime = Now });

        Assert.Multiple(() =>
        {
            Assert.That(widget.NumberLabel.Visible, Is.False);
            Assert.That(widget.MapLabel.Visible, Is.False);
            Assert.That(widget.ModeLabel.Visible, Is.False);
            Assert.That(widget.ModeDescriptionLabel.Visible, Is.False);
            Assert.That(widget.RoundTimeLabel.Text, Is.EqualTo("lobby-state-player-status-round-not-started"));
        });
    }

    [Test]
    public void RoundShowsNumberMapAndMode()
    {
        var widget = new MalinovRoundTileWidget();

        widget.Refresh(new MalinovLobbyTileContext { CurTime = Now, LobbyInfo = Info() });

        Assert.Multiple(() =>
        {
            Assert.That(widget.NumberLabel.Visible, Is.True);
            Assert.That(widget.NumberLabel.GetMessage(), Is.EqualTo("malinov-lobby-tile-round-number"));
            Assert.That(widget.MapLabel.GetMessage(), Is.EqualTo("malinov-lobby-tile-round-map"));
            Assert.That(widget.ModeLabel.Visible, Is.True);
            Assert.That(widget.ModeLabel.GetMessage(), Is.EqualTo("malinov-lobby-tile-round-mode"));
            Assert.That(widget.ModeDescriptionLabel.Visible, Is.True);
            Assert.That(widget.ModeDescriptionLabel.GetMessage(), Is.EqualTo("malinov-lobby-tile-round-mode-description"));
        });
    }

    [Test]
    public void RoundSaysWhenNoMapIsChosenYet()
    {
        var widget = new MalinovRoundTileWidget();

        widget.Refresh(new MalinovLobbyTileContext { CurTime = Now, LobbyInfo = Info(mapNames: Array.Empty<string>()) });

        Assert.That(widget.MapLabel.GetMessage(), Is.EqualTo("malinov-lobby-tile-round-no-map"));
    }

    [Test]
    public void RoundHidesTheModeWithoutAPreset()
    {
        var widget = new MalinovRoundTileWidget();

        widget.Refresh(new MalinovLobbyTileContext { CurTime = Now, LobbyInfo = Info(modeTitle: null, modeDescription: null) });

        Assert.Multiple(() =>
        {
            Assert.That(widget.ModeLabel.Visible, Is.False);
            Assert.That(widget.ModeDescriptionLabel.Visible, Is.False);
        });
    }

    [Test]
    public void RoundHidesAnEmptyModeDescription()
    {
        var widget = new MalinovRoundTileWidget();

        widget.Refresh(new MalinovLobbyTileContext { CurTime = Now, LobbyInfo = Info(modeDescription: string.Empty) });

        Assert.Multiple(() =>
        {
            Assert.That(widget.ModeLabel.Visible, Is.True);
            Assert.That(widget.ModeDescriptionLabel.Visible, Is.False);
        });
    }

    [Test]
    public void RoundTimeRunsDuringTheRound()
    {
        var widget = new MalinovRoundTileWidget();

        widget.Refresh(new MalinovLobbyTileContext
        {
            CurTime = Now,
            IsGameStarted = true,
            RoundStartTime = Now - TimeSpan.FromMinutes(65),
            LobbyInfo = Info(),
        });

        Assert.That(widget.RoundTimeLabel.Text, Is.EqualTo("lobby-state-player-status-round-time"));
    }

    private static MalinovLobbyInfoEvent Info(
        string[]? mapNames = null,
        string? modeTitle = "preset-test-title",
        string? modeDescription = "preset-test-description")
    {
        return new MalinovLobbyInfoEvent(
            roundId: 271,
            playerCount: 12,
            readyCount: 3,
            inGameCount: 9,
            mapNames: mapNames ?? new[] { "Box Station" },
            modeTitle: modeTitle,
            modeDescription: modeDescription);
    }
}
