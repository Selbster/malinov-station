using Content.Client.Hands.Systems;
using Content.Shared._MalinovStation.Surgery;
using JetBrains.Annotations;
using Robust.Client.UserInterface;

namespace Content.Client._MalinovStation.Surgery;

[UsedImplicitly]
public sealed class SurgeryBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private SurgeryWindow? _window;

    private readonly HandsSystem _hands;

    public SurgeryBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _hands = EntMan.System<HandsSystem>();
    }

    protected override void Open()
    {
        base.Open();

        _window = this.CreateWindow<SurgeryWindow>();
        _window.SetPatient(Owner);
        _window.OnStepChosen += (surgery, step) =>
            SendMessage(new SurgeryStepChosenBuiMsg { Surgery = surgery, Step = step });

        _hands.OnPlayerItemAdded += OnHeldItemsChanged;
        _hands.OnPlayerItemRemoved += OnHeldItemsChanged;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is SurgeryBuiState st)
            _window?.Populate(st);
    }

    private void OnHeldItemsChanged(string handId, EntityUid item)
    {
        _window?.RefreshValidity();
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
            _window?.Dispose();

        _hands.OnPlayerItemAdded -= OnHeldItemsChanged;
        _hands.OnPlayerItemRemoved -= OnHeldItemsChanged;
    }
}
