using System.Numerics;
using Content.Shared._MalinovStation.Lobby;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Timing;

namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// The lobby board: <see cref="MalinovLobbyLayouts.BoardColumns"/> columns of cells across the whole width, from the left
/// edge of the screen to the right one, each tile on the cells of its <see cref="MalinovLobbyTileControl.TilePosition"/>
/// and <see cref="MalinovLobbyTileControl.TileSize"/>.
/// Rows are <see cref="RowHeight"/> tall and grow only when a tile's content needs more; they never stretch
/// to fill the screen, so empty cells and the space below the tiles show the lobby background.
/// </summary>
/// <remarks>
/// Only <see cref="MalinovLobbyTileControl"/> children are laid out, and hidden ones leave their cells empty.
/// While <see cref="Animated"/>, a tile whose place changes slides there over <see cref="SlideSeconds"/>;
/// a tile that was not shown before appears in its place at once.
/// </remarks>
public sealed class MalinovLobbyTileGrid : Container
{
    public const float MinCellWidth = 100f;
    public const float RowHeight = 100f;
    public const float Gap = 8f;

    /// <summary>
    /// The board is never narrower than this; a narrower window scrolls sideways.
    /// </summary>
    public const float MinBoardWidth =
        MalinovLobbyLayouts.BoardColumns * MinCellWidth + (MalinovLobbyLayouts.BoardColumns - 1) * Gap;

    public const float SlideSeconds = 0.2f;

    /// <summary>
    /// Empty rows shown below the tiles while <see cref="ShowCells"/> is set, so tiles can be dropped there.
    /// </summary>
    public const int EditingRows = 2;

    public const string StyleClassDropPreview = "MalinovLobbyDropPreview";
    public const string StylePropertyCellColor = "cell-color";

    // Shows when the stylesheet has no cell color.
    private static readonly Color FallbackCellColor = Color.White.WithAlpha(0.15f);

    // Reused between layout passes to avoid allocations on every resize.
    private readonly List<Control> _laidOut = new();
    private readonly List<MalinovLobbyTilePlacement> _placements = new();
    private readonly List<float> _contentHeights = new();
    private readonly List<float> _rowHeights = new();
    private readonly Dictionary<Control, TileSlide> _slides = new();
    private readonly List<Control> _gone = new();

    private readonly PanelContainer _dropPreviewPanel;
    private MalinovLobbyTilePlacement? _dropPreview;
    private bool _showCells;
    private int _rows;
    private float _cellWidth = MinCellWidth;

    private float _measuredWidth;
    private float _arrangedWidth;

    /// <summary>
    /// Whether tiles slide to new places instead of jumping there.
    /// </summary>
    public bool Animated { get; set; }

    /// <summary>
    /// Whether every cell is outlined, empty ones included, with <see cref="EditingRows"/> more rows below the tiles.
    /// </summary>
    public bool ShowCells
    {
        get => _showCells;
        set
        {
            if (_showCells == value)
                return;

            _showCells = value;
            InvalidateMeasure();
        }
    }

    /// <summary>
    /// Cells outlined over the tiles to show where a tile would go, if any.
    /// </summary>
    public MalinovLobbyTilePlacement? DropPreview
    {
        get => _dropPreview;
        set
        {
            if (_dropPreview == value)
                return;

            _dropPreview = value;
            _dropPreviewPanel.Visible = value != null;
            InvalidateMeasure();
        }
    }

    public MalinovLobbyTileGrid()
    {
        MinWidth = MinBoardWidth;

        _dropPreviewPanel = new PanelContainer
        {
            Visible = false,
            MouseFilter = MouseFilterMode.Ignore,
        };
        _dropPreviewPanel.AddStyleClass(StyleClassDropPreview);
        AddChild(_dropPreviewPanel);
    }

    /// <summary>
    /// The cell under <paramref name="position"/>, relative to the grid; cells continue below the tiles.
    /// </summary>
    public Vector2i GetCellAt(Vector2 position)
    {
        return MalinovLobbyTileLayout.GetCell(
            position,
            _cellWidth,
            _rowHeights,
            RowHeight,
            Gap,
            MalinovLobbyLayouts.BoardColumns,
            MalinovLobbyLayouts.MaxRows);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        // A scroll container that scrolls sideways gives no width to measure for. The width the grid was last laid out
        // at is the best guess then; the arrange pass asks for another measure if it turns out different.
        var unboundedWidth = !float.IsFinite(availableSize.X);
        var width = unboundedWidth ? _arrangedWidth : availableSize.X;
        width = Math.Max(width, MinBoardWidth);
        _measuredWidth = width;

        Layout();
        var cellWidth = MalinovLobbyTileLayout.GetCellWidth(width, MalinovLobbyLayouts.BoardColumns, Gap);

        // Natural height of every tile at its width; rows grow to fit them.
        _contentHeights.Clear();
        for (var i = 0; i < _laidOut.Count; i++)
        {
            var tileWidth = MalinovLobbyTileLayout.GetSpanWidth(_placements[i].Width, cellWidth, Gap);
            _laidOut[i].Measure(new Vector2(tileWidth, float.PositiveInfinity));
            _contentHeights.Add(_laidOut[i].DesiredSize.Y);
        }

        MalinovLobbyTileLayout.FitRowHeights(_placements, _contentHeights, _rows, RowHeight, Gap, _rowHeights);

        // The parent may arrange the grid at the same size as before, which would keep the old rows.
        InvalidateArrange();

        var height = _rows == 0 ? 0f : MalinovLobbyTileLayout.GetSpanHeight(_rowHeights, 0, _rows, Gap);
        // A sideways scrolling container lays the grid out at least as wide as it says, so it only asks for its minimum.
        return new Vector2(unboundedWidth ? MinBoardWidth : width, height);
    }

