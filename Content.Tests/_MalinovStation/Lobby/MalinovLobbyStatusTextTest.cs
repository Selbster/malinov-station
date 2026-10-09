#nullable enable
using System;
using Content.Client._MalinovStation.Lobby;
using NUnit.Framework;
using Robust.Shared.Utility;

namespace Content.Tests._MalinovStation.Lobby;

[TestFixture]
[TestOf(typeof(MalinovLobbyStatusText))]
public sealed class MalinovLobbyStatusTextTest
{
    private static readonly TimeSpan Now = TimeSpan.FromMinutes(10);

    [Test]
    public void CountdownIsHiddenOnceTheRoundRuns()
    {
        var countdown = MalinovLobbyStatusText.GetCountdown(true, false, Now + TimeSpan.FromMinutes(2), Now);
        Assert.That(countdown.Kind, Is.EqualTo(MalinovLobbyCountdownKind.Hidden));
    }

    [Test]
    public void PausedCountdownWinsOverTheStartTime()
    {
        var countdown = MalinovLobbyStatusText.GetCountdown(false, true, Now - TimeSpan.FromSeconds(5), Now);
        Assert.That(countdown.Kind, Is.EqualTo(MalinovLobbyCountdownKind.Paused));
    }

    [Test]
    public void PassedStartTimeMeansTheRoundStartsSoon()
    {
        var countdown = MalinovLobbyStatusText.GetCountdown(false, false, Now - TimeSpan.FromSeconds(5), Now);
        Assert.That(countdown.Kind, Is.EqualTo(MalinovLobbyCountdownKind.Soon));
    }

    [Test]
    public void RunningCountdownReportsTheTimeLeft()
    {
        var countdown = MalinovLobbyStatusText.GetCountdown(false, false, Now + TimeSpan.FromSeconds(129), Now);

        Assert.Multiple(() =>
        {
            Assert.That(countdown.Kind, Is.EqualTo(MalinovLobbyCountdownKind.Running));
            Assert.That(countdown.Left, Is.EqualTo(TimeSpan.FromSeconds(129)));
        });
    }

    [TestCase(0, "0:00")]
    [TestCase(5, "0:05")]
    [TestCase(129, "2:09")]
    [TestCase(3599, "59:59")]
    [TestCase(3723, "1:02:03")]
    [TestCase(36000, "10:00:00")]
    public void TimeLeftUsesHoursOnlyWhenNeeded(int seconds, string expected)
    {
        Assert.That(MalinovLobbyStatusText.FormatTimeLeft(TimeSpan.FromSeconds(seconds)), Is.EqualTo(expected));
    }

    [Test]
    public void TimeLeftDropsFractionsOfASecond()
    {
        Assert.That(MalinovLobbyStatusText.FormatTimeLeft(TimeSpan.FromSeconds(129.9)), Is.EqualTo("2:09"));
    }

    [Test]
    public void NoMapNamesMeanNoMap()
    {
        Assert.That(MalinovLobbyStatusText.JoinMapNames(Array.Empty<string>()), Is.Null);
    }

    [Test]
    public void StationNamesAreJoined()
    {
        Assert.That(MalinovLobbyStatusText.JoinMapNames(new[] { "Box Station", "Outpost" }), Is.EqualTo("Box Station, Outpost"));
    }

    [Test]
    public void MapNamesAreEscapedForMarkup()
    {
        // Station names come from the server, so they must not be able to inject markup.
        Assert.That(MalinovLobbyStatusText.JoinMapNames(new[] { "[bold]Box[/bold]" }),
            Is.EqualTo(FormattedMessage.EscapeText("[bold]Box[/bold]")));
    }

    [TestCase(false, MalinovLobbyCountdownKind.Running, 30, true)]
    [TestCase(false, MalinovLobbyCountdownKind.Running, MalinovLobbyStatusText.ReadyAccentSeconds, true)]
    [TestCase(false, MalinovLobbyCountdownKind.Running, MalinovLobbyStatusText.ReadyAccentSeconds + 1, false)]
    [TestCase(false, MalinovLobbyCountdownKind.Running, 120, false)]
    [TestCase(false, MalinovLobbyCountdownKind.Soon, 0, true)]
    [TestCase(false, MalinovLobbyCountdownKind.Paused, 0, false)]
    [TestCase(false, MalinovLobbyCountdownKind.Hidden, 0, false)]
    [TestCase(true, MalinovLobbyCountdownKind.Running, 30, false)]
    [TestCase(true, MalinovLobbyCountdownKind.Soon, 0, false)]
    public void ReadyCallsForAttentionOnlyWhenTheRoundIsAboutToStartWithoutThePlayer(
        bool ready,
        MalinovLobbyCountdownKind kind,
        int secondsLeft,
        bool expected)
    {
        var countdown = new MalinovLobbyCountdown(kind, TimeSpan.FromSeconds(secondsLeft));
        Assert.That(MalinovLobbyStatusText.NeedsReadyAccent(countdown, ready), Is.EqualTo(expected));
    }

    [TestCase(0, null)]
    [TestCase(60, null)]
    [TestCase(61, "lobby-state-playtime-comment-normal")]
    [TestCase(179, "lobby-state-playtime-comment-normal")]
    [TestCase(180, "lobby-state-playtime-comment-concerning")]
    [TestCase(359, "lobby-state-playtime-comment-concerning")]
    [TestCase(360, "lobby-state-playtime-comment-grasstouchless")]
    [TestCase(719, "lobby-state-playtime-comment-grasstouchless")]
    [TestCase(720, "lobby-state-playtime-comment-selfdestructive")]
    [TestCase(5000, "lobby-state-playtime-comment-selfdestructive")]
    public void PlaytimeCommentAppearsAfterAnHourAndEscalates(int minutesToday, string? expected)
    {
        var comment = MalinovLobbyStatusText.GetPlaytimeComment(minutesToday);
        Assert.That(comment?.Id, Is.EqualTo(expected));
    }
}
