using System.Linq;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>Bounded decision history. Intermediate navigation observations never close a result record.</summary>
public sealed partial class AiTraceSystem
{
    [Dependency] private IGameTiming _traceTiming = default!;
    public const int DecisionHistoryLimit = 16;

    public int DecisionStarted(EntityUid uid, float boredom, float curiosity, string? topDesire)
    {
        if (!TryComp<AiDecisionTraceComponent>(uid, out var comp))
            return 0;

        if (comp.Current is { Finished: false, ExecutionId: null, SelectedAction: null } previous)
            FinishRecord(uid, previous, AiActionResult.Cancelled("Решение заменено до выбора действия."), false);

        var trace = new AiDecisionTrace
        {
            Id = ++comp.DecisionId,
            StartedAt = _traceTiming.CurTime,
            Boredom = boredom,
            Curiosity = curiosity,
            TopDesire = topDesire,
        };
        comp.Current = trace;
        comp.History.Add(trace);
        while (comp.History.Count > DecisionHistoryLimit)
        {
            var index = comp.History.FindIndex(t => t.Finished);
            if (index < 0)
            {
                var activeId = TryComp<AiBusyStateComponent>(uid, out var busy) && busy.CurrentAction is not null
                    ? busy.DecisionId : 0;
                index = comp.History.FindIndex(t => t != trace && t.Id != activeId);
            }
            comp.History.RemoveAt(index);
        }
        return trace.Id;
    }

    public int GetCurrentDecisionId(EntityUid uid) =>
        TryComp<AiDecisionTraceComponent>(uid, out var comp) ? comp.Current?.Id ?? 0 : 0;

    public AiDecisionTrace? GetDecision(EntityUid uid, int decisionId) =>
        TryComp<AiDecisionTraceComponent>(uid, out var comp)
            ? comp.History.Find(t => t.Id == decisionId) : null;

    public AiDecisionTrace? GetExecution(EntityUid uid, long executionId) =>
        TryComp<AiDecisionTraceComponent>(uid, out var comp)
            ? comp.History.Find(t => t.ExecutionId == executionId) : null;

    /// <summary>Creates a record for direct dispatch, or uses the unbound decision awaiting an action.</summary>
    public int BeginAction(EntityUid uid, string action)
    {
        var trace = GetDecision(uid, GetCurrentDecisionId(uid));
        if (trace is null || trace.Finished || trace.SelectedAction is not null)
        {
            var id = DecisionStarted(uid,
                TryComp<NeedsComponent>(uid, out var needs) ? needs.Boredom : 0f,
                TryComp<PersonalityComponent>(uid, out var personality) ? personality.Curiosity : 0f,
                TryComp<IntentComponent>(uid, out var intent) ? intent.DesireServed : null);
            trace = GetDecision(uid, id);
            if (trace is not null && TryComp<IntentComponent>(uid, out var currentIntent))
                trace.Intent = currentIntent.Name;
        }

        if (trace is null)
            return 0;
        trace.SelectedAction = action;
        trace.ExecutionState = "ActionSelected";
        return trace.Id;
    }

    public void DecisionIntent(EntityUid uid, string intent, string category,
        IEnumerable<string> eligibleActions, int? decisionId = null)
    {
        if (GetDecision(uid, decisionId ?? GetCurrentDecisionId(uid)) is not { Finished: false } trace)
            return;
        trace.Intent = intent;
        trace.Category = category;
        trace.EligibleActions = string.Join(", ", eligibleActions);
    }

    public int BindExecution(EntityUid uid, long executionId, string action, string target, EntityCoordinates destination)
    {
        var trace = GetDecision(uid, GetCurrentDecisionId(uid));
        if (trace is null || trace.Finished || trace.ExecutionId is not null || trace.SelectedAction != action)
            trace = GetDecision(uid, BeginAction(uid, action));
        if (trace is null)
            return 0;

        trace.SelectedAction = action;
        trace.ExecutionId = executionId;
        trace.ExplorationTarget = target;
        trace.NavigationTarget = destination.ToString();
        trace.ExecutionState = "TargetSelected";
        return trace.Id;
    }

    public void ExecutionPlanning(EntityUid uid, long executionId)
    {
        if (GetExecution(uid, executionId) is { Finished: false, SteeringStarted: false } trace)
            trace.ExecutionState = "Planning";
    }

