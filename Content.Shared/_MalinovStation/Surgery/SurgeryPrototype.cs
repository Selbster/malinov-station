using Content.Shared.Body;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._MalinovStation.Surgery;

/// <summary>
/// An ordered sequence of <see cref="SurgeryStepPrototype"/>s that either extracts or installs a single
/// organ category on a patient. Availability is derived from whether the patient currently has the
/// <see cref="TargetOrgan"/>, rather than from a separate "completed" flag.
/// </summary>
[Prototype]
public sealed partial class SurgeryPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name { get; private set; }

    [DataField]
    public LocId? Description { get; private set; }

    /// <summary>
    /// Coarse anatomical grouping shown as the first navigation pane in the surgery window (stands in for
    /// Starlight's "Parts" pane, since this repo's body has no separate part entities to list instead).
    /// </summary>
    [DataField(required: true)]
    public ProtoId<SurgeryRegionPrototype> Region { get; private set; }

    [DataField(required: true)]
    public ProtoId<OrganCategoryPrototype> TargetOrgan { get; private set; }

    /// <summary>
    /// True for extraction surgeries (patient must currently have the organ), false for installation
    /// surgeries (patient must currently be missing the organ).
    /// </summary>
    [DataField(required: true)]
    public bool RequireOrganPresent { get; private set; }

    /// <summary>
    /// An additional organ category that must be present for this surgery to be offered - e.g. a hand
    /// can't be installed without an arm to attach it to, even though the hand's own <see cref="TargetOrgan"/>
    /// is what's actually missing/present.
    /// </summary>
    [DataField]
    public ProtoId<OrganCategoryPrototype>? RequiresOrgan { get; private set; }

    [DataField(required: true)]
    public List<ProtoId<SurgeryStepPrototype>> Steps { get; private set; } = new();

    [DataField]
    public HashSet<ProtoId<SpeciesPrototype>>? SpeciesWhitelist { get; private set; }
}
