using System.Linq;
using System.Text;
using Content.Server._MalinovStation.AIPlayers.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// AI Players 0.6.3, spec section 3: the end-to-end decision trace.
///
/// Kept in its own partial rather than added to <see cref="AiTraceSystem"/>'s existing body because it works
/// differently to everything else there: the other methods log an event and are done, whereas these accumulate
/// one decision's stages on <see cref="AiDecisionTraceComponent"/> and emit a single block at the end. Every
/// method here is a no-op for an entity without that component, so nothing needs to check first.
///
/// Stage count is bounded by the number of real transitions in the chain, so spec section 3's "do not log
/// every movement tick" holds by construction rather than by a rate limit.
/// </summary>
public sealed partial class AiTraceSystem
{
    [Dependency] private IGameTiming _traceTiming = default!;

    /// <summary>
    /// Starts recording a new decision, closing out any previous one that never reached a terminal stage -
    /// an abandoned decision is itself a finding, and emitting it is how a silent dead end becomes visible.
    /// </summary>
    public void DecisionStarted(EntityUid uid, float boredom, float curiosity, string? topDesire)
    {
        if (!TryComp<AiDecisionTraceComponent>(uid, out var trace))
            return;

        if (trace.Current is { Finished: false } previous)
            Emit(uid, previous, DescribeUnfinished(previous));

        trace.DecisionId++;
        trace.Current = new AiDecisionTrace
        {
            Id = trace.DecisionId,
            StartedAt = _traceTiming.CurTime,
            Boredom = boredom,
            Curiosity = curiosity,
            TopDesire = topDesire,
        };
    }

    public void DecisionIntent(EntityUid uid, string intent, string category, IEnumerable<string> eligibleActions)
    {
        if (Current(uid) is not { } trace)
            return;

        trace.Intent = intent;
        trace.Category = category;
        trace.EligibleActions = string.Join(", ", eligibleActions);
    }

    public void DecisionAction(EntityUid uid, string action)
    {
        if (Current(uid) is { } trace)
            trace.SelectedAction = action;
    }

    public void DecisionTarget(EntityUid uid, string target, EntityCoordinates destination)
    {
        if (Current(uid) is not { } trace)
            return;

        trace.ExplorationTarget = target;
        trace.NavigationTarget = destination.ToString();
    }

    /// <summary>Movement genuinely began - the point past which the AI is doing something in the world rather
    /// than merely having decided to. Emits the block: everything after this is consequence, not decision.</summary>
    public void DecisionMovementStarted(EntityUid uid)
    {
        if (Current(uid) is not { } trace || trace.MovementStarted)
            return;

        trace.MovementStarted = true;
        Emit(uid, trace, "пошёл");
    }

    public void DecisionDiscovery(EntityUid uid, string place)
    {
        // Discovery legitimately arrives after the block was emitted - movement starts, the block prints, and
        // the AI only reaches the new area seconds later. So this writes to the last decision too, keeping
        // aiplayer_debug's view complete even though the log line has already gone out.
        if (!TryComp<AiDecisionTraceComponent>(uid, out var comp) || (comp.Current ?? comp.Last) is not { } trace)
            return;

        trace.Discovery = place;
        trace.LocationKnowledgeUpdated = true;
    }

    public void DecisionFeedback(EntityUid uid, string feedback)
    {
        if (Current(uid) is not { } trace)
            return;

        trace.Feedback = feedback;
        Emit(uid, trace, "не вышло");
    }

    public void DecisionReevaluation(EntityUid uid)
    {
        if (TryComp<AiDecisionTraceComponent>(uid, out var trace) && (trace.Current ?? trace.Last) is { } record)
            record.Reevaluation = true;
    }

    /// <summary>The decision currently being recorded, or null if there is none in flight or it already ended.
    /// Stages arriving after the block was emitted are dropped rather than reopening it.</summary>
    private AiDecisionTrace? Current(EntityUid uid) =>
        TryComp<AiDecisionTraceComponent>(uid, out var trace) && trace.Current is { Finished: false } current
            ? current
            : null;

