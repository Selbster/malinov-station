using Content.Shared.Damage;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;

namespace Content.Shared._MalinovStation.Surgery;

/// <summary>
/// A single step in a <see cref="SurgeryPrototype"/> - e.g. "make an incision" or "extract the organ".
/// </summary>
[Prototype]
public sealed partial class SurgeryStepPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name { get; private set; }

    [DataField]
    public LocId? Description { get; private set; }

    /// <summary>
    /// Base time to perform this step. Scaled by the tool's <see cref="Components.SurgeryToolComponent.Speed"/>.
    /// </summary>
    [DataField(required: true)]
    public TimeSpan Duration { get; private set; }

    [DataField(required: true)]
    public SurgeryStepKind Kind { get; private set; }

    /// <summary>
    /// The tool type the surgeon must be holding to perform this step. Null for steps that don't need
    /// a dedicated tool (e.g. <see cref="SurgeryStepKind.ExtractOrgan"/>/<see cref="SurgeryStepKind.InsertOrgan"/>,
    /// which just need empty/full hands).
    /// </summary>
    [DataField]
    public SurgeryToolType? RequiredTool { get; private set; }

    /// <summary>
    /// Bleed amount applied to the patient on success (positive for e.g. Incision, negative for e.g. ClampBleeders).
    /// Only used by non-organ step kinds.
    /// </summary>
    [DataField]
    public float BleedDelta { get; private set; }

    /// <summary>Direct damage dealt to the patient when this step succeeds (e.g. the cut itself).</summary>
    [DataField]
    public DamageSpecifier? Damage { get; private set; }

    /// <summary>Extra damage dealt to the patient if this step fails and has to be retried (a slip).</summary>
    [DataField]
    public DamageSpecifier? MishapDamage { get; private set; }

    /// <summary>Shown to everyone near the patient when this step completes.</summary>
    [DataField]
    public LocId? Popup { get; private set; }

    [DataField]
    public SoundSpecifier? Sound { get; private set; }
}
