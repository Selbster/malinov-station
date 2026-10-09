using Content.Client.Audio;
using Content.Client.GameTicking.Managers;
using Content.Client.Playtime;
using Content.Shared.CCVar;
using Content.Shared.GameTicking.Prototypes;
using Robust.Client;
using Robust.Client.ResourceManagement;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client._MalinovStation.Lobby;

public sealed partial class MalinovLobbyTileUIController
{
    /*
     * Lobby data part: follows the game ticker, the server's lobby info, the player's layout, lobby music
     * and background, and pushes a fresh MalinovLobbyTileContext to the tiles.
     */
    [Dependency] private IBaseClient _client = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IResourceCache _resourceCache = default!;
    [Dependency] private ClientsidePlaytimeTrackingManager _playtimeTracking = default!;

    private ClientGameTicker? _gameTicker;
    private MalinovLobbyInfoSystem? _lobbyInfo;
    private MalinovLobbyLayoutSystem? _layouts;
    private ContentAudioSystem? _contentAudio;
    private string? _serverTitle;
    private MalinovLobbySong? _song;
    private LobbyBackgroundPrototype? _background;
    private long _refreshedSecond = -1;

    /// <summary>
    /// Lobby data the tiles show now.
    /// </summary>
    public MalinovLobbyTileContext Context { get; private set; }

    private void SubscribeLobbyData()
    {
        _gameTicker = EntityManager.System<ClientGameTicker>();
        _lobbyInfo = EntityManager.System<MalinovLobbyInfoSystem>();
        _layouts = EntityManager.System<MalinovLobbyLayoutSystem>();
        _contentAudio = EntityManager.System<ContentAudioSystem>();
        _serverTitle = GetServerTitle();

        _gameTicker.LobbyStatusUpdated += OnLobbyStatusUpdated;
        _gameTicker.LobbyLateJoinStatusUpdated += OnLobbyDataChanged;
        _lobbyInfo.InfoUpdated += OnLobbyDataChanged;
        _layouts.LayoutUpdated += OnLayoutUpdated;
        // Subscribing also reports the soundtrack that already plays, if any.
        _contentAudio.LobbySoundtrackChanged += OnLobbySoundtrackChanged;

        UpdateBackground();
        // The tiles were built before the layout system was at hand.
        ApplyLayout();
    }

    private void UnsubscribeLobbyData()
    {
        if (_gameTicker != null)
        {
            _gameTicker.LobbyStatusUpdated -= OnLobbyStatusUpdated;
            _gameTicker.LobbyLateJoinStatusUpdated -= OnLobbyDataChanged;
        }

        if (_lobbyInfo != null)
            _lobbyInfo.InfoUpdated -= OnLobbyDataChanged;

        if (_layouts != null)
            _layouts.LayoutUpdated -= OnLayoutUpdated;

        if (_contentAudio != null)
            _contentAudio.LobbySoundtrackChanged -= OnLobbySoundtrackChanged;

        _gameTicker = null;
        _lobbyInfo = null;
        _layouts = null;
        _contentAudio = null;
        _song = null;
        _background = null;
        _refreshedSecond = -1;
    }

    private void OnLobbyDataChanged()
    {
        RefreshTiles();
    }

    private void OnLobbyStatusUpdated()
    {
        UpdateBackground();
        RefreshTiles();
    }

    private void OnLayoutUpdated()
    {
        ApplyLayout();
    }

    private void OnLobbySoundtrackChanged(LobbySoundtrackChangedEvent args)
    {
        _song = GetSong(args.SoundtrackFilename);
        RefreshTiles();
    }

    /// <summary>
    /// Rebuilds <see cref="Context"/> from the current lobby data and shows it on the tiles.
    /// </summary>
    private void RefreshTiles()
    {
        if (_lobby == null || _gameTicker == null)
            return;

        _refreshedSecond = (long) _timing.CurTime.TotalSeconds;

        var context = new MalinovLobbyTileContext
        {
            IsGameStarted = _gameTicker.IsGameStarted,
            Paused = _gameTicker.Paused,
            AreWeReady = _gameTicker.AreWeReady,
            DisallowedLateJoin = _gameTicker.DisallowedLateJoin,
            StartTime = _gameTicker.StartTime,
            RoundStartTime = _gameTicker.RoundStartTimeSpan,
            CurTime = _timing.CurTime,
            ServerTitle = _serverTitle,
            LobbyInfo = _lobbyInfo?.Info,
            PlaytimeMinutesToday = _playtimeTracking.PlaytimeMinutesToday,
            Song = _song,
            Background = _background,
        };
        Context = context;

        ApplyPhase(context.Phase);
        foreach (var tile in _tiles)
        {
            tile.Refresh(context);
        }
    }

    public override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        if (_lobby == null || _gameTicker == null)
            return;

        if (_editing)
        {
            _drag.Update(args.DeltaSeconds);
            _resize.Update(args.DeltaSeconds);
        }

        // Countdowns and pauses change without events, and the texts never change faster than once per second.
        if ((long) _timing.CurTime.TotalSeconds != _refreshedSecond)
            RefreshTiles();
    }

    private void UpdateBackground()
    {
        if (_lobby == null || _gameTicker == null)
            return;

        if (_prototype.TryIndex(_gameTicker.LobbyBackground, out var background))
        {
            _background = background;
            _lobby.Background.SetTexture(_resourceCache.GetResource<TextureResource>(background.Background), MotionAllowed);
            return;
        }

        _background = null;
        _lobby.Background.SetTexture(null, MotionAllowed);
    }

    private string GetServerTitle()
    {
        var lobbyName = _cfg.GetCVar(CCVars.ServerLobbyName);
        if (!string.IsNullOrEmpty(lobbyName))
            return lobbyName;

        return _loc.GetString("ui-lobby-title", ("serverName", _client.GameInfo?.ServerName ?? string.Empty));
    }

    private MalinovLobbySong? GetSong(string? filename)
    {
        if (filename == null || !_resourceCache.TryGetResource<AudioResource>(filename, out var resource))
            return null;

        var stream = resource.AudioStream;
        return new MalinovLobbySong(stream.Title, stream.Artist);
    }
}
