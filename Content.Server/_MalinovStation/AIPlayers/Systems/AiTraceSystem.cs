using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC.HTN;
using Prometheus;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Single structured logging surface for AI player behaviour (Stabilization milestone, stage 1: "the main
/// server log is too noisy to analyse NPC behaviour"). One sawmill ("aiplayers.trace"), one line per
/// meaningful event, never per-tick. Other AI systems call into this instead of logging directly, so a
/// round's AI behaviour can be filtered out of the noisy main log (dungeon gen, physics, etc.) by sawmill
/// alone.
///
/// Goal/Need events are raised by <see cref="GoalSystem"/> and <see cref="LlmGatewaySystem"/> directly,
/// since they already own that state. Plan/Action events are different: vanilla HTN dispatch
/// (Content.Server/NPC/HTN/HTNSystem.cs) raises no events of its own to hook into, and it's the hottest
/// shared code path for every NPC in the game, so instrumenting it directly wasn't worth the blast radius.
/// Instead this system's own Update() diffs each AI player's <see cref="HTNComponent"/> against the last
/// tick's <see cref="AiTraceStateComponent"/> snapshot to infer plan/action transitions from the outside -
/// cheap (plain field comparisons on a handful of entities, no spatial queries) and touches nothing vanilla.
/// </summary>
public sealed partial class AiTraceSystem : EntitySystem
{
    /// <summary>
    /// Every trace event this system logs, also as a real queryable counter broken down by <c>kind</c> - spec
    /// Stabilization milestone stage 13/21's per-run report (goals stuck/failed, replans, interruptions,
    /// actions failed, LLM calls/failures) needs actual numbers, not log-scraping. GoalChanged isn't included
    /// here - <see cref="GoalSystem.GoalChangesMetric"/> already counts it from the side that decides it.
    /// GoalCompleted has no counter either: nothing calls it yet (see its own doc comment) - a "kind" that's
    /// always zero would be misleading rather than merely unused.
    /// </summary>
    public static readonly Counter TraceEventsMetric = Metrics.CreateCounter(
        "aiplayers_trace_events_total",
        "Total AI player trace events by kind (GoalStuck, GoalFailed, PlanInterrupted, Replanned, ActionFailed, LlmDecision, LlmFailure, etc).",
        new CounterConfiguration { LabelNames = new[] { "kind" } });

    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private BeliefSystem _belief = default!;

    /// <summary>AI Players 0.4 Milestone 12: how many consecutive identical action failures before it becomes
    /// a Belief instead of just another outcome memory - see <see cref="AiTraceStateComponent.ConsecutiveActionFailures"/>.</summary>
    public const int ConsecutiveFailuresBeforeBelief = 3;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("aiplayers.trace");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GoalComponent, HTNComponent, AiTraceStateComponent>();
        while (query.MoveNext(out var uid, out var goal, out var htn, out var state))
        {
            TracePlan(uid, goal, htn, state);
        }
    }

    private void TracePlan(EntityUid uid, GoalComponent goal, HTNComponent htn, AiTraceStateComponent state)
    {
        // Journeys report explicit results, including preemption. Do not also infer an outcome from
        // the same plan disappearing before the busy-state system consumes its queued observation.
        if (TryComp<AiBusyStateComponent>(uid, out var busy) && busy.JourneyTarget is not null)
        {
            state.LastPlan = null;
            state.LastPlanIndex = -1;
            state.LastOperatorName = null;
            state.LastPlanGoal = null;
            return;
        }

        var plan = htn.Plan;

        if (plan is null)
        {
            // Plan disappeared. Whether the goal changed - not how far the plan's task index had gotten - is
            // what tells a genuine interruption from a completion: reaching the last task's index only means
            // that operator started, not that it reported Finished, so a plan can still be cut short by an
            // external goal change while sitting on its very last step (e.g. mid-DoAfter). A same-goal plan
            // that ends before its last task is a real failure/abort instead; a same-goal plan that reaches
            // its last task is treated as a silent natural completion (no event - GoalSystem's own reasoning
            // for staying on the same goal already covers that case).
            if (state.LastPlan is { } previous)
            {
                if (state.LastPlanGoal != goal.CurrentGoal)
                {
                    PlanInterrupted(uid, state.LastPlanGoal ?? "Unknown", goal.CurrentGoal);
                    state.LastInterruptedGoal = state.LastPlanGoal;
                }
                else if (state.LastPlanIndex < previous.Tasks.Count - 1)
                {
                    ActionFailed(uid, state.LastOperatorName ?? "Unknown", "PlanAborted");
                }
            }

            state.LastPlan = null;
            state.LastPlanIndex = -1;
            state.LastOperatorName = null;
            state.LastPlanGoal = null;
            return;
        }

        if (!ReferenceEquals(plan, state.LastPlan))
        {
            var previousPlan = state.LastPlan;
            var previousGoal = state.LastPlanGoal;
            var pendingResume = state.LastInterruptedGoal;

            if (previousGoal == goal.CurrentGoal && previousPlan is not null)
            {
                Replanned(uid, goal.CurrentGoal, "new-plan-same-goal");
            }
            else if (pendingResume == goal.CurrentGoal)
            {
                PlanResumed(uid, goal.CurrentGoal);
                state.LastInterruptedGoal = null;
            }
            else
            {
                // HTNSystem.UpdateNPC can replace an in-progress plan with a better one for a different goal
                // in a single step (comp.Plan = comp.PlanningJob.Result) without ever setting Plan back to
                // null in between - the null-branch above only catches an interruption that's visible for at
                // least one whole tick. This is the same interruption, just caught here instead: the old plan
                // belonged to a different goal (previousGoal != goal.CurrentGoal is guaranteed by this being
                // the final else branch), so it counts as preempted regardless of how far its task index had
                // gotten - see the null-branch above for why index position alone can't tell "done" from "cut
                // short on the last step".
                if (previousPlan is not null)
                {
                    PlanInterrupted(uid, previousGoal ?? "Unknown", goal.CurrentGoal);
                    state.LastInterruptedGoal = previousGoal;
                }

                PlanStarted(uid, goal.CurrentGoal);
            }

            state.LastPlan = plan;
            // Force the operator-changed branch below to fire for the plan's first task too.
            state.LastPlanIndex = -1;
            state.LastPlanGoal = goal.CurrentGoal;
        }

        if (plan.Index == state.LastPlanIndex)
            return;

        if (state.LastOperatorName is { } finishedOperator)
            ActionCompleted(uid, finishedOperator);

        state.LastPlanIndex = plan.Index;
        state.LastOperatorName = plan.Index < plan.Tasks.Count ? plan.CurrentOperator.GetType().Name : null;

        if (state.LastOperatorName is { } startedOperator)
            ActionStarted(uid, startedOperator);
    }

    public void GoalChanged(EntityUid uid, string from, string to, string reason, float priority) =>
        _sawmill.Debug($"[AI:{ToPrettyString(uid)}] GoalChanged: {from} -> {to} (reason={reason}, priority={priority:0.00})");

    public void GoalCompleted(EntityUid uid, string goal, string reason) =>
        _sawmill.Info($"[AI:{ToPrettyString(uid)}] GoalCompleted: {goal} (reason={reason})");

    public void GoalFailed(EntityUid uid, string goal, string reason)
    {
        TraceEventsMetric.WithLabels("GoalFailed").Inc();
        _sawmill.Info($"[AI:{ToPrettyString(uid)}] GoalFailed: {goal} (reason={reason})");

        // AI Players 2.0 Milestone 1's feedback loop ("observe result -> adapt -> continue"): only for a
        // cognitive-mode AI player (HasComp is a safe check regardless of whether the entity even has a
        // MemoryComponent) - a legacy AI player never gets this side effect, since AiTraceSystem otherwise has
        // zero memory writes of its own.
        if (TryComp<CognitiveModeComponent>(uid, out var cognitive))
        {
            var content = $"Попытался (попыталась) {goal}, но не продвинулся (продвинулась) и бросил(а) это.";
            _memory.AddMemory(uid, content: content,
                importance: MemoryImportanceScorer.Score("outcome", emotionalWeight: -0.2f, content: content),
                source: "outcome", emotionalWeight: -0.2f);
            // AI Players 0.3: a failed goal is exactly the kind of outcome that should shorten the wait until
            // the AI reconsiders (spec's "current intent invalidated" trigger), same precedent as
            // DangerSystem forcing GoalComponent.ReconsiderAccumulator to 0 on a new attack.
            cognitive.ReflectionAccumulator = 0f;
        }
    }

    public void GoalStuck(EntityUid uid, string goal, string reason)
    {
        TraceEventsMetric.WithLabels("GoalStuck").Inc();
        _sawmill.Warning($"[AI:{ToPrettyString(uid)}] GoalStuck: {goal} (reason={reason})");
    }

    public void NeedChanged(EntityUid uid, string need)
    {
        TraceEventsMetric.WithLabels("NeedChanged").Inc();
        _sawmill.Debug($"[AI:{ToPrettyString(uid)}] NeedChanged: {need} (now urgent)");
    }

    public void NeedSatisfied(EntityUid uid, string need)
    {
        TraceEventsMetric.WithLabels("NeedSatisfied").Inc();
        _sawmill.Debug($"[AI:{ToPrettyString(uid)}] NeedSatisfied: {need}");
    }

    public void LlmDecision(EntityUid uid, string intent, float priority, string reason)
    {
        TraceEventsMetric.WithLabels("LlmDecision").Inc();
        _sawmill.Info($"[AI:{ToPrettyString(uid)}] LLMDecision: {intent} (priority={priority:0.00}, reason={reason})");
    }

    public void LlmFailure(EntityUid uid, string reason)
    {
        TraceEventsMetric.WithLabels("LlmFailure").Inc();
        _sawmill.Debug($"[AI:{ToPrettyString(uid)}] LLMFailure: {reason}");
    }

    private void PlanStarted(EntityUid uid, string goal)
    {
        TraceEventsMetric.WithLabels("PlanStarted").Inc();
        _sawmill.Debug($"[AI:{ToPrettyString(uid)}] PlanStarted: {goal}");
    }

    private void PlanInterrupted(EntityUid uid, string goal, string byGoal)
    {
        TraceEventsMetric.WithLabels("PlanInterrupted").Inc();
        _sawmill.Debug($"[AI:{ToPrettyString(uid)}] PlanInterrupted: {goal} (by={byGoal})");

        if (TryComp<CognitiveModeComponent>(uid, out var cognitive))
        {
            var content = $"Был(а) на середине {goal}, когда {byGoal} стало важнее.";
            _memory.AddMemory(uid, content: content,
                importance: MemoryImportanceScorer.Score("outcome", content: content),
                source: "outcome");
            cognitive.ReflectionAccumulator = 0f;
        }
    }

    private void PlanResumed(EntityUid uid, string goal)
    {
        TraceEventsMetric.WithLabels("PlanResumed").Inc();
        _sawmill.Debug($"[AI:{ToPrettyString(uid)}] PlanResumed: {goal}");
    }

    private void Replanned(EntityUid uid, string goal, string reason)
    {
        TraceEventsMetric.WithLabels("Replanned").Inc();
        _sawmill.Debug($"[AI:{ToPrettyString(uid)}] Replanned: {goal} (reason={reason})");
    }

    private void ActionStarted(EntityUid uid, string action)
    {
        TraceEventsMetric.WithLabels("ActionStarted").Inc();
        _sawmill.Debug($"[AI:{ToPrettyString(uid)}] ActionStarted: {action}");
    }

    private void ActionCompleted(EntityUid uid, string action)
    {
        TraceEventsMetric.WithLabels("ActionCompleted").Inc();
        _sawmill.Debug($"[AI:{ToPrettyString(uid)}] ActionCompleted: {action}");
    }

    /// <summary>
    /// A named action was attempted and failed. Called both by the internal HTN-plan diffing above (an
    /// operator aborted mid-plan) and by terminal action results, including CanDo/Do rejection.
    /// Returns whether the outcome was written to cognitive memory.
    /// </summary>
    public bool ActionFailed(EntityUid uid, string action, string reason)
    {
        TraceEventsMetric.WithLabels("ActionFailed").Inc();
        _sawmill.Info($"[AI:{ToPrettyString(uid)}] ActionFailed: {action} (reason={reason})");

        if (TryComp<CognitiveModeComponent>(uid, out var cognitive))
        {
            var content = $"Попытался (попыталась) выполнить {action}, но не вышло: {reason}.";
            var memory = _memory.AddMemory(uid, content: content,
                importance: MemoryImportanceScorer.Score("outcome", emotionalWeight: -0.1f, content: content),
                source: "outcome", emotionalWeight: -0.1f);
            cognitive.ReflectionAccumulator = 0f;

            // AI Players 0.4 Milestone 12: repeated failure of the exact same thing is a pattern worth
            // actually believing something about, not just another one-off outcome memory.
            if (TryComp<AiTraceStateComponent>(uid, out var state))
            {
                var key = $"{action}|{reason}";
                state.ConsecutiveActionFailures.TryGetValue(key, out var count);
                count++;
                state.ConsecutiveActionFailures[key] = count;

                if (count == ConsecutiveFailuresBeforeBelief)
                {
                    _belief.AddBelief(uid,
                        subject: action,
                        content: $"{action}, похоже, сейчас ненадёжно — не получилось одинаково уже {count} раза подряд ({reason}).",
                        confidence: 0.6f,
                        source: "repeated-failure");
                }
            }
            return memory is not null;
        }
        return false;
    }

    /// <summary>
    /// A cognitive decision's chosen action was successfully carried out via
    /// <see cref="AiActionRegistrySystem.TryDoAction"/> (AI Players 0.3 - prior to this milestone this was
    /// logged instead of the action actually running, with the literal placeholder name "reflect"; now
    /// <paramref name="actionName"/> is the real action that ran, e.g. "PursueGoal"/"ContinueActivity" - see
    /// <see cref="LlmGatewaySystem.TryApplyCognitiveDecision"/>).
    /// </summary>
    public void ActionProposed(EntityUid uid, string actionName, string reason)
    {
        TraceEventsMetric.WithLabels("ActionProposed").Inc();
        _sawmill.Info($"[AI:{ToPrettyString(uid)}] ActionProposed: {actionName} (reason={reason})");
    }

    /// <summary>
    /// AI Players 0.6, spec section 34: a richer trace specifically for a successful travel decision
    /// (<c>GoToKnownLocation</c>) - desire/intent/destination/reason together on one line, on top of the
    /// generic <see cref="ActionProposed"/> line every successful action already gets. Called only from
    /// <see cref="LlmGatewaySystem.TryApplyCognitiveDecision"/> when the resolved action is
    /// <c>GoToKnownLocation</c> - same "meaningful events only, never per-tick" discipline as every other trace
    /// call in this file.
    /// </summary>
    public void TravelDecided(EntityUid uid, string desire, string intention, string selectedLocation, string reason)
    {
        TraceEventsMetric.WithLabels("TravelDecided").Inc();
        _sawmill.Info(
            $"[AI:{ToPrettyString(uid)}] TravelDecided: {intention} (desire={desire}, destination={selectedLocation}, reason={reason})");
    }
}
