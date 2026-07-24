using Content.Shared._MalinovStation.Surgery;
using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._MalinovStation.Surgery.Components;

/// <summary>
/// Marks an item as usable to perform surgery steps that require <see cref="ToolType"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SurgeryToolComponent : Component
{
    [DataField(required: true)]
    public SurgeryToolType ToolType;

    /// <summary>Multiplier applied to a step's base duration. Below 1 is faster.</summary>
    [DataField]
    public float Speed = 1f;

    /// <summary>Chance in [0,1] that a step performed with this tool succeeds instead of having to be retried.</summary>
    [DataField]
    public float SuccessRate = 1f;

    [DataField]
    public SoundSpecifier? StartSound;

    [DataField]
    public SoundSpecifier? EndSound;
}
