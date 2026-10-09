using Robust.Shared.Utility;

namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// What the round start countdown shows.
/// </summary>
public enum MalinovLobbyCountdownKind : byte
{
    /// <summary>
    /// The round runs; there is nothing to count down.
    /// </summary>
    Hidden,

    /// <summary>
    /// The countdown is paused.
    /// </summary>
    Paused,

    /// <summary>
    /// The start time has passed and the round is about to start.
    /// </summary>
    Soon,

    /// <summary>
    /// The countdown runs; time left is known.
    /// </summary>
    Running,
}

/// <summary>
/// Round start countdown: what to show and how much time is left when it runs.
/// </summary>
public readonly record struct MalinovLobbyCountdown(MalinovLobbyCountdownKind Kind, TimeSpan Left);

/// <summary>
/// Pure decisions behind the lobby status texts, kept free of controls and localization so they can be unit tested.
/// </summary>
public static class MalinovLobbyStatusText
{
    /// <summary>
    /// Playtime today after which the lobby comments on it.
    /// </summary>
    public const int PlaytimeCommentMinutes = 60;

    /// <summary>
    /// Time left before the round start from which a player who is not ready is called to get ready.
    /// </summary>
    public const int ReadyAccentSeconds = 60;

    /// <param name="isGameStarted">Whether the round runs.</param>
    /// <param name="paused">Whether the countdown is paused.</param>
    /// <param name="startTime">Game time at which the round is due to start.</param>
    /// <param name="curTime">Current game time.</param>
    public static MalinovLobbyCountdown GetCountdown(bool isGameStarted, bool paused, TimeSpan startTime, TimeSpan curTime)
    {
        if (isGameStarted)
            return new MalinovLobbyCountdown(MalinovLobbyCountdownKind.Hidden, TimeSpan.Zero);

        if (paused)
            return new MalinovLobbyCountdown(MalinovLobbyCountdownKind.Paused, TimeSpan.Zero);

        if (startTime < curTime)
            return new MalinovLobbyCountdown(MalinovLobbyCountdownKind.Soon, TimeSpan.Zero);

        return new MalinovLobbyCountdown(MalinovLobbyCountdownKind.Running, startTime - curTime);
    }

    /// <summary>
    /// Whether the ready tile should stand out: the round is about to start and the player is not ready.
    /// A paused countdown is no reason to hurry.
    /// </summary>
    public static bool NeedsReadyAccent(MalinovLobbyCountdown countdown, bool ready)
    {
        if (ready)
            return false;

        return countdown.Kind switch
        {
            MalinovLobbyCountdownKind.Soon => true,
            MalinovLobbyCountdownKind.Running => countdown.Left.TotalSeconds <= ReadyAccentSeconds,
            _ => false,
        };
    }

    /// <summary>
    /// Formats time left as <c>m:ss</c>, or <c>h:mm:ss</c> from one hour on. Fractions of a second are dropped.
    /// </summary>
    public static string FormatTimeLeft(TimeSpan left)
    {
        return left.TotalHours >= 1
            ? $"{Math.Floor(left.TotalHours)}:{left.Minutes:D2}:{left.Seconds:D2}"
            : $"{left.Minutes}:{left.Seconds:D2}";
    }

    /// <summary>
    /// Joins map or station names for display, escaped for markup since they come from the server.
    /// </summary>
    /// <returns><c>null</c> when there are no names, meaning no map is selected yet.</returns>
    public static string? JoinMapNames(IReadOnlyList<string> names)
    {
        if (names.Count == 0)
            return null;

        return FormattedMessage.EscapeText(string.Join(", ", names));
    }

    /// <summary>
    /// Comment on today's playtime, or <c>null</c> while it is under <see cref="PlaytimeCommentMinutes"/>.
    /// The comment gets more insistent the longer the player has played.
    /// </summary>
    public static LocId? GetPlaytimeComment(float minutesToday)
    {
        if (minutesToday <= PlaytimeCommentMinutes)
            return null;

        return minutesToday switch
        {
            < 180 => "lobby-state-playtime-comment-normal",
            < 360 => "lobby-state-playtime-comment-concerning",
            < 720 => "lobby-state-playtime-comment-grasstouchless",
            _ => "lobby-state-playtime-comment-selfdestructive",
        };
    }
}
