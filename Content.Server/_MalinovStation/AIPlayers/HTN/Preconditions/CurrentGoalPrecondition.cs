using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Preconditions;

/// <summary>
/// IsMet when the owner's <see cref="GoalComponent.CurrentGoal"/> equals <see cref="Goal"/>. This is what
/// makes <see cref="Systems.GoalSystem"/>'s priority arbitration (needs + personality + danger, and - via
/// <see cref="Systems.LlmGatewaySystem"/> - the LLM) the actual decider between several simultaneously
/// eligible branches, instead of the fixed top-to-bottom order in AIPlayerRootCompound always winning.
/// Deliberately paired with each branch's own real-world fact precondition (e.g. DangerPrecondition,
/// RepairOpportunityPrecondition), never used alone: an LLM/goal decision can pick which eligible branch runs,
/// but it can never make a branch run when its underlying fact isn't true. Food/Thirst (vanilla FoodCompound)
/// and ForcedMove (an explicit directive, not a needs/danger goal) intentionally aren't gated by this - see
/// GoalComponent's doc comment.
/// </summary>
public sealed partial class CurrentGoalPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    [DataField(required: true)]
    public string Goal = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        return blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager) &&
               _entManager.TryGetComponent<GoalComponent>(owner, out var goal) &&
               goal.CurrentGoal == Goal;
    }
}
