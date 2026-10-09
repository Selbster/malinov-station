using System.Diagnostics.CodeAnalysis;
using Content.Client.Interaction;
using Content.Shared._MalinovStation.Lobby;
using Robust.Shared.Prototypes;

namespace Content.Client._MalinovStation.Lobby;

public sealed partial class MalinovLobbyTileUIController
{
    /*
     * Editing part: the player drags tiles onto other cells of the board, resizes them by any edge or corner,
     * hides and shows them, or resets the layout. While a tile is dragged, an outline shows where it would land
     * and the tiles that would make room are highlighted. Every change goes through the layout system to the server,
     * which keeps it.
     */
    private readonly DragDropHelper<MalinovLobbyTileControl> _drag;
    private readonly DragDropHelper<MalinovLobbyTileControl> _resize;
    // Tiles highlighted as making room for the dragged one.
    private readonly List<MalinovLobbyTileControl> _inTheWay = new();
    private readonly List<ProtoId<MalinovLobbyTilePrototype>> _inTheWayIds = new();
    private bool _editing;
    // Column of the dragged tile the player took it by, so the tile keeps its place under the pointer.
    private int _grabColumn;
    // Edges of the resized tile that follow the pointer.
    private MalinovLobbyTileEdges _resizeEdges;
    // Where the dragged or resized tile would land if released now.
    private MalinovLobbyTilePlacement? _landing;

    /// <summary>
    /// Whether the player edits the lobby layout.
    /// </summary>
    public bool IsEditing => _editing;

    private void OnTileDragPressed(MalinovLobbyTileControl tile)
    {
        if (!_editing || tile.TileId is not { } id || !_placements.TryGetValue(id, out var placement))
            return;

        _grabColumn = Math.Clamp(GetCellUnderMouse().X - placement.Column, 0, placement.Width - 1);
        _drag.MouseDown(tile);
    }

    private void OnTileDragReleased(MalinovLobbyTileControl tile)
    {
        _drag.EndDrag();
    }

    private void OnTileResizePressed(MalinovLobbyTileControl tile, MalinovLobbyTileEdges edges)
    {
        if (!_editing)
            return;

        _resizeEdges = edges;
        _resize.MouseDown(tile);
    }

    private void OnTileResizeReleased(MalinovLobbyTileControl tile)
    {
        _resize.EndDrag();
    }

    private void OnTileHidePressed(MalinovLobbyTileControl tile)
    {
        if (tile.TileId is { } id)
            TrySetTileHidden(id, !tile.UserHidden);
    }

    private bool OnBeginDrag()
    {
        if (!_editing || _drag.Dragged is not { } tile)
            return false;

        tile.Dragged = true;
        return true;
    }

    private bool OnContinueDrag(float frameTime)
    {
        if (_drag.Dragged?.TileId is not { } id || !_placements.TryGetValue(id, out var placement))
            return false;

        if (!IsMouseOverBoard())
        {
            ClearLanding();
            return true;
        }

        var cell = GetCellUnderMouse();
        var landing = MalinovLobbyBoard.GetMoved(placement, new Vector2i(cell.X - _grabColumn, cell.Y));
        if (landing == _landing)
            return true;

        // A move that leaves no room for the other tiles would do nothing, so nothing is outlined.
        if (landing == placement || CanMoveTile(id, landing.Position))
            SetLanding(id, landing);
        else
            ClearLanding();

        return true;
    }

    /// <remarks>
    /// Also runs when the press ended before the mouse left the drag dead zone; nothing moves then.
    /// </remarks>
    private void OnEndDrag()
    {
        var landing = _landing;
        ClearLanding();

        if (_drag.Dragged is not { } dragged)
            return;

        dragged.Dragged = false;
        if (landing is { } place && dragged.TileId is { } id)
            TryMoveTile(id, place.Position);
    }

    private bool OnBeginResize()
    {
        return _editing && _resize.Dragged is { Resizable: true };
    }

