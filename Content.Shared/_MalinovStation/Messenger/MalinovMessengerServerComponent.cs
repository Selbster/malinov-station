namespace Content.Shared._MalinovStation.Messenger;

/// <summary>
///     Marker component for the station Malinov Messenger relay server.
/// </summary>
[RegisterComponent]
public sealed partial class MalinovMessengerServerComponent : Component
{
    /// <summary>
    ///     Round-local directory mapping display name to device network address.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public Dictionary<string, string> Directory = new();

    /// <summary>
    ///     Sender display names that are muted by admins and must be silently dropped
    ///     by the relay. Lives for the duration of the round only.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite)]
    public HashSet<string> Muted = new();
}
