using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;

namespace Content.Client._MalinovStation.UserInterface;

/// <summary>
/// Keeps the standard window header's close button working when a window is reused.
/// </summary>
public sealed partial class MalinovWindowCloseUIController : UIController
{
    public override void Initialize()
    {
        base.Initialize();
        UIManager.WindowRoot.OnChildRemoved += OnWindowRemoved;
    }

    private static void OnWindowRemoved(Control control)
    {
        if (control is not DefaultWindow window)
            return;

        // The pinned engine removes its header callback in DefaultWindow.ExitedTree,
        // but only installs it in the constructor. Repair it after that first removal.
        var button = FindHeaderCloseButton(window, window.Contents);
        if (button == null)
            return;

        button.OnPressed -= OnClosePressed;
        button.OnPressed += OnClosePressed;
    }

    private static TextureButton? FindHeaderCloseButton(Control control, Control contents)
    {
        // Content can contain unrelated buttons using the same style (e.g. VV component removal).
        if (control == contents)
            return null;

        if (control is TextureButton button && button.HasStyleClass(DefaultWindow.StyleClassWindowCloseButton))
            return button;

        foreach (var child in control.Children)
        {
            if (FindHeaderCloseButton(child, contents) is { } close)
                return close;
        }

        return null;
    }

    private static void OnClosePressed(BaseButton.ButtonEventArgs args)
    {
        for (Control? control = args.Button.Parent; control != null; control = control.Parent)
        {
            if (control is not DefaultWindow window)
                continue;

            if (window.IsOpen)
                window.Close();
            return;
        }
    }
}