    private bool OnContinueResize(float frameTime)
    {
        if (_resize.Dragged is not { TileId: { } id } tile || !_placements.TryGetValue(id, out var placement))
            return false;

        if (!IsMouseOverBoard())
        {
            ClearLanding();
            return true;
        }

        // The edges the player took follow the pointer; the others stay.
        var landing = MalinovLobbyBoard.GetResized(placement, _resizeEdges, GetCellUnderMouse(), tile.MinTileSize, tile.MaxTileSize);
        if (landing == _landing)
            return true;

        if (landing == placement || CanResizeTile(id, landing))
            SetLanding(id, landing);
        else
            ClearLanding();

        return true;
    }

    private void OnEndResize()
    {
        var landing = _landing;
        ClearLanding();

        if (landing is { } place && _resize.Dragged?.TileId is { } id)
            TryResizeTile(id, place);
    }

    public bool TrySetEditing(bool editing)
    {
        if (!CanSetEditing())
            return false;

        SetEditing(editing);
        return true;
    }

    /// <summary>
    /// The layout is edited in the lobby screen once the layout system is at hand.
    /// </summary>
    public bool CanSetEditing()
    {
        return _lobby != null && _layouts != null;
    }

    /// <summary>
    /// Moves <paramref name="tile"/> so its top left cell is <paramref name="position"/>; see <see cref="MalinovLobbyBoard.Move"/>
    /// for the tiles in the way.
    /// </summary>
    public bool TryMoveTile(ProtoId<MalinovLobbyTilePrototype> tile, Vector2i position)
    {
        if (!CanMoveTile(tile, position, out var placements))
            return false;

        SaveLayout(placements, GetHidden());
        return true;
    }

    /// <summary>
    /// Tiles move while the layout is edited, when the move changes something and leaves room for the other tiles.
    /// </summary>
    public bool CanMoveTile(ProtoId<MalinovLobbyTilePrototype> tile, Vector2i position)
    {
        return CanMoveTile(tile, position, out _);
    }

    /// <summary>
    /// Resizes <paramref name="tile"/> to <paramref name="size"/> from its top left corner, within its limits;
    /// the tiles in the way go down.
    /// </summary>
    public bool TryResizeTile(ProtoId<MalinovLobbyTilePrototype> tile, Vector2i size)
    {
        if (!_placements.TryGetValue(tile, out var placement) || !_tilesById.TryGetValue(tile, out var control))
            return false;

        return TryResizeTile(tile, MalinovLobbyBoard.GetResized(placement, size, control.MinTileSize, control.MaxTileSize));
    }

    /// <summary>
    /// Gives <paramref name="tile"/> the cells of <paramref name="area"/>, fitted to its limits; the tiles in the way go down.
    /// </summary>
    public bool TryResizeTile(ProtoId<MalinovLobbyTilePrototype> tile, MalinovLobbyTilePlacement area)
    {
        if (!CanResizeTile(tile, area, out var placements))
            return false;

        SaveLayout(placements, GetHidden());
        return true;
    }

    /// <summary>
    /// Resizable tiles change their cells while the layout is edited, when the cells change and the other tiles fit.
    /// </summary>
    public bool CanResizeTile(ProtoId<MalinovLobbyTilePrototype> tile, MalinovLobbyTilePlacement area)
    {
        return CanResizeTile(tile, area, out _);
    }

    public bool TrySetTileHidden(ProtoId<MalinovLobbyTilePrototype> tile, bool hidden)
    {
        if (!CanSetTileHidden(tile, hidden))
            return false;

        var hiddenTiles = GetHidden();
        hiddenTiles.Remove(tile);
        if (hidden)
            hiddenTiles.Add(tile);

        SaveLayout(_placements, hiddenTiles);
        return true;
    }

    /// <summary>
    /// While the layout is edited, a tile can be shown again, and hidden unless the lobby cannot work without it.
    /// </summary>
    public bool CanSetTileHidden(ProtoId<MalinovLobbyTilePrototype> tile, bool hidden)
    {
        return _editing && _tilesById.TryGetValue(tile, out var control) && (!hidden || control.Hideable);
    }

