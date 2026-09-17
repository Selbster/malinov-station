using Robust.Shared.Map;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Content.Client.Administration.UI.Tabs.AdminbusTab;

public sealed partial class LoadBlueprintsWindow
{
    private void MalinovBindEvents()
    {
        MapOptions.OnItemSelected += OnOptionSelect;
        RotationSpin.ValueChanged += OnRotate;
        SubmitButton.OnPressed += OnSubmitButtonPressed;
        TeleportButton.OnPressed += OnTeleportButtonPressed;
        ResetButton.OnPressed += OnResetButtonPressed;
    }

    private void MalinovRefreshMaps()
    {
        var selectedId = MapOptions.SelectedId;
        MapOptions.Clear();
        foreach (var mapId in _entityManager.System<SharedMapSystem>().GetAllMapIds())
        {
            MapOptions.AddItem(mapId.ToString(), (int) mapId);
        }
        MapOptions.TrySelectId(selectedId);

        var empty = MapOptions.ItemCount == 0;
        MapOptions.Disabled = empty;
        SubmitButton.Disabled = empty;
        TeleportButton.Disabled = empty;
    }

    private bool MalinovCanUseSelectedMap()
    {
        if (MapOptions.ItemCount > 0 &&
            _entityManager.System<SharedMapSystem>().MapExists(new MapId(MapOptions.SelectedId)))
        {
            return true;
        }

        // Refresh the list, but do not execute the command on a different map instead.
        MalinovRefreshMaps();
        return false;
    }
}
