using Content.Shared._MalinovStation.Surgery;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._MalinovStation.Surgery;

[UsedImplicitly]
public sealed class SurgeryBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SurgeryWindow? _window;

    public SurgeryBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SurgeryWindow>();
        _window.OnStepChosen += (surgery, step) =>
            SendMessage(new SurgeryStepChosenBuiMsg { Surgery = surgery, Step = step });
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SurgeryBuiState st)
            _window?.Populate(st);
    }
}
