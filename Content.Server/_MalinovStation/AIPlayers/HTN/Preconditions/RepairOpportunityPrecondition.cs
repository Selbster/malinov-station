using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Prototypes;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Robust.Shared.Prototypes;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Preconditions;

/// <summary>
/// IsMet when the owner currently sees a damaged repairable machine (populated by
/// <see cref="RepairOpportunitySystem"/>'s scan) AND its job is allowed to pursue
/// <see cref="ProfessionalGoals.RepairMachine"/>. Re-derives the same fact <see cref="GoalSystem"/> uses to
/// compute the RepairMachine priority, independently of it - RepairMachineCompound also separately requires
/// <c>CurrentGoalPrecondition</c> to match, so this is defense-in-depth: CurrentGoal alone (whether set by
/// GoalSystem or an LLM override) can never make this branch run against a job/opportunity that isn't real.
/// </summary>
public sealed partial class RepairOpportunityPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager) ||
            !_entManager.TryGetComponent<RepairOpportunityComponent>(owner, out var opportunity) ||
            opportunity.NearbyRepairTarget is not { } target ||
            _entManager.Deleted(target))
        {
            return false;
        }

        if (!_entManager.TryGetComponent<AIPlayerComponent>(owner, out var aiPlayer) || aiPlayer.Job is not { } job)
            return false;

        return _proto.TryIndex<AiProfessionalGoalPrototype>(ProfessionalGoals.RepairMachine, out var goalProto) &&
               goalProto.Jobs.Contains(job);
    }
}
