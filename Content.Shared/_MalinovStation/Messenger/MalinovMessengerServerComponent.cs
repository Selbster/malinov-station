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
}
