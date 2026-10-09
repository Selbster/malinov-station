#nullable enable
using System.Collections.Generic;
using System.Numerics;
using System.Reflection;
using Content.Client._MalinovStation.Lobby;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.Tests._MalinovStation.Lobby;

/// <summary>
/// Clicks buttons of lobby widgets the way the input system does.
/// </summary>
internal static class MalinovLobbyTestInput
{
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.NonPublic;
    private static readonly MethodInfo ButtonDown = typeof(BaseButton).GetMethod("KeyBindDown", InstanceMembers)!;
    private static readonly MethodInfo ButtonUp = typeof(BaseButton).GetMethod("KeyBindUp", InstanceMembers)!;
    private static readonly MethodInfo ControlDown = typeof(Control).GetMethod("KeyBindDown", InstanceMembers)!;
    private static readonly MethodInfo ControlUp = typeof(Control).GetMethod("KeyBindUp", InstanceMembers)!;
    private static readonly MethodInfo FrameUpdate = typeof(Control).GetMethod("FrameUpdate", InstanceMembers)!;
    private static readonly MethodInfo MouseEntered = typeof(Control).GetMethod("MouseEntered", InstanceMembers)!;
    private static readonly MethodInfo MouseExited = typeof(Control).GetMethod("MouseExited", InstanceMembers)!;

    /// <summary>
    /// Lets <paramref name="seconds"/> of frame time pass for <paramref name="control"/> alone,
    /// as the interface manager does for every control in the tree; this is what runs animations.
    /// </summary>
    public static void Frame(Control control, float seconds)
    {
        FrameUpdate.Invoke(control, new object[] { new FrameEventArgs(seconds) });
    }

    /// <summary>
    /// Moves the pointer onto <paramref name="control"/>, as the interface manager reports it.
    /// </summary>
    public static void Hover(Control control)
    {
        MouseEntered.Invoke(control, null);
    }

    /// <summary>
    /// Moves the pointer off <paramref name="control"/>.
    /// </summary>
    public static void Leave(Control control)
    {
        MouseExited.Invoke(control, null);
    }

    /// <summary>
    /// Presses the left mouse button on <paramref name="control"/>, as the input system does for the control under the pointer.
    /// </summary>
    public static void Press(Control control)
    {
        ControlDown.Invoke(control, new object[] { ClickArgs(control, BoundKeyState.Down) });
    }

    /// <summary>
    /// Releases the left mouse button over <paramref name="control"/>.
    /// </summary>
    public static void Release(Control control)
    {
        ControlUp.Invoke(control, new object[] { ClickArgs(control, BoundKeyState.Up) });
    }

    private static GUIBoundKeyEventArgs ClickArgs(Control control, BoundKeyState state)
    {
        var relative = control.Size / 2;
        var pointer = new ScreenCoordinates(control.GlobalPixelPosition + relative * control.UIScale, WindowId.Main);
        return new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, pointer, true, relative, relative * control.UIScale);
    }

    public static void Click(BaseButton button)
    {
        // Give the button a hit-testable area; the dummy theme has no button textures.
        button.MuteSounds = true;
        button.MinSize = new Vector2(32);
        button.Measure(new Vector2(32));
        button.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(32)));
        var relative = button.Size / 2;
        var pointer = new ScreenCoordinates(button.GlobalPixelPosition + relative * button.UIScale, WindowId.Main);

        // The protected input entry points keep the real disabled checks and press/release semantics.
        ButtonDown.Invoke(button, new object[]
        {
            new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Down, pointer, true, relative, relative * button.UIScale),
        });
        ButtonUp.Invoke(button, new object[]
        {
            new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, BoundKeyState.Up, pointer, true, relative, relative * button.UIScale),
        });
    }
}

/// <summary>
/// Lobby actions that allow everything and record what the widgets asked for.
/// </summary>
internal sealed class MalinovRecordingLobbyActions : IMalinovLobbyActions
{
    public readonly List<bool> ReadyRequests = new();
    public int JoinRequests;

    public bool TrySetReady(bool ready)
    {
        ReadyRequests.Add(ready);
        return true;
    }

    public bool TryJoinGame()
    {
        JoinRequests++;
        return true;
    }
}
