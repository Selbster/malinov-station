using System.Numerics;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._MalinovStation.ViewVariables;

/// <summary>
/// Reserves one header row before engine VV tabs are measured and arranged.
/// The surrounding ScrollContainer supplies horizontal scrolling when the window is narrow.
/// </summary>
internal sealed class MalinovViewVariablesTabs : Container
{
    private readonly TabContainer _tabs;
    private readonly Vector2 _originalMinimum;

    public MalinovViewVariablesTabs(TabContainer tabs)
    {
        _tabs = tabs;
        _originalMinimum = tabs.MinSize;
        AddChild(tabs);
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        // Opening, changing themes and changing UI scale can all invalidate the header dimensions.
        // Read the current style before the child computes its own wrapping.
        _tabs.DoStyleUpdate();
        var scale = _tabs.UIScale;
        var font = _tabs.StylePropertyDefault("font", UserInterfaceManager.ThemeDefaults.DefaultFont);
        _tabs.TryGetStyleProperty<StyleBox>(TabContainer.StylePropertyTabStyleBox, out var active);
        _tabs.TryGetStyleProperty<StyleBox>(TabContainer.StylePropertyTabStyleBoxInactive, out var inactive);

        var width = 0f;
        var height = 0f;
        for (var i = 0; i < _tabs.ChildCount; i++)
        {
            if (!_tabs.GetTabVisible(i))
                continue;

            var titleWidth = 0;
            foreach (var rune in _tabs.GetActualTabTitle(i).EnumerateRunes())
            {
                if (font.TryGetCharMetrics(rune, scale, out var metrics))
                    titleWidth += metrics.Advance;
            }

            var titleSize = new Vector2(titleWidth, font.GetHeight(scale));
            var activeSize = active?.GetEnvelopBox(Vector2.Zero, titleSize, scale).Size ?? titleSize;
            var inactiveSize = inactive?.GetEnvelopBox(Vector2.Zero, titleSize, scale).Size ?? titleSize;
            var size = Vector2.Max(activeSize, inactiveSize);
            width += size.X;
            height = Math.Max(height, size.Y);
        }

        var panel = _tabs.PanelStyleBoxOverride;
        if (panel == null)
            _tabs.TryGetStyleProperty(TabContainer.StylePropertyPanelStyleBox, out panel);

        // Leave one physical pixel for rounding at fractional UI scales.
        var minimum = new Vector2(MathF.Ceiling((width + 1) / scale), MathF.Ceiling(height / scale));
        minimum.Y += panel?.MinimumSize.Y ?? 0;
        _tabs.MinSize = Vector2.Max(_originalMinimum, minimum);
        return base.MeasureOverride(availableSize);
    }
}
