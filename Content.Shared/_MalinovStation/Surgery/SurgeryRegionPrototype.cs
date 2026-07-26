using Robust.Shared.Prototypes;

namespace Content.Shared._MalinovStation.Surgery;

/// <summary>
/// A coarse anatomical grouping used purely to organize the surgery UI (e.g. "Head", "Torso").
/// This repo's body has no separate body-part entities to navigate (see <see cref="SurgeryPrototype"/>'s
/// remarks), so this stands in for Starlight's "Parts" pane as a lighter-weight, purely cosmetic grouping -
/// a foundation to build a real per-part navigation on top of later, if this fork ever gains one.
/// </summary>
[Prototype]
public sealed partial class SurgeryRegionPrototype : IPrototype
{
    [IdDataField]
    public string ID { get; private set; } = default!;

    [DataField(required: true)]
    public LocId Name { get; private set; }

    /// <summary>Lower sorts first in the region list.</summary>
    [DataField]
    public int SortOrder { get; private set; }
}
