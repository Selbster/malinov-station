using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>
/// Single-shot operator: resolves the owner's <see cref="DangerComponent.NearbyInjured"/> character's
/// current position and writes it to <see cref="TargetKey"/> for a following MoveToOperator task.
/// </summary>
public sealed partial class PickInjuredTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;

    [DataField]
    public string TargetKey = "TargetCoordinates";

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<DangerComponent>(owner, out var danger) ||
            danger.NearbyInjured is not { } injured ||
            _entManager.Deleted(injured) ||
            !_entManager.TryGetComponent<TransformComponent>(injured, out var injuredXform))
        {
            return HTNOperatorStatus.Failed;
        }

        blackboard.SetValue(TargetKey, injuredXform.Coordinates);
        return HTNOperatorStatus.Finished;
    }
}
