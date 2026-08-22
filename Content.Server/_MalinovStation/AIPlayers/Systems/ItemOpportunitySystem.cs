using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Physics;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Periodically scans for nearby loose (not held/worn/contained) items within unobstructed line of sight (AI
/// Players 0.3's first AI Inventory slice), mirroring <see cref="InteractionOpportunitySystem"/>'s own
/// scan-and-populate shape exactly, reusing the same non-omniscience discipline <see cref="PerceptionSystem"/>
/// uses for characters. Populates <see cref="ItemOpportunityComponent"/>; read by
/// <see cref="Actions.PickUpItemAction"/> and surfaced to the LLM via
/// <see cref="ContextBuilderSystem.BuildCognitiveState"/>'s <c>NearbyItems</c>.
/// </summary>
/// <remarks>
/// Only considers <see cref="ItemComponent"/> holders that are both not currently inside any container
/// (<see cref="SharedContainerSystem.IsEntityInContainer"/> - excludes an item already held in a hand, worn
/// in an inventory slot, or sitting inside a backpack/locker, all containers in this ECS) and not anchored
/// (excludes pipes/vents/scrubbers/other station infrastructure that happens to carry
/// <see cref="ItemComponent"/> for construction/deconstruction purposes but was never "loose" in any sense
/// worth offering the LLM). Whether the AI can actually pick a given surviving candidate up (full hands,
/// action-blocked states) is left to <see cref="Actions.PickUpItemAction.CanDo"/> via vanilla's own
/// <c>SharedHandsSystem.CanPickupAnyHand</c> rather than duplicated here - the same "cheap pre-filter, not
/// exhaustive" split <see cref="InteractionOpportunitySystem"/> already established.
///
/// Only ever added to AI players spawned in cognitive mode (see <see cref="AIPlayerSystem.SpawnAiPlayer"/>) -
/// same "new perception side effects are cognitive-only" convention every other opportunity system follows.
/// </remarks>
public sealed partial class ItemOpportunitySystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private AiLodSystem _lod = default!;

    private readonly HashSet<Entity<ItemComponent>> _nearby = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ItemOpportunityComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var opportunity, out var xform))
        {
            opportunity.ScanAccumulator -= frameTime;
            if (opportunity.ScanAccumulator > 0f)
                continue;

            opportunity.ScanAccumulator = opportunity.ScanCooldown * _lod.GetMultiplier(uid);
            Scan(uid, opportunity, xform);
        }
    }

    private void Scan(EntityUid uid, ItemOpportunityComponent opportunity, TransformComponent xform)
    {
        opportunity.NearbyItems.Clear();

        _nearby.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, opportunity.ScanRadius, _nearby);

        foreach (var (candidate, _) in _nearby)
        {
            if (candidate == uid || Deleted(candidate))
                continue;

            if (_container.IsEntityInContainer(candidate))
                continue;

            // Anchored entities (pipes, vents, scrubbers, machines with ItemComponent for construction/
            // deconstruction purposes) aren't "loose" in any sense an AI should reason about picking up -
            // Anchored is the direct ECS concept for "fixed in place", a much more precise signal than
            // waiting for PickUpItemAction.CanDo's CanPickupAnyHand (BodyType.Static) to reject it one at a
            // time. Filtering here keeps the LLM's candidate list from filling up with station infrastructure.
            if (Transform(candidate).Anchored)
                continue;

            if (!_interaction.InRangeUnobstructed(uid, candidate, opportunity.ScanRadius, CollisionGroup.Opaque))
                continue;

            opportunity.NearbyItems.Add(candidate);
        }
    }
}