    public void ExecutionSteeringStarted(EntityUid uid, long executionId)
    {
        if (GetExecution(uid, executionId) is not { Finished: false } trace)
            return;
        trace.SteeringStarted = true;
        trace.ExecutionState = "Executing";
    }

    public void DecisionMovementStarted(EntityUid uid, long executionId)
    {
        if (GetExecution(uid, executionId) is { Finished: false } trace)
            trace.MovementStarted = true;
    }

    public void DecisionDiscovery(EntityUid uid, string place)
    {
        // Perception runs independently of cognition. Only the active journey may own this observation.
        if (!TryComp<AiBusyStateComponent>(uid, out var busy) || busy.JourneyTarget is null ||
            GetExecution(uid, busy.ExecutionId) is not { Finished: false } trace)
            return;
        trace.Discovery = place;
        trace.LocationKnowledgeUpdated = true;
    }

    public void ExecutionFinished(EntityUid uid, long executionId, AiActionResult result, bool delivered)
    {
        if (GetExecution(uid, executionId) is { } trace)
            FinishRecord(uid, trace, result, delivered);
    }

    public void DecisionResult(EntityUid uid, int decisionId, AiActionResult result, bool delivered = false)
    {
        if (GetDecision(uid, decisionId) is { } trace)
        {
            if (result.Outcome == AiActionOutcome.Started && trace.ExecutionId is null)
                trace.ExecutionState = "Executing";
            FinishRecord(uid, trace, result, delivered);
        }
    }

    private void FinishRecord(EntityUid uid, AiDecisionTrace trace, AiActionResult result, bool delivered)
    {
        if (trace.Finished || result.Outcome == AiActionOutcome.Started)
            return;
        trace.Finished = true;
        trace.Result = result;
        trace.Feedback = $"{result.Outcome}: {result.Reason}";
        trace.CognitiveFeedbackDelivered = delivered;
        trace.Reevaluation = delivered;
        trace.ExecutionState = result.Outcome switch
        {
            AiActionOutcome.Completed => "Completed",
            AiActionOutcome.Cancelled => "Cancelled",
            _ => "Failed",
        };
        if (TryComp<AiDecisionTraceComponent>(uid, out var comp))
            comp.Last = trace;
        TraceEventsMetric.WithLabels("Decision").Inc();
        _sawmill.Info($"[AI:{ToPrettyString(uid)}] Decision #{trace.Id}, execution={trace.ExecutionId}, " +
            $"action={trace.SelectedAction}, target={trace.NavigationTarget}, progress={trace.MovementStarted}, " +
            $"result={result.Outcome}, reason={result.Reason}, cognitiveFeedback={delivered}");
    }

    public string? DescribeLastDecision(EntityUid uid)
    {
        if (!TryComp<AiDecisionTraceComponent>(uid, out var comp) || comp.Current is not { } trace)
            return null;
        var described = Describe(trace);
        var execution = comp.History.LastOrDefault(t => t.ExecutionId is not null && t != trace);
        if (execution is not null)
            described += "\n" + Describe(execution);
        return described;
    }

    private static string Describe(AiDecisionTrace trace) =>
        $"Решение #{trace.Id}, исполнение={trace.ExecutionId?.ToString() ?? "-"}, состояние={trace.ExecutionState}:\n" +
        $"  скука={trace.Boredom:0.00} любопытство={trace.Curiosity:0.00} желание={Or(trace.TopDesire)}\n" +
        $"  намерение={Or(trace.Intent)} категория={Or(trace.Category)}\n" +
        $"  пригодные={Or(trace.EligibleActions)}\n" +
        $"  выбрано={Or(trace.SelectedAction)}\n" +
        $"  цель={Or(trace.ExplorationTarget)} точка={Or(trace.NavigationTarget)}\n" +
        $"  steering={trace.SteeringStarted} продвижение={trace.MovementStarted} открыто={Or(trace.Discovery)}\n" +
        $"  результат={trace.Result?.Outcome.ToString() ?? "-"} причина={Or(trace.Result?.Reason)}\n" +
        $"  передано когнитивному слою={trace.CognitiveFeedbackDelivered}";

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;
}
