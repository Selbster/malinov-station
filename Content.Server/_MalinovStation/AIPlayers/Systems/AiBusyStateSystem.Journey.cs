using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.HTN.Operators;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

public sealed partial class AiBusyStateSystem
{
    /// <summary>Replaces old execution before publishing the new destination to HTN.</summary>
    public long StartJourney(EntityUid uid, string action, EntityCoordinates target,
        string? placeName = null, string? targetDescription = null)
    {
        var busy = EnsureComp<AiBusyStateComponent>(uid);
        if (busy.CurrentAction is not null)
            FinishAction(uid, busy, AiActionResult.Cancelled("Ты выбрал(а) другое действие."));
        else
            StopExecution(uid);

        busy.ExecutionId++;
        busy.CurrentAction = action;
        busy.StartedAt = _timing.CurTime;
        busy.JourneyTarget = target;
        busy.JourneyPlace = placeName;
        busy.JourneyStart = Transform(uid).Coordinates;
        busy.ExternallyRelocated = false;
        busy.SteeringStarted = false;
        busy.ProgressConfirmed = false;
        busy.PendingResult = null;

        var htn = Comp<HTNComponent>(uid);
        htn.Blackboard.SetValue(MoveToAction.ForcedDestinationKey, target);
        htn.Blackboard.SetValue(AiJourneyOperator.JourneyIdKey, busy.ExecutionId);
        htn.PlanAccumulator = 0f;

        if (action == ExploreStationAction.ActionName)
        {
            var exploration = EnsureComp<ExplorationComponent>(uid);
            exploration.CurrentTargetName = placeName;
            exploration.CurrentTargetCoordinates = target;
        }

        busy.DecisionId = _trace.BindExecution(uid, busy.ExecutionId, action,
            targetDescription ?? placeName ?? target.ToString(), target);
        return busy.ExecutionId;
    }

    public bool IsCurrentJourney(EntityUid uid, long executionId) =>
        TryComp<AiBusyStateComponent>(uid, out var busy) && busy.CurrentAction is not null &&
        busy.JourneyTarget is not null && busy.ExecutionId == executionId &&
        busy.LastFinishedExecutionId != executionId;

    /// <summary>Safe inside HTN callbacks. Only the first terminal observation is accepted.</summary>
    public void QueueJourneyResult(EntityUid uid, long executionId, AiActionResult result)
    {
        if (result.Outcome == AiActionOutcome.Started || !IsCurrentJourney(uid, executionId))
            return;
        Comp<AiBusyStateComponent>(uid).PendingResult ??= result;
    }

    public void JourneySteeringStarted(EntityUid uid, long executionId)
    {
        if (!IsCurrentJourney(uid, executionId))
            return;
        Comp<AiBusyStateComponent>(uid).SteeringStarted = true;
        _trace.ExecutionSteeringStarted(uid, executionId);
    }

    /// <summary>
    /// Claims one terminal result, cancels planning, shuts down consumers, then removes their data.
    /// Call outside HTN callbacks; operators use <see cref="QueueJourneyResult"/> instead.
    /// </summary>
    public bool FinishJourney(EntityUid uid, long executionId, AiActionResult result)
    {
        if (result.Outcome == AiActionOutcome.Started || !IsCurrentJourney(uid, executionId))
            return false;

        var busy = Comp<AiBusyStateComponent>(uid);
        ObserveProgress(uid, busy);
        result = busy.PendingResult ?? result;
        if (busy.ExternallyRelocated)
            result = AiActionResult.Cancelled("Тебя переместили; прежнее действие отменено.");
        var place = busy.JourneyPlace;
        var action = busy.CurrentAction!;
        busy.LastFinishedExecutionId = executionId;
        busy.LastResult = result;
        StopExecution(uid);

        if (TryComp<ExplorationComponent>(uid, out var exploration))
        {
            if (place is not null && result.Outcome is AiActionOutcome.NoPath or AiActionOutcome.AccessDenied or AiActionOutcome.Blocked)
                exploration.UnreachablePlaces[place] = _timing.CurTime + TimeSpan.FromSeconds(exploration.UnreachableMemorySeconds);

            exploration.CurrentTargetName = null;
            exploration.CurrentTargetCoordinates = null;
        }

        Clear(busy);
        var delivered = PublishResult(uid, action, result);
        _trace.ExecutionFinished(uid, executionId, result, delivered);
        return true;
    }

    /// <summary>Used when a validated non-travel action replaces the current commitment.</summary>
    public void CancelCurrentAction(EntityUid uid, string reason)
    {
        if (TryComp<AiBusyStateComponent>(uid, out var busy) && busy.CurrentAction is not null)
            FinishAction(uid, busy, AiActionResult.Cancelled(reason));
    }

    private void FinishAction(EntityUid uid, AiBusyStateComponent busy, AiActionResult result, bool stopExecution = true)
    {
        if (busy.JourneyTarget is not null)
        {
            FinishJourney(uid, busy.ExecutionId, result);
            return;
        }

        var action = busy.CurrentAction!;
        var decisionId = busy.DecisionId;
        if (stopExecution)
            StopExecution(uid);
        if (action == PursueGoalAction.ActionName && TryComp<GoalComponent>(uid, out var goal))
        {
            goal.IsLlmOverride = false;
            goal.ReconsiderAccumulator = 0f;
        }

        busy.LastResult = result;
        Clear(busy);
        _trace.DecisionResult(uid, decisionId, result, PublishResult(uid, action, result));
    }

    private bool PublishResult(EntityUid uid, string action, AiActionResult result)
    {
        if (!TryComp<CognitiveModeComponent>(uid, out var cognitive))
            return false;

        var content = $"Действие {action}: {result.Reason}";
        _memory.AddMemory(uid, content: content,
            importance: MemoryImportanceScorer.Score("outcome", content: content), source: "outcome");
        cognitive.ReflectionAccumulator = 0f;
        return true;
    }

    internal void StopExecution(EntityUid uid)
    {
        if (TryComp<NPCSteeringComponent>(uid, out var steering))
            _doAfter.Cancel(steering.DoAfterId);

        if (TryComp<HTNComponent>(uid, out var htn))
        {
            // Cancellation alone does not detach an already finished job from HTN's next update.
            htn.PlanningToken?.Cancel();
            htn.PlanningToken = null;
            htn.PlanningJob = null;
            if (htn.Plan is { } plan)
            {
                _htn.ShutdownTask(plan.CurrentOperator, htn.Blackboard, HTNOperatorStatus.Failed);
                _htn.ShutdownPlan(htn);
            }

            htn.Blackboard.Remove<EntityCoordinates>(MoveToAction.ForcedDestinationKey);
            htn.Blackboard.Remove<long>(AiJourneyOperator.JourneyIdKey);
            htn.Blackboard.Remove<long>(AiJourneyOperator.PlannedIdKey);
            htn.Blackboard.Remove<NPCSteeringComponent>(AiJourneyOperator.SteeringKey);
            htn.Blackboard.Remove<PathResultEvent>(NPCBlackboard.PathfindKey);
            htn.PlanAccumulator = 0f;
        }

        _steering.Unregister(uid);
        _doors.CancelApproach(uid);

        // The explicit result supersedes the old heuristic which infers failure from a plan diff.
        if (TryComp<AiTraceStateComponent>(uid, out var state))
        {
            state.LastPlan = null;
            state.LastPlanIndex = -1;
            state.LastOperatorName = null;
        }
    }
}
