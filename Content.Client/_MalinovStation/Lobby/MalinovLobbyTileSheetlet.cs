using Content.Client.Stylesheets;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using static Content.Client.Stylesheets.StylesheetHelpers;

namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// Styles of the lobby tiles.
/// </summary>
[CommonSheetlet]
public sealed class MalinovLobbyTileSheetlet : Sheetlet<PalettedStylesheet>
{
    public override StyleRule[] GetRules(PalettedStylesheet sheet, object config)
    {
        var header = new StyleBoxFlat
        {
            BackgroundColor = sheet.SecondaryPalette.BackgroundDark,
            BorderColor = sheet.HighlightPalette.Element,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        // An accented tile turns its header into a bright bar with a dark title.
        var headerAccent = new StyleBoxFlat
        {
            BackgroundColor = sheet.HighlightPalette.Element,
            BorderColor = sheet.HighlightPalette.HoveredElement,
            BorderThickness = new Thickness(0, 0, 0, 1),
        };

        // Where a dragged or resized tile would land: a bright frame over the board.
        var dropPreview = new StyleBoxFlat
        {
            BackgroundColor = sheet.HighlightPalette.Element.WithAlpha(0.2f),
            BorderColor = sheet.HighlightPalette.Element,
            BorderThickness = new Thickness(2),
        };

        // The edge or corner of a resizable tile under the pointer while editing; the others stay invisible.
        var resizeHandle = new StyleBoxFlat
        {
            BackgroundColor = sheet.HighlightPalette.Element,
        };

        return
        [
            E<PanelContainer>().Class(MalinovLobbyTileGrid.StyleClassDropPreview).Panel(dropPreview),
            E<PanelContainer>()
                .Class(MalinovLobbyTileControl.StyleClassResizeHandle)
                .Class(MalinovLobbyTileControl.StyleClassResizeHandleHovered)
                .Panel(resizeHandle),
            // Outlines of the board cells while editing; faint, as they only hint where tiles may go.
            E<MalinovLobbyTileGrid>().Prop(MalinovLobbyTileGrid.StylePropertyCellColor, sheet.SecondaryPalette.Text.WithAlpha(0.2f)),
            E<PanelContainer>().Class(MalinovLobbyTileControl.StyleClassHeader).Panel(header),
            E<PanelContainer>()
                .Class(MalinovLobbyTileControl.StyleClassHeader)
                .Class(MalinovLobbyTileControl.StyleClassHeaderAccent)
                .Panel(headerAccent),
            // Two classes on the header outrank the title's own heading color.
            E<PanelContainer>()
                .Class(MalinovLobbyTileControl.StyleClassHeader)
                .Class(MalinovLobbyTileControl.StyleClassHeaderAccent)
                .ParentOf(E<Label>())
                .FontColor(sheet.SecondaryPalette.BackgroundDark),
        ];
    }
}
