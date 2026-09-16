using Content.Client.Light;
using Robust.Client.Graphics;

#pragma warning disable IDE0130
namespace Content.Client.Light.EntitySystems;

public sealed partial class PlanetLightSystem
{
    private void SetAmbientOcclusion(bool enabled)
    {
        AmbientOcclusion = enabled;
    }

    private void RemoveLightOverlay<T>() where T : Overlay
    {
        if (!_overlayMan.TryGetOverlay<T>(out var overlay))
            return;

        _overlayMan.RemoveOverlay(overlay);
        overlay.Dispose();
    }

    private void ShutdownLightOverlays()
    {
        RemoveLightOverlay<BeforeLightTargetOverlay>();
        RemoveLightOverlay<RoofOverlay>();
        RemoveLightOverlay<TileEmissionOverlay>();
        RemoveLightOverlay<LightBlurOverlay>();
        RemoveLightOverlay<SunShadowOverlay>();
        RemoveLightOverlay<AfterLightTargetOverlay>();
        RemoveLightOverlay<AmbientOcclusionOverlay>();
        _ambientOcclusion = false;
    }
}
