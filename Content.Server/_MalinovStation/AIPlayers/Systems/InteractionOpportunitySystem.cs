using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.DeviceLinking.Components;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Periodically scans for nearby single-step, no-UI interactable objects within unobstructed line of sight
/// (AI Players 0.3's first AI Interaction slice, spec section 16), mirroring <see cref="RepairOpportunitySystem"/>/
/// <see cref="LandmarkPerceptionSystem"/>'s own scan-and-populate shape and reusing the same non-omniscience
/// discipline <see cref="PerceptionSystem"/> uses for characters: an AI player can only be aware of something
/// it could actually see right now. Populates <see cref="InteractionOpportunityComponent"/>; read by
/// <see cref="Actions.UseInteractableAction"/> and surfaced to the LLM via
/// <see cref="ContextBuilderSystem.BuildCognitiveState"/>'s <c>NearbyInteractables</c>.
/// </summary>
/// <remarks>
/// v1 only considers <see cref="SignalSwitchComponent"/> (a wall light switch/button) - the smallest real,
/// single-step, no-held-item vanilla interaction (<c>ActivateInWorldEvent</c> resolved synchronously by
/// <c>SignalSwitchSystem</c>, no DoAfter, no BUI). Anything with <see cref="ActivatableUIComponent"/> is
/// explicitly excluded even though a switch shouldn't have one anyway - defense in depth, and documents the
/// "no UI-flow targets" invariant for whenever more target types get added (vending machines, computers,
/// lockers with item transfer are "AI Inventory"/"AI Search" territory, deferred by spec section 15). Doors
/// are out of scope too: AI players already path through them via existing HTN/pathfinding blackboard flags
/// (see <see cref="AIPlayerSystem.SpawnAiPlayer"/>'s NavInteract/NavAccessInteract/NavGentleApproach) - this
/// system is about a discretionary interaction the LLM chooses to make, not movement-required door handling.
///
/// Only ever added to AI players spawned in cognitive mode (see <see cref="AIPlayerSystem.SpawnAiPlayer"/>) -
/// same "new perception side effects are cognitive-only" convention <see cref="LandmarkPerceptionSystem"/>
/// established, and avoids the added spatial-scan cost for every legacy/background AI player.
/// </remarks>
public sealed partial class InteractionOpportunitySystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private AiLodSystem _lod = default!;

    private readonly HashSet<Entity<SignalSwitchComponent>> _nearby = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<InteractionOpportunityComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var opportunity, out var xform))
        {
            opportunity.ScanAccumulator -= frameTime;
            if (opportunity.ScanAccumulator > 0f)
                continue;

            opportunity.ScanAccumulator = opportunity.ScanCooldown * _lod.GetMultiplier(uid);
            Scan(uid, opportunity, xform);
        }
    }

    private void Scan(EntityUid uid, InteractionOpportunityComponent opportunity, TransformComponent xform)
    {
        opportunity.NearbyInteractables.Clear();

        _nearby.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, opportunity.ScanRadius, _nearby);

        foreach (var (candidate, _) in _nearby)
        {
            if (candidate == uid || Deleted(candidate))
                continue;

            if (HasComp<ActivatableUIComponent>(candidate))
                continue;

            if (!_interaction.InRangeUnobstructed(uid, candidate, opportunity.ScanRadius, CollisionGroup.Opaque))
                continue;

            opportunity.NearbyInteractables.Add(candidate);
        }
    }
}
