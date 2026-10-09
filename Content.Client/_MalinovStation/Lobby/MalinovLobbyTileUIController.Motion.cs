using Content.Shared.CCVar;

namespace Content.Client._MalinovStation.Lobby;

public sealed partial class MalinovLobbyTileUIController
{
    /*
     * Motion part: tiles fade in and slide to new places and the background fades between pictures,
     * unless the player turned on reduced motion in the accessibility options.
     */

    // Tiles entering the lobby fade in one after another, this far apart.
    private const float FadeInStaggerSeconds = 0.04f;

    /// <summary>
    /// Whether the lobby may animate.
    /// </summary>
    private bool MotionAllowed => !_cfg.GetCVar(CCVars.ReducedMotion);

    private void OnReducedMotionChanged(bool reduced)
    {
        if (_lobby != null)
            _lobby.TileGrid.Animated = !reduced;
    }

    /// <summary>
    /// Fades the shown tiles in one after another, in layout order.
    /// </summary>
    private void FadeInTiles()
    {
        if (!MotionAllowed)
            return;

        var delay = 0f;
        foreach (var tile in _tiles)
        {
            if (!tile.Visible)
                continue;

            tile.FadeIn(delay);
            delay += FadeInStaggerSeconds;
        }
    }
}