    protected override Vector2 ArrangeOverride(Vector2 finalSize)
    {
        _arrangedWidth = finalSize.X;
        if (!MathHelper.CloseTo(finalSize.X, _measuredWidth))
            InvalidateMeasure();

        Layout();
        _cellWidth = MalinovLobbyTileLayout.GetCellWidth(finalSize.X, MalinovLobbyLayouts.BoardColumns, Gap);

        // Heights come from the measure pass instead of measuring again here: re-measuring during arrange
        // would invalidate our own measure and relayout every frame. Adding, removing, hiding or moving a tile
        // invalidates the measure, so both passes see the same tiles in the same places.
        MalinovLobbyTileLayout.FitRowHeights(_placements, _contentHeights, _rows, RowHeight, Gap, _rowHeights);

        for (var i = 0; i < _laidOut.Count; i++)
        {
            var target = MalinovLobbyTileLayout.GetRect(_placements[i], _cellWidth, _rowHeights, Gap);
            _laidOut[i].Arrange(GetShownRect(_laidOut[i], target));
        }

        if (_dropPreview is { } preview)
            _dropPreviewPanel.Arrange(MalinovLobbyTileLayout.GetRect(preview, _cellWidth, _rowHeights, Gap));

        ForgetSlidesOfGoneTiles();
        return finalSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        base.Draw(handle);

        if (!_showCells)
            return;

        if (!TryGetStyleProperty<Color>(StylePropertyCellColor, out var color))
            color = FallbackCellColor;

        // Drawing works in pixels, the layout in interface units.
        var top = 0f;
        for (var row = 0; row < _rows && row < _rowHeights.Count; row++)
        {
            for (var column = 0; column < MalinovLobbyLayouts.BoardColumns; column++)
            {
                var left = column * (_cellWidth + Gap);
                var cell = UIBox2.FromDimensions(new Vector2(left, top), new Vector2(_cellWidth, _rowHeights[row]));
                handle.DrawRect(cell.Scale(UIScale), color, filled: false);
            }

            top += _rowHeights[row] + Gap;
        }
    }

    protected override void FrameUpdate(FrameEventArgs args)
    {
        base.FrameUpdate(args);

        var sliding = false;
        foreach (var slide in _slides.Values)
        {
            if (slide.Elapsed >= SlideSeconds)
                continue;

            slide.Elapsed = Math.Min(slide.Elapsed + args.DeltaSeconds, SlideSeconds);
            sliding = true;
        }

        // The places in between come from the arrange pass.
        if (sliding)
            InvalidateArrange();
    }

    protected override void ChildAdded(Control newChild)
    {
        base.ChildAdded(newChild);

        // The preview is drawn over the tiles, so it stays the last child.
        if (newChild != _dropPreviewPanel && _dropPreviewPanel.Parent == this)
            _dropPreviewPanel.SetPositionLast();
    }

    /// <summary>
    /// Where <paramref name="child"/> is shown on its way to <paramref name="target"/>; starts a slide when the target changed.
    /// </summary>
    private UIBox2 GetShownRect(Control child, UIBox2 target)
    {
        if (!_slides.TryGetValue(child, out var slide))
        {
            // Nothing to slide from: the tile was not shown before.
            _slides[child] = new TileSlide { Shown = target, Target = target, Elapsed = SlideSeconds };
            return target;
        }

        if (slide.Target != target)
        {
            slide.From = slide.Shown;
            slide.Target = target;
            slide.Elapsed = Animated ? 0f : SlideSeconds;
        }

        slide.Shown = MalinovLobbyTileLayout.Slide(slide.From, slide.Target, slide.Elapsed / SlideSeconds);
        return slide.Shown;
    }

    /// <summary>
    /// Hidden and removed tiles lose their slide, so they appear in place when shown again.
    /// </summary>
    private void ForgetSlidesOfGoneTiles()
    {
        if (_slides.Count == _laidOut.Count)
            return;

        _gone.Clear();
        foreach (var child in _slides.Keys)
        {
            if (!_laidOut.Contains(child))
                _gone.Add(child);
        }

        foreach (var child in _gone)
        {
            _slides.Remove(child);
        }
    }

    /// <summary>
    /// Collects the shown tiles and their cells, and counts the rows the board shows.
    /// </summary>
    private void Layout()
    {
        _laidOut.Clear();
        _placements.Clear();
        _rows = 0;

        foreach (var child in Children)
        {
            if (!child.Visible || child is not MalinovLobbyTileControl tile)
                continue;

            var placement = MalinovLobbyLayouts.Fit(
                new MalinovLobbyTilePlacement(tile.TilePosition, tile.TileSize),
                Vector2i.One,
                new Vector2i(MalinovLobbyLayouts.BoardColumns, MalinovLobbyLayouts.MaxRows));

            _laidOut.Add(child);
            _placements.Add(placement);
            _rows = Math.Max(_rows, placement.Bottom);
        }

        if (_showCells)
            _rows += EditingRows;

        if (_dropPreview is { } preview)
            _rows = Math.Max(_rows, preview.Bottom);
    }

    private sealed class TileSlide
    {
        public UIBox2 From;
        public UIBox2 Target;
        public UIBox2 Shown;
        public float Elapsed;
    }
}
