using Content.Client.UserInterface.Fragments;
using Content.Shared._MalinovStation.Messenger;
using Robust.Client.UserInterface;

namespace Content.Client._MalinovStation.Messenger;

/// <summary>
/// UI fragment for the Malinov Messenger cartridge.
/// </summary>
public sealed partial class MalinovMessengerUi : UIFragment
{
    private MalinovMessengerUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new MalinovMessengerUiFragment();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not MalinovMessengerUiState)
            return;

        _fragment?.UpdateState();
    }
}
