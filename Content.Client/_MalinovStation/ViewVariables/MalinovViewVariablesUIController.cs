using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controllers;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.ViewVariables;

namespace Content.Client._MalinovStation.ViewVariables;

/// <summary>
/// Registers content VV editors and guards the engine's VV layout and component removal controls.
/// </summary>
public sealed partial class MalinovViewVariablesUIController : UIController
{
    [Dependency] private IViewVariableControlFactory _controlFactory = default!;
    private readonly Dictionary<Control, PendingTabWatcher> _pendingWindows = new();

    public override void Initialize()
    {
        base.Initialize();
        _controlFactory.RegisterWithCondition(type => type.IsEnum, _ => new MalinovEnumPropEditor());
        UIManager.WindowRoot.OnChildAdded += OnWindowAdded;
        UIManager.WindowRoot.OnChildRemoved += OnWindowRemoved;
    }

    private void OnWindowAdded(Control control)
    {
        // Remote VV can finish populating this window after it has already opened.
        if (control is not DefaultWindow window || window.Title != Loc.GetString("view-variables"))
            return;

        var watcher = new PendingTabWatcher();
        _pendingWindows.Add(window, watcher);
        watcher.Watch(window.Contents);
    }

    private void OnWindowRemoved(Control control)
    {
        // Window removal also covers Dispose and Orphan, which do not raise OnClose.
        if (_pendingWindows.Remove(control, out var watcher))
            watcher.Stop();
    }

    private sealed class PendingTabWatcher
    {
        private readonly HashSet<Control> _controls = new();
        private MalinovViewVariablesComponentGuard? _componentGuard;
        private bool _stopped;

        public void Watch(Control control)
        {
            if (_stopped)
                return;

            if (control is MalinovViewVariablesTabs wrapper)
            {
                FinishDiscovery();
                _componentGuard = new MalinovViewVariablesComponentGuard((TabContainer) wrapper.GetChild(0));
                return;
            }

            // Entity VV places its tabs in a VBox inside the window's ScrollContainer.
            if (control is TabContainer tabs && tabs.Parent is { Parent: ScrollContainer } parent)
            {
                FinishDiscovery();
                _componentGuard = new MalinovViewVariablesComponentGuard(tabs);
                var index = tabs.GetPositionInParent();
                parent.RemoveChild(tabs);
                var tabWrapper = new MalinovViewVariablesTabs(tabs);
                parent.AddChild(tabWrapper);
                tabWrapper.SetPositionInParent(index);
                return;
            }

            if (!_controls.Add(control))
                return;

            control.OnChildAdded += Watch;
            for (var i = 0; i < control.ChildCount && !_stopped; i++)
                Watch(control.GetChild(i));
        }

        public void Stop()
        {
            FinishDiscovery();
            _componentGuard?.Dispose();
            _componentGuard = null;
        }

        private void FinishDiscovery()
        {
            _stopped = true;
            foreach (var control in _controls)
                control.OnChildAdded -= Watch;
            _controls.Clear();
        }
    }
}
