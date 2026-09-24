namespace Content.Shared.Humanoid.Markings;

public sealed partial class MarkingsGroupPrototype
{
    // Optional rendering behavior for tail markings in this group.
    /// <summary>
    /// Draw tail markings behind the body, except when viewed from the back.
    /// </summary>
    [DataField]
    public bool DirectionalTail;
}
