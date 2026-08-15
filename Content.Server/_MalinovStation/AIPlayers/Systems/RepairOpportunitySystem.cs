using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Repairable;
using Content.Shared.Tools;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Periodically scans for a nearby damaged <see cref="RepairableComponent"/> machine within unobstructed
/// line of sight (spec Milestone 11's first professional/job-specific goal), mirroring
/// <see cref="DangerSystem"/>'s own scan-and-populate-a-component shape and reusing the same non-omniscience
/// discipline <see cref="PerceptionSystem"/> uses for characters: an AI player can only be aware of a broken
/// machine it could actually see. Populates <see cref="RepairOpportunityComponent"/>; read by
/// <see cref="GoalSystem"/> and the RepairMachineCompound HTN precondition/operator.
/// </summary>
/// <remarks>
/// Only considers machines needing <see cref="WeldingQuality"/> - Milestone 11 gives AI players no way to
/// know in advance whether they're holding the right tool for a machine that needs something else, so the
/// scan and <c>HasToolQualityPrecondition Quality: Welding</c> are kept in lockstep instead. Widening this to
/// other tool qualities is a follow-up, not a v1 requirement.
/// </remarks>
public sealed partial class RepairOpportunitySystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private AiLodSystem _lod = default!;

    private static readonly ProtoId<ToolQualityPrototype> WeldingQuality = "Welding";

    private readonly HashSet<Entity<RepairableComponent>> _nearby = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<RepairOpportunityComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var opportunity, out var xform))
        {
            opportunity.ScanAccumulator -= frameTime;
            if (opportunity.ScanAccumulator > 0f)
                continue;

            opportunity.ScanAccumulator = opportunity.ScanCooldown * _lod.GetMultiplier(uid);
            Scan(uid, opportunity, xform);
        }
    }

    private void Scan(EntityUid uid, RepairOpportunityComponent opportunity, TransformComponent xform)
    {
        opportunity.NearbyRepairTarget = null;

        _nearby.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, opportunity.ScanRadius, _nearby);

        foreach (var (machine, repairable) in _nearby)
        {
            if (machine == uid || Deleted(machine))
                continue;

            if (repairable.QualityNeeded != WeldingQuality)
                continue;

            if (!TryComp<DamageableComponent>(machine, out var damageable))
                continue;

            // Safe: mirrors the same (obsolete-but-still-current) API RepairableSystem.Repair itself uses to
            // decide whether there's anything to fix - see that method for the vanilla precedent.
#pragma warning disable CS0618
            if (_damageable.GetTotalDamage((machine, damageable)) <= 0)
                continue;
#pragma warning restore CS0618

            if (!_interaction.InRangeUnobstructed(uid, machine, opportunity.ScanRadius, CollisionGroup.Opaque))
                continue;

            opportunity.NearbyRepairTarget = machine;
            return;
        }
    }
}
