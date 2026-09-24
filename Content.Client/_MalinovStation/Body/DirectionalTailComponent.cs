namespace Content.Client._MalinovStation.Body;

/// <summary>
/// Tracks the sprite layers whose order follows the tail's on-screen direction.
/// </summary>
[RegisterComponent, Access(typeof(DirectionalTailSystem))]
public sealed partial class DirectionalTailComponent : Component
{
    /// <summary>
    /// Runtime layer-map keys in marking sprite order; rebuilt when organ markings change.
    /// </summary>
    [NonSerialized, ViewVariables(VVAccess.ReadOnly)]
    public readonly List<string> Layers = new();

    /// <summary>
    /// Last applied ordering relative to the body. Null forces an update after marking layers change.
    /// </summary>
    [NonSerialized, ViewVariables(VVAccess.ReadOnly)]
    public bool? InFront;
}