    /// <summary>
    /// Writes the whole decision out as one block, in the order spec section 3 lists the stages. A stage that
    /// never happened is printed as "-" rather than omitted: the gaps are the point, and a missing line would
    /// be much easier to overlook than an explicitly empty one.
    /// </summary>
    private void Emit(EntityUid uid, AiDecisionTrace trace, string outcome)
    {
        trace.Finished = true;

        if (TryComp<AiDecisionTraceComponent>(uid, out var comp))
            comp.Last = trace;

        TraceEventsMetric.WithLabels("Decision").Inc();

        var sb = new StringBuilder();
        sb.AppendLine($"[AI:{ToPrettyString(uid)}] решение #{trace.Id} — {outcome}");
        sb.AppendLine($"  скука={trace.Boredom:0.00} любопытство={trace.Curiosity:0.00} желание={Or(trace.TopDesire)}");
        sb.AppendLine($"  намерение={Or(trace.Intent)} категория={Or(trace.Category)}");
        sb.AppendLine($"  пригодные={Or(trace.EligibleActions)}");
        sb.AppendLine($"  выбрано={Or(trace.SelectedAction)}");
        sb.AppendLine($"  цель={Or(trace.ExplorationTarget)} точка={Or(trace.NavigationTarget)}");
        sb.AppendLine($"  движение={(trace.MovementStarted ? "началось" : "-")} открыто={Or(trace.Discovery)} знание обновлено={(trace.LocationKnowledgeUpdated ? "да" : "-")}");
        sb.Append($"  обратная связь={Or(trace.Feedback)} переосмысление={(trace.Reevaluation ? "да" : "-")}");

        _sawmill.Info(sb.ToString());
    }

    /// <summary>
    /// How a decision that never reached a terminal stage of its own should be described when the next one
    /// closes it out.
    ///
    /// Not every such decision is broken, and saying so indiscriminately was actively misleading: an action
    /// like <c>ContinueActivity</c> or <c>Rest</c> finishes correctly without ever moving the AI anywhere, yet
    /// the block still announced that the decision "never reached an action" directly above a line naming the
    /// action it had chosen. So the wording is derived from how far the chain actually got, which keeps a
    /// genuine dead end - a route picked but never walked, a category that produced no choice - distinguishable
    /// at a glance from an ordinary stationary decision.
    /// </summary>
    private static string DescribeUnfinished(AiDecisionTrace trace)
    {
        if (string.IsNullOrWhiteSpace(trace.SelectedAction))
        {
            return string.IsNullOrWhiteSpace(trace.Intent)
                ? "оборвалось: модель не дала намерения"
                : "оборвалось: действие так и не выбрано";
        }

        if (!string.IsNullOrWhiteSpace(trace.NavigationTarget))
            return "маршрут выбран, но движение так и не началось";

        return "выполнено без перемещения";
    }

    private static string Or(string? value) => string.IsNullOrWhiteSpace(value) ? "-" : value;

    /// <summary>
    /// AI Players 0.6.3: the last decision, rendered for <c>aiplayer_debug</c>. Same content as the logged
    /// block, so what an admin reads live and what ends up in the log cannot drift apart.
    /// </summary>
    public string? DescribeLastDecision(EntityUid uid)
    {
        if (!TryComp<AiDecisionTraceComponent>(uid, out var comp) || (comp.Current ?? comp.Last) is not { } trace)
            return null;

        var age = (_traceTiming.CurTime - trace.StartedAt).TotalSeconds;

        return $"Последнее решение #{trace.Id} ({age:0} с назад){(trace.Finished ? string.Empty : ", ещё идёт")}:\n" +
            $"  скука={trace.Boredom:0.00} любопытство={trace.Curiosity:0.00} желание={Or(trace.TopDesire)}\n" +
            $"  намерение={Or(trace.Intent)} категория={Or(trace.Category)}\n" +
            $"  пригодные={Or(trace.EligibleActions)}\n" +
            $"  выбрано={Or(trace.SelectedAction)}\n" +
            $"  цель={Or(trace.ExplorationTarget)} точка={Or(trace.NavigationTarget)}\n" +
            $"  движение={(trace.MovementStarted ? "началось" : "-")} открыто={Or(trace.Discovery)} знание обновлено={(trace.LocationKnowledgeUpdated ? "да" : "-")}\n" +
            $"  обратная связь={Or(trace.Feedback)} переосмысление={(trace.Reevaluation ? "да" : "-")}";
    }
}
