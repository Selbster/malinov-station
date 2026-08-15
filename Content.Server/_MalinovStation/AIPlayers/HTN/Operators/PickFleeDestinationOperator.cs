using System.Numerics;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.GameObjects;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>
/// Single-shot operator: picks a point away from the owner's current <see cref="DangerComponent.ThreatSource"/>
/// and writes it to <see cref="TargetKey"/> (default "TargetCoordinates", the same key MoveToOperator reads
/// by default) - the following MoveToOperator task does the actual fleeing via the normal
/// pathfinding/steering stack. Re-running this (via HTN's constant replanning while the threat stays active)
/// naturally keeps picking a fresh point away from the threat's current position.
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

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<DangerComponent>(owner, out var danger) ||
            danger.ThreatSource is not { } threat ||
            _entManager.Deleted(threat))
        {
            return HTNOperatorStatus.Failed;
        }

        if (!_entManager.TryGetComponent<TransformComponent>(owner, out var ownerXform) ||
            !_entManager.TryGetComponent<TransformComponent>(threat, out var threatXform))
        {
            return HTNOperatorStatus.Failed;
        }

        var direction = _transform.GetWorldPosition(ownerXform) - _transform.GetWorldPosition(threatXform);
        if (direction.LengthSquared() < 0.01f)
            direction = new Vector2(1, 0);

        direction = Vector2.Normalize(direction) * FleeDistance;

        blackboard.SetValue(TargetKey, ownerXform.Coordinates.Offset(direction));
        return HTNOperatorStatus.Finished;
    }
}
