using Content.Server.Station.Components;
using Content.Shared._MalinovStation.Lobby;
using Content.Shared.GameTicking;
using Robust.Shared.Player;

// ReSharper disable once CheckNamespace
namespace Content.Server.GameTicking;

public sealed partial class GameTicker
{
    /*
     * Malinov part: structured round information for the tile lobby (MalinovLobbyInfoEvent),
     * sent alongside the lobby info text and when the ready or in-game counts change on their own.
     */

    /// <summary>
    /// Sends <see cref="MalinovLobbyInfoEvent"/> to every connected player, like <see cref="UpdateInfoText"/>.
    /// </summary>
    public void MalinovSendLobbyInfo()
    {
        RaiseNetworkEvent(MalinovGetLobbyInfo(), Filter.Empty().AddPlayers(_playerManager.NetworkedSessions));
    }

    /// <summary>
    /// Called when a player joins the game, to update the in-game count.
    /// </summary>
    /// <remarks>
    /// At round start every ready player joins in a row before the run level becomes
    /// <see cref="GameRunLevel.InRound"/>; <see cref="UpdateInfoText"/> follows once after that,
    /// so only late joins are sent here and round start costs one broadcast, not one per player.
    /// </remarks>
    private void MalinovOnPlayerJoinedGame()
    {
        if (RunLevel == GameRunLevel.InRound)
            MalinovSendLobbyInfo();
    }

    private MalinovLobbyInfoEvent MalinovGetLobbyInfo()
    {
        var ready = 0;
        var inGame = 0;
        foreach (var session in _playerManager.Sessions)
        {
            if (!_playerGameStatuses.TryGetValue(session.UserId, out var status))
                continue;

            if (status == PlayerGameStatus.ReadyToPlay)
                ready++;
            else if (status == PlayerGameStatus.JoinedGame)
                inGame++;
        }

        // Same choice as the lobby info text: no mode without a preset, and secret modes show their decoy.
        var preset = CurrentPreset ?? Preset;
        var shownPreset = preset == null ? null : Decoy ?? preset;

        return new MalinovLobbyInfoEvent(
            RoundId,
            _playerManager.PlayerCount,
            ready,
            inGame,
            MalinovGetMapNames(),
            shownPreset?.ModeTitle,
            shownPreset?.Description);
    }

    /// <summary>
    /// Station names once the round has stations, otherwise the selected map, as the lobby info text shows them.
    /// </summary>
    private string[] MalinovGetMapNames()
    {
        var names = new List<string>();
        var query = EntityQueryEnumerator<StationJobsComponent, StationSpawningComponent, MetaDataComponent>();
        while (query.MoveNext(out _, out _, out var meta))
        {
            names.Add(meta.EntityName);
        }

        if (names.Count == 0 && _gameMapManager.GetSelectedMap()?.MapName is { } mapName)
            names.Add(mapName);

        return names.ToArray();
    }
}
