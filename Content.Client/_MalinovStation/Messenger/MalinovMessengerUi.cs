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
        // The cartridge loader calls Setup() on every loader-level state update, and the
        // on-screen fragment is only re-attached when its type changes. Recreating the
        // fragment here would orphan the displayed instance, so messenger state would be
        // forwarded to a hidden fragment and the chat would not open. Reuse the live one.
        if (_fragment is { Disposed: false })
            return;

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

        _fragment.OnCloseChat += () =>
        {
            var message = new MalinovMessengerUiMessageEvent(MalinovMessengerUiAction.CloseChat);
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
