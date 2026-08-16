using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>
/// Single-shot operator: picks a point away from the owner's current threat - <see cref="DangerComponent.ThreatSource"/>
/// (an attacker) if set, else <see cref="DangerComponent.FireHazardLocation"/> (a nearby fire has no entity to
/// point at) - and writes it to <see cref="TargetKey"/> (default "TargetCoordinates", the same key
/// MoveToOperator reads by default) - the following MoveToOperator task does the actual fleeing via the normal
/// pathfinding/steering stack. Re-running this (via HTN's constant replanning while the threat stays active)
/// naturally keeps picking a fresh point away from the threat's current position.
/// Implements <see cref="Plan"/> (not just <see cref="Update"/>) with the exact same effect: MoveToOperator's
/// own Plan() unconditionally requires TargetKey to already be in the blackboard during the planning pass
/// (independent of pathfindInPlanning), so without this, FleeCompound would silently fail to plan at all and
/// the branch would never be selected - the same bug class Milestone 11 found and fixed for repair.
/// </summary>
public sealed partial class PickFleeDestinationOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private SharedTransformSystem _transform = default!;

    [DataField]
    public float FleeDistance = 8f;

    [DataField]
    public string TargetKey = "TargetCoordinates";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _transform = sysManager.GetEntitySystem<SharedTransformSystem>();
    }

    private bool TryResolve(NPCBlackboard blackboard, out EntityCoordinates destination)
    {
        destination = default;

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<DangerComponent>(owner, out var danger) ||
            !_entManager.TryGetComponent<TransformComponent>(owner, out var ownerXform))
        {
            return false;
        }

        Vector2 threatWorldPos;
        if (danger.ThreatSource is { } threat && !_entManager.Deleted(threat) &&
            _entManager.TryGetComponent<TransformComponent>(threat, out var threatXform))
        {
            threatWorldPos = _transform.GetWorldPosition(threatXform);
        }
        else if (danger.FireHazardLocation is { } fireLocation)
        {
            threatWorldPos = _transform.ToMapCoordinates(fireLocation).Position;
        }
        else
        {
            return false;
        }

        var direction = _transform.GetWorldPosition(ownerXform) - threatWorldPos;
        if (direction.LengthSquared() < 0.01f)
            direction = new Vector2(1, 0);

        direction = Vector2.Normalize(direction) * FleeDistance;

        destination = ownerXform.Coordinates.Offset(direction);
        return true;
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!TryResolve(blackboard, out var destination))
            return (false, null);

        return (true, new Dictionary<string, object> { { TargetKey, destination } });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!TryResolve(blackboard, out var destination))
            return HTNOperatorStatus.Failed;

        blackboard.SetValue(TargetKey, destination);
        return HTNOperatorStatus.Finished;
    }
}
