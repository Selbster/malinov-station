using Robust.Shared.Prototypes;

namespace Content.Shared.Body;

/// <summary>
/// On map initialization, spawns the given organs into the body.
/// Liable to change as the body becomes more complex.
/// </summary>
[RegisterComponent]
[Access(typeof(InitialBodySystem))]
public sealed partial class InitialBodyComponent : Component
{
    /// <summary>
    /// The organs to spawn based on their category.
    /// </summary>
    [DataField(required: true)]
    public Dictionary<ProtoId<OrganCategoryPrototype>, EntProtoId<OrganComponent>> Organs;

    /// <summary>
    /// Organs to spawn inside a part-bearing entry of <see cref="Organs"/>, keyed by the owning part's own
    /// category (e.g. Torso -> {Heart, Lungs, ...}). Entries whose key isn't a part (or isn't present in
    /// <see cref="Organs"/> at all) are ignored - species without a Parts layer (most animals) simply never
    /// populate this and keep spawning everything flat via <see cref="Organs"/> alone.
    /// </summary>
    [DataField]
    public Dictionary<ProtoId<OrganCategoryPrototype>, Dictionary<ProtoId<OrganCategoryPrototype>, EntProtoId<OrganComponent>>> PartOrgans = new();
}
