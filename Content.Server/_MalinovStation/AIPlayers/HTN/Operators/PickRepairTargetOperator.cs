using System.Threading;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>
/// Single-shot operator: resolves the owner's <see cref="RepairOpportunityComponent.NearbyRepairTarget"/>
/// and writes both its coordinates (for the following vanilla MoveToOperator) and the entity itself (for the
/// following vanilla InteractWithOperator, which does the actual repair by reusing the same
/// InteractUsingEvent pipeline a real player's click would trigger against RepairableComponent).
/// </summary>
/// <remarks>
/// Overrides <see cref="Plan"/> (not just <see cref="Update"/>) to return the same values as effects:
/// HTN's planner builds and validates the whole branch *before* ever calling Update on anything (see
/// HTNPlanJob.PrimitiveConditionMet), so the following MoveToOperator's own Plan() - which requires
/// TargetCoordinates to already be on the (simulated) blackboard - would never see it and the branch would
/// never be selected, without this.
/// </remarks>
public sealed partial class PickRepairTargetOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;

    [DataField]
    public string CoordinatesKey = "TargetCoordinates";

    [DataField]
    public string TargetKey = "RepairTarget";

    private bool TryResolve(NPCBlackboard blackboard, out EntityUid target, out EntityCoordinates coordinates)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (_entManager.TryGetComponent<RepairOpportunityComponent>(owner, out var opportunity) &&
            opportunity.NearbyRepairTarget is { } candidate &&
            !_entManager.Deleted(candidate) &&
            _entManager.TryGetComponent<TransformComponent>(candidate, out var targetXform))
        {
            target = candidate;
            coordinates = targetXform.Coordinates;
            return true;
        }

        target = default;
        coordinates = default;
        return false;
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        if (!TryResolve(blackboard, out var target, out var coordinates))
            return (false, null);

        return (true, new Dictionary<string, object>
        {
            { CoordinatesKey, coordinates },
            { TargetKey, target },
        });
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!TryResolve(blackboard, out var target, out var coordinates))
            return HTNOperatorStatus.Failed;

        blackboard.SetValue(CoordinatesKey, coordinates);
        blackboard.SetValue(TargetKey, target);
        return HTNOperatorStatus.Finished;
    }
}
