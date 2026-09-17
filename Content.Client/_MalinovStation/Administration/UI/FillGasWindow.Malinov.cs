using System.Linq;
using Robust.Client.UserInterface.XAML;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Content.Client.Administration.UI.Tabs.AtmosTab;

public sealed partial class FillGasWindow
{
    public FillGasWindow()
    {
        RobustXamlLoader.Load(this);
        GridOptions.OnItemSelected += args => GridOptions.SelectId(args.Id);
        GasOptions.OnItemSelected += args => GasOptions.SelectId(args.Id);
        SubmitButton.OnPressed += SubmitButtonOnOnPressed;
    }

    private void MalinovResetOptions()
    {
        GridOptions.Clear();
        GasOptions.Clear();
    }

    private void MalinovRefreshSubmitState()
    {
        GridOptions.Disabled = GridOptions.ItemCount == 0;
        GasOptions.Disabled = GasOptions.ItemCount == 0;
        SubmitButton.Disabled = GridOptions.Disabled || GasOptions.Disabled;
    }

    private bool MalinovTryGetSelection(out NetEntity grid, out string gasId)
    {
        grid = default;
        gasId = string.Empty;
        if (_gridData == null || _gasData == null ||
            (uint) GridOptions.SelectedId >= (uint) _gridData.Count)
            return false;

        var gases = _gasData.ToList();
        if ((uint) GasOptions.SelectedId >= (uint) gases.Count)
            return false;

        grid = _gridData[GridOptions.SelectedId];
        gasId = gases[GasOptions.SelectedId].ID;
        return true;
    }
}
