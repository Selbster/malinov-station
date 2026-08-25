using Content.Client.UserInterface.Fragments;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.CartridgeLoader;
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

        _fragment.OnContactSelected += name =>
        {
            var message = new MalinovMessengerUiMessageEvent(MalinovMessengerUiAction.RefreshContacts, targetName: name);
            userInterface.SendPredictedMessage(new CartridgeUiMessage(message));
        };

        _fragment.OnSendMessage += (target, text) =>
        {
            var message = new MalinovMessengerUiMessageEvent(MalinovMessengerUiAction.Send, targetName: target, text: text);
            userInterface.SendPredictedMessage(new CartridgeUiMessage(message));
        };
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is not MalinovMessengerUiState messengerState)
            return;

        _fragment?.UpdateState(messengerState);
    }
}
