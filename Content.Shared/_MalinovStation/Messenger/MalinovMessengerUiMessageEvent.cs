using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._MalinovStation.Messenger;

[Serializable, NetSerializable]
public enum MalinovMessengerUiAction
{
    RefreshContacts,
    Send
}

/// <summary>
/// UI message sent from the client messenger fragment to the server cartridge system.
/// </summary>
[Serializable, NetSerializable]
public sealed class MalinovMessengerUiMessageEvent : CartridgeMessageEvent
{
    public readonly MalinovMessengerUiAction Action;

    /// <summary>
    ///     Selected contact name. Used by <see cref="MalinovMessengerUiAction.RefreshContacts"/>.
    /// </summary>
    public readonly string? TargetName;

    /// <summary>
    ///     Text to send. Used by <see cref="MalinovMessengerUiAction.Send"/>.
    /// </summary>
    public readonly string? Text;

    public MalinovMessengerUiMessageEvent(MalinovMessengerUiAction action, string? targetName = null, string? text = null)
    {
        Action = action;
        TargetName = targetName;
        Text = text;
    }
}
