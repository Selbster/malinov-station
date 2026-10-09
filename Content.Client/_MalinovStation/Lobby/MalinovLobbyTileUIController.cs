using System.Linq;
using Content.Client._MalinovStation.Lobby.Tiles;
using Content.Client.Interaction;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Shared._MalinovStation.Lobby;
using Content.Shared.CCVar;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Shared.Prototypes;

namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// Builds the lobby screen out of tiles described by <see cref="MalinovLobbyTilePrototype"/>,
/// keeps them fed with lobby data and runs the lobby actions they ask for.
/// </summary>
/// <remarks>
/// The lobby screen is cached for the whole session, so tiles are built on every lobby entry
/// and release adopted controls back to <see cref="LobbyGui.LegacyPool"/> on exit.
/// </remarks>
public sealed partial class MalinovLobbyTileUIController : UIController,
    IOnStateEntered<LobbyState>,
    IOnStateExited<LobbyState>,
    IMalinovLobbyActions
{
    /*
     * Tile part: builds the tiles from their prototypes, puts them on the player's board
     * and shows them by round phase and the player's choice.
     */
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private ILocalizationManager _loc = default!;

    /// <summary>
    /// Widget keys used by tile prototypes, mapped to what builds each tile's content.
    /// Controls that vanilla code still reads from <see cref="LobbyGui"/> are adopted from it.
    /// </summary>
    private static readonly Dictionary<string, Func<MalinovLobbyTileUIController, LobbyGui, Control>> Widgets = new()
    {
        ["Ready"] = (controller, _) => new MalinovReadyTileWidget(controller),
        ["Players"] = (_, _) => new MalinovPlayersTileWidget(),
        ["Round"] = (_, _) => new MalinovRoundTileWidget(),
        ["Actions"] = (_, lobby) => lobby.ActionsTileContent,
        ["ServerInfo"] = (_, _) => new MalinovServerTileWidget(),
        ["Character"] = (_, lobby) => lobby.CharacterPreview,
        ["Chat"] = (_, lobby) => lobby.Chat,
        ["Credits"] = (_, _) => new MalinovCreditsTileWidget(),
        ["Changelog"] = (_, _) => new MalinovChangelogTileWidget(),
    };

    // Tiles in reading order of the board, and by prototype.
    private readonly List<MalinovLobbyTileControl> _tiles = new();
    private readonly Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTileControl> _tilesById = new();
    private readonly Dictionary<ProtoId<MalinovLobbyTilePrototype>, HashSet<MalinovLobbyPhase>> _tilePhases = new();
    // Tiles in their default order with their sizes, where they stand by default and where they stand now.
    private readonly List<MalinovLobbyBoardTile> _boardTiles = new();
    private readonly Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> _defaultPlacements = new();
    private readonly Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> _placements = new();
    private LobbyState? _lobbyState;
    private LobbyGui? _lobby;
    private MalinovLobbyPhase? _phase;

    /// <summary>
    /// Tiles of the lobby, in reading order of the board: by row, then by column.
    /// </summary>
    public IReadOnlyList<MalinovLobbyTileControl> Tiles => _tiles;

    public MalinovLobbyTileUIController()
    {
        _drag = new DragDropHelper<MalinovLobbyTileControl>(OnBeginDrag, OnContinueDrag, OnEndDrag);
        _resize = new DragDropHelper<MalinovLobbyTileControl>(OnBeginResize, OnContinueResize, OnEndResize);
    }

    public override void Initialize()
    {
        base.Initialize();
        _prototype.PrototypesReloaded += OnPrototypesReloaded;
        _cfg.OnValueChanged(CCVars.ReducedMotion, OnReducedMotionChanged);
    }

    /// <summary>
    /// Whether a tile prototype may use <paramref name="widget"/> as its widget key.
    /// </summary>
    public bool HasWidget(string widget)
    {
        return Widgets.ContainsKey(widget);
    }

    public void OnStateEntered(LobbyState state)
    {
        if (state.Lobby == null)
            return;

        _lobbyState = state;
        BuildTiles(state.Lobby);
        SubscribeLobbyData();
        SubscribeLobbyActions(state.Lobby);
        RefreshTiles();
        FadeInTiles();
    }

    public void OnStateExited(LobbyState state)
    {
        // While the lobby is still at hand, so its buttons are left as they were before editing.
        SetEditing(false);
        UnsubscribeLobbyActions();
        UnsubscribeLobbyData();
        ClearTiles();
        _lobbyState = null;
    }

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs args)
    {
        if (_lobby == null || !args.WasModified<MalinovLobbyTilePrototype>())
            return;

        BuildTiles(_lobby);
        RefreshTiles();
        FadeInTiles();
    }

    private void BuildTiles(LobbyGui lobby)
    {
        ClearTiles();
        _lobby = lobby;

        var prototypes = _prototype.EnumeratePrototypes<MalinovLobbyTilePrototype>()
            .OrderBy(proto => proto.Order)
            .ThenBy(proto => proto.ID, StringComparer.Ordinal);

        foreach (var proto in prototypes)
        {
            if (!Widgets.TryGetValue(proto.Widget, out var createContent))
            {
                Log.Error($"Lobby tile {proto.ID} uses unknown widget {proto.Widget}.");
                continue;
            }

            var tile = new MalinovLobbyTileControl
            {
                TileId = proto.ID,
                TileSize = proto.Size,
                MinTileSize = proto.SmallestSize,
                MaxTileSize = proto.LargestSize,
                Hideable = proto.Hideable,
            };
            tile.SetTitle(proto.Title is { } title ? _loc.GetString(title) : string.Empty);
            tile.DragPressed += OnTileDragPressed;
            tile.DragReleased += OnTileDragReleased;
            tile.ResizePressed += OnTileResizePressed;
            tile.ResizeReleased += OnTileResizeReleased;
            tile.HidePressed += OnTileHidePressed;

            tile.Adopt(createContent(this, lobby));
            lobby.TileGrid.AddChild(tile);
            _tiles.Add(tile);
            _tilesById[proto.ID] = tile;
            _tilePhases[proto.ID] = proto.Phases;
            _boardTiles.Add(new MalinovLobbyBoardTile(proto.ID, proto.Size, proto.SmallestSize, proto.LargestSize));
        }

        MalinovLobbyBoard.Arrange(_boardTiles, null, _defaultPlacements);
        lobby.TileGrid.Animated = MotionAllowed;
        ApplyLayout();
    }

    private void ClearTiles()
    {
        foreach (var tile in _tiles)
        {
            tile.Release();
            tile.Orphan();
        }

        _tiles.Clear();
        _tilesById.Clear();
        _tilePhases.Clear();
        _boardTiles.Clear();
        _defaultPlacements.Clear();
        _placements.Clear();
        _lobby = null;
        _phase = null;
    }

    /// <summary>
    /// Puts the tiles where the player put them on the board and marks the ones they hid.
    /// </summary>
    private void ApplyLayout()
    {
        if (_lobby == null)
            return;

        var layout = _layouts?.Layout;
        MalinovLobbyBoard.Arrange(_boardTiles, layout, _placements);

        foreach (var (id, placement) in _placements)
        {
            var tile = _tilesById[id];
            tile.TilePosition = placement.Position;
            tile.TileSize = placement.Size;
            tile.UserHidden = tile.Hideable && layout != null && layout.Hidden.Contains(id);
        }

        _tiles.Sort((a, b) => MalinovLobbyLayouts.CompareReadingOrder(_placements[a.TileId!.Value], _placements[b.TileId!.Value]));
        UpdateTileVisibility();
    }

    /// <summary>
    /// Shows only the tiles that ask for <paramref name="phase"/>; does nothing while the phase stays the same.
    /// </summary>
    private void ApplyPhase(MalinovLobbyPhase phase)
    {
        if (phase == _phase)
            return;

        _phase = phase;
        UpdateTileVisibility();
    }

    /// <summary>
    /// Shows the tiles of the current phase that the player did not hide; while editing, shows every tile.
    /// A tile that comes into sight fades in.
    /// </summary>
    private void UpdateTileVisibility()
    {
        foreach (var tile in _tiles)
        {
            tile.Editing = _editing;
            var inPhase = _phase is not { } phase || _tilePhases[tile.TileId!.Value].Contains(phase);
            var visible = _editing || inPhase && !tile.UserHidden;
            if (visible && !tile.Visible && MotionAllowed)
                tile.FadeIn(0f);

            tile.Visible = visible;
        }
    }
}
