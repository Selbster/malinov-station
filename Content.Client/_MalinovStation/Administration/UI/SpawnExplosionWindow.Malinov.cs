using Robust.Shared.GameObjects;
using Robust.Shared.Map;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Content.Client.Administration.UI.SpawnExplosion;

public sealed partial class SpawnExplosionWindow
{
    private void MalinovSetLocation()
    {
        _pausePreview = true;
        try
        {
            UpdateMapOptions();

            // Nullspace is not a selectable map. Keep the first available map and the existing
            // coordinates when there is no attached entity on one of the listed maps.
            if (_entMan.TryGetComponent(_playerManager.LocalEntity, out TransformComponent? transform)
                && MapOptions.TrySelect(_mapData.IndexOf(transform.MapID)))
            {
                (MapX.Value, MapY.Value) = _transform.GetMapCoordinates(_playerManager.LocalEntity!.Value, xform: transform).Position;
            }
        }
        finally
        {
            _pausePreview = false;
        }

        UpdatePreview();
    }

    /// <summary>
    /// Revalidate at the point of use: a selected map can disappear without rebuilding its popup.
    /// </summary>
    private bool MalinovUpdateSelection(out MapId mapId, out string explosionType)
    {
        mapId = default;
        explosionType = string.Empty;
        var valid = MapOptions.SelectedId >= 0 && MapOptions.SelectedId < _mapData.Count
                    && ExplosionOption.SelectedId >= 0 && ExplosionOption.SelectedId < _explosionTypes.Count;
        if (valid)
        {
            mapId = _mapData[MapOptions.SelectedId];
            explosionType = _explosionTypes[ExplosionOption.SelectedId];
            valid = _mapSystem.MapExists(mapId);
        }

        Spawn.Disabled = !valid;
        Preview.Disabled = !valid;
        return valid;
    }
}