    /// <summary>
    /// Brings back the default layout, here and for the player's account.
    /// </summary>
    public bool TryResetLayout()
    {
        if (!CanResetLayout())
            return false;

        _layouts!.SetLayout(new MalinovLobbyLayout());
        return true;
    }

    /// <summary>
    /// The layout is reset while it is edited.
    /// </summary>
    public bool CanResetLayout()
    {
        return _editing && _layouts != null;
    }

    private bool CanMoveTile(
        ProtoId<MalinovLobbyTilePrototype> tile,
        Vector2i position,
        [NotNullWhen(true)] out Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>? placements)
    {
        placements = null;
        if (!_editing)
            return false;

        if (!MalinovLobbyBoard.Move(_placements, tile, position, out var moved))
            return false;

        placements = moved;
        return true;
    }

    private bool CanResizeTile(
        ProtoId<MalinovLobbyTilePrototype> tile,
        MalinovLobbyTilePlacement area,
        [NotNullWhen(true)] out Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>? placements)
    {
        placements = null;
        if (!_editing || !_tilesById.TryGetValue(tile, out var control) || !control.Resizable)
            return false;

        if (!MalinovLobbyBoard.Resize(_placements, tile, area, control.MinTileSize, control.MaxTileSize, out var resized))
            return false;

        placements = resized;
        return true;
    }

    private void SetEditing(bool editing)
    {
        if (_editing == editing)
            return;

        _editing = editing;
        if (!editing)
        {
            _drag.EndDrag();
            _resize.EndDrag();
        }

        if (_lobby != null)
        {
            _lobby.CustomizeButton.Text = _loc.GetString(editing
                ? "malinov-lobby-customize-done-button"
                : "malinov-lobby-customize-button");
            _lobby.ResetLayoutButton.Visible = editing;
            _lobby.TileGrid.ShowCells = editing;
        }

        UpdateTileVisibility();
    }

    /// <summary>
    /// Keeps the new layout; the layout system then reports the change, which puts the tiles in place.
    /// </summary>
    private void SaveLayout(
        IReadOnlyDictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> placements,
        List<ProtoId<MalinovLobbyTilePrototype>> hidden)
    {
        _layouts?.SetLayout(MalinovLobbyLayouts.Create(placements, hidden, _defaultPlacements));
    }

    private List<ProtoId<MalinovLobbyTilePrototype>> GetHidden()
    {
        var hidden = new List<ProtoId<MalinovLobbyTilePrototype>>();
        foreach (var tile in _tiles)
        {
            if (tile.UserHidden)
                hidden.Add(tile.TileId!.Value);
        }

        return hidden;
    }

    private bool IsMouseOverBoard()
    {
        if (_lobby == null)
            return false;

        var grid = _lobby.TileGrid;
        return UIBox2.FromDimensions(grid.GlobalPosition, grid.Size).Contains(UIManager.MousePositionScaled.Position);
    }

    private Vector2i GetCellUnderMouse()
    {
        if (_lobby == null)
            return Vector2i.Zero;

        var grid = _lobby.TileGrid;
        return grid.GetCellAt(UIManager.MousePositionScaled.Position - grid.GlobalPosition);
    }

    /// <summary>
    /// Outlines <paramref name="landing"/> on the board and highlights the tiles that would make room for <paramref name="tile"/>.
    /// </summary>
    private void SetLanding(ProtoId<MalinovLobbyTilePrototype> tile, MalinovLobbyTilePlacement landing)
    {
        ClearLanding();
        _landing = landing;

        if (_lobby != null)
            _lobby.TileGrid.DropPreview = landing;

        MalinovLobbyBoard.GetInTheWay(_placements, tile, landing, _inTheWayIds);
        foreach (var id in _inTheWayIds)
        {
            var control = _tilesById[id];
            control.DropTarget = true;
            _inTheWay.Add(control);
        }
    }

    private void ClearLanding()
    {
        _landing = null;

        if (_lobby != null)
            _lobby.TileGrid.DropPreview = null;

        foreach (var control in _inTheWay)
        {
            control.DropTarget = false;
        }

        _inTheWay.Clear();
    }
}
