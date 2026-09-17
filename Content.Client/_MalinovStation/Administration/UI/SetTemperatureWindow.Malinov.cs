using Robust.Client.UserInterface.XAML;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Content.Client.Administration.UI.Tabs.AtmosTab;

public sealed partial class SetTemperatureWindow
{
    public SetTemperatureWindow()
    {
        RobustXamlLoader.Load(this);
        GridOptions.OnItemSelected += eventArgs => GridOptions.SelectId(eventArgs.Id);
        SubmitButton.OnPressed += SubmitButtonOnOnPressed;
    }
}
