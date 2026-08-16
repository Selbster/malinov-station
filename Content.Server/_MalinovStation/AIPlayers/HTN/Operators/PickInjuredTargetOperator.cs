using System.Threading;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>
/// Single-shot operator: resolves the owner's <see cref="DangerComponent.NearbyInjured"/> character's
/// current position and writes it to <see cref="TargetKey"/> for a following MoveToOperator task.
/// Implements <see cref="Plan"/> (not just <see cref="Update"/>) with the exact same effect: MoveToOperator's
/// own Plan() unconditionally requires TargetKey to already be in the blackboard during the planning pass, so
/// without this, HelpInjuredCompound would silently fail to plan at all and the branch would never be selected
/// - the same bug class Milestone 11 found and fixed for repair (and Milestone 1 found again for Flee).
/// </summary>
public sealed partial class PickInjuredTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;

    [DataField]
    public string TargetKey = "TargetCoordinates";

    private bool TryResolve(NPCBlackboard blackboard, out EntityCoordinates destination)
    {
        destination = default;

        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<DangerComponent>(owner, out var danger) ||
            danger.NearbyInjured is not { } injured ||
            _entManager.Deleted(injured) ||
            !_entManager.TryGetComponent<TransformComponent>(injured, out var injuredXform))
        {
            return false;
        }

        destination = injuredXform.Coordinates;
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
