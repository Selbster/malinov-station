using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Shared.Utility;

namespace Content.Client._MalinovStation.ViewVariables;

/// <summary>
/// Disables the local VV removal buttons for the entity's required components.
/// </summary>
internal sealed class MalinovViewVariablesComponentGuard : IDisposable
{
    // EntityManager.RemoveComponentImmediate/Deferred reject these two component types.
    // VV exposes their CLR display names in rows, but does not expose the component objects.
    private static readonly HashSet<string> ProtectedComponentNames = new()
    {
        PrettyPrint.PrintUserFacingTypeShort(typeof(MetaDataComponent), 2),
        PrettyPrint.PrintUserFacingTypeShort(typeof(TransformComponent), 2),
    };

    private readonly TabContainer _tabs;
    private readonly HashSet<Control> _pages = new();

    public MalinovViewVariablesComponentGuard(TabContainer tabs)
    {
        _tabs = tabs;
        tabs.OnChildAdded += OnPageAdded;
        tabs.OnChildRemoved += OnPageRemoved;
        foreach (var page in tabs.Children)
            OnPageAdded(page);
    }

    private void OnPageAdded(Control page)
    {
        if (!_pages.Add(page))
            return;

        // Remote VV adds a page before assigning its title, then populates its rows.
        page.OnChildAdded += OnRowAdded;
        foreach (var row in page.Children)
            OnRowAdded(row);
    }

    private void OnPageRemoved(Control page)
    {
        if (_pages.Remove(page))
            page.OnChildAdded -= OnRowAdded;
    }

    private void OnRowAdded(Control row)
    {
        if (_tabs.ChildCount < 2 || row.Parent != _tabs.GetChild(1)
            || _tabs.GetActualTabTitle(1) != Loc.GetString("view-variable-instance-entity-client-components-tab-title")
            || row is not Button { Text: { } name } || !ProtectedComponentNames.Contains(name))
            return;

        foreach (var child in row.Children)
        {
            if (child is TextureButton remove && remove.HasStyleClass(DefaultWindow.StyleClassWindowCloseButton))
                remove.Disabled = true;
        }
    }

    public void Dispose()
    {
        _tabs.OnChildAdded -= OnPageAdded;
        _tabs.OnChildRemoved -= OnPageRemoved;
        foreach (var page in _pages)
            page.OnChildAdded -= OnRowAdded;
        _pages.Clear();
    }
}
