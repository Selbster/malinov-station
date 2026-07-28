using Robust.Shared.Containers;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared.Body;

/// <summary>
/// Marks an organ entity as also being a body part - a container for other organs (e.g. the heart/lungs
/// live inside the torso, the hand lives inside the arm). Sits one level below <see cref="BodyComponent"/>:
/// a part is itself an organ of the body (still a direct child of <see cref="BodyComponent.ContainerID"/>),
/// while everything inside its own <see cref="Organs"/> container is one level deeper. See <see cref="BodySystem"/>
/// for how insertion/removal at either level still reaches the true body entity.
/// </summary>
/// <seealso cref="BodySystem" />
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
[Access(typeof(BodySystem))]
public sealed partial class BodyPartComponent : Component
{
    public const string ContainerID = "part_organs";

    /// <summary>
    /// The actual container with entities with <see cref="OrganComponent" /> in it.
    /// </summary>
    [ViewVariables]
    public Container? Organs;

    /// <summary>
    /// The body this part is currently attached to, if any - null while detached (e.g. a severed limb).
    /// </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Body;

    /// <summary>
    /// What kind of part this is. Every part-bearing entity also has an <see cref="OrganComponent"/> whose
    /// own Category is normally identical to this one - duplicated here so part identity is cheap to read
    /// without cross-referencing the sibling component, and so a future non-organ part isn't forced to have one.
    /// </summary>
    [DataField]
    public ProtoId<OrganCategoryPrototype>? Category;
}

/// <summary>
/// Raised on a part entity, when it is inserted into a body
/// </summary>
[ByRefEvent]
public readonly record struct PartGotInsertedEvent(EntityUid Target);

/// <summary>
/// Raised on a part entity, when it is removed from a body
/// </summary>
[ByRefEvent]
public readonly record struct PartGotRemovedEvent(EntityUid Target);

/// <summary>
/// Raised on a body entity, when a part is inserted into it
/// </summary>
[ByRefEvent]
public readonly record struct PartInsertedIntoEvent(EntityUid Part);

/// <summary>
/// Raised on a body entity, when a part is removed from it
/// </summary>
[ByRefEvent]
public readonly record struct PartRemovedFromEvent(EntityUid Part);
