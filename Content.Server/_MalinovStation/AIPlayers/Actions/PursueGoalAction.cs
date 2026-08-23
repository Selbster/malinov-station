using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Commits to actively pursuing one of the AI's existing reflexive goals right now (AI Players 0.3) - the
/// bridge from a cognitive AI's free-form <see cref="IntentComponent"/> back to the HTN-backed behaviour that
/// already exists for it (Flee/HelpInjured/Rest/RepairMachine etc, see <see cref="GoalComponent"/>'s own doc
/// comment). The only way a cognitive decision reaches <see cref="GoalComponent"/> - unlike
/// <see cref="MoveToAction"/>, which still isn't itself directly LLM-selectable (see
/// <see cref="LLM.ActionProposalResolver"/>): a cognitive AI reaches the same underlying movement via
/// <see cref="GoToKnownLocationAction"/> instead, which resolves a remembered place name to coordinates so
/// the LLM never has to invent raw <see cref="Robust.Shared.Map.EntityCoordinates"/> itself.
/// </summary>
/// <remarks>
/// Takes its dependencies via constructor rather than [Dependency] fields - see <see cref="TalkAction"/>'s
/// remarks for why.
/// </remarks>
public sealed class PursueGoalAction : IAiAction
{
    public const string ActionName = "PursueGoal";

    private readonly IEntityManager _entManager;
    private readonly GoalSystem _goal;

    public PursueGoalAction(IEntityManager entManager, GoalSystem goal)
    {
        _entManager = entManager;
        _goal = goal;
    }

    public string Name => ActionName;
    public string Description => "Commit to actively pursuing one of the AI's existing reflexive goals right now.";
    public string Category => AiActionCategories.Work;
    public bool IsExtended => true;

    public bool IsEligible(EntityUid uid)
    {
        return _entManager.HasComponent<GoalComponent>(uid);
    }

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not PursueGoalActionParams pursue)
        {
            failReason = $"{Name} requires {nameof(PursueGoalActionParams)}.";
            return false;
        }

        if (!_entManager.TryGetComponent<GoalComponent>(uid, out _))
        {
            failReason = "Entity is not a valid AI player.";
            return false;
        }

        if (!_goal.IsKnownGoalName(pursue.GoalName))
        {
            failReason = $"Unknown goal \"{pursue.GoalName}\".";
            return false;
        }

        failReason = null;
        return true;
    }

    public void Do(EntityUid uid, IAiActionParams parameters)
    {
        var pursue = (PursueGoalActionParams)parameters;
        _goal.TrySetExternalGoal(uid, pursue.GoalName, pursue.Priority, $"cognitive: {pursue.Reason}", out _);
    }
}
