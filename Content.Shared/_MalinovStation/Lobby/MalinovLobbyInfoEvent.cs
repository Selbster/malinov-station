using Robust.Shared.Serialization;

namespace Content.Shared._MalinovStation.Lobby;

/// <summary>
/// Round information for the lobby tiles. The server sends it whenever it updates the lobby info text,
/// and also when the ready or in-game counts change without that text being resent.
/// </summary>
[Serializable, NetSerializable]
public sealed class MalinovLobbyInfoEvent : EntityEventArgs
{
    public int RoundId { get; }

    /// <summary>
    /// Players connected to the server, counted like the lobby info text does.
    /// </summary>
    public int PlayerCount { get; }

    /// <summary>
    /// Connected players who are ready for the next round.
    /// </summary>
    public int ReadyCount { get; }

    /// <summary>
    /// Connected players who have joined the running round, observers included.
    /// </summary>
    public int InGameCount { get; }

    /// <summary>
    /// Names of the stations in play; before they exist, the selected map. Empty while no map is selected.
    /// </summary>
    public string[] MapNames { get; }

    /// <summary>
    /// Localization key of the game mode title. Secret modes report their decoy. <c>null</c> without a preset.
    /// </summary>
    public string? ModeTitle { get; }

    /// <summary>
    /// Localization key of the game mode description, chosen like <see cref="ModeTitle"/>.
    /// </summary>
    public string? ModeDescription { get; }

    public MalinovLobbyInfoEvent(
        int roundId,
        int playerCount,
        int readyCount,
        int inGameCount,
        string[] mapNames,
        string? modeTitle,
        string? modeDescription)
    {
        RoundId = roundId;
        PlayerCount = playerCount;
        ReadyCount = readyCount;
        InGameCount = inGameCount;
        MapNames = mapNames;
        ModeTitle = modeTitle;
        ModeDescription = modeDescription;
    }
}
