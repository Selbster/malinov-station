using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Perception;
using Content.Server._MalinovStation.AIPlayers.Prototypes;
using Content.Shared._MalinovStation.AIPlayers;
using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Decides *when* to ask the LLM for something and applies the (validated) result: goal decisions (applied
/// to <see cref="GoalComponent"/>) and, since Milestone 6, conversation lines (handed back to the caller via
/// callback - see <see cref="TryRequestLine"/>). LLM calls are event-driven, never every tick, and are
/// subject to a per-entity cooldown (goal decisions) and a single global concurrency budget shared across
/// both request kinds (spec sections 23/24). If the LLM is disabled, unreachable, slow, or returns garbage,
/// callers fall back to their own non-LLM behaviour - nothing here is required for basic operation.
/// </summary>
public sealed partial class LlmGatewaySystem : EntitySystem
{
    /// <summary>
    /// Total LLM requests kicked off (decisions + conversation lines combined - both draw from the same
    /// concurrency budget). Spec Milestone 1 section 20: divide by elapsed time/population externally
    /// (Prometheus rate()) to get "LLM calls/minute" - this just exposes the raw counter, same convention as
    /// <see cref="Content.Server.Database.ServerDbManager.DbReadOpsMetric"/>.
    /// </summary>
    public static readonly Counter LlmRequestsMetric = Metrics.CreateCounter(
        "aiplayers_llm_requests_total",
        "Total number of LLM requests (decisions + conversation lines) kicked off by the AI players LLM gateway.");

    /// <summary>
    /// Wall-clock time from kicking off a decision request to it completing (success, failure or timeout).
    /// Spec Milestone 1 section 20's "average decision latency" - sum/count of this histogram.
    /// </summary>
    public static readonly Histogram LlmDecisionLatencyMetric = Metrics.CreateHistogram(
        "aiplayers_llm_decision_latency_seconds",
        "Time from kicking off an LLM goal-decision request to it completing.",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.1, 2, 10) });

    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ILlmClient _client = default!;
    [Dependency] private ContextBuilderSystem _contextBuilder = default!;
    [Dependency] private AiLodSystem _lod = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logManager = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private AiTraceSystem _trace = default!;
    [Dependency] private GoalSystem _goal = default!;
    [Dependency] private AiActionRegistrySystem _actionRegistry = default!;

    private ISawmill _sawmill = default!;

    private bool _enabled;
    private bool _cognitiveEnabled;
    private float _decisionCooldownSeconds;
    private int _maxConcurrentRequests;
    private float _timeoutSeconds;

    private int _inFlight;
    private readonly Dictionary<EntityUid, (Task<LlmDecision?> Task, TimeSpan StartedAt)> _pending = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastRequestAt = new();
    private readonly List<EntityUid> _finishedBuffer = new();

    private readonly Dictionary<EntityUid, (Task<string?> Task, Action<EntityUid, string?> OnComplete)> _pendingLines = new();
    private readonly List<EntityUid> _finishedLinesBuffer = new();

    /// <summary>AI Players 0.4 Milestone 3: the hierarchical decision's first stage - shares
    /// <see cref="_lastRequestAt"/>/<see cref="_inFlight"/> with the legacy path rather than tracking a
    /// separate budget, since an entity is never both cognitive and legacy.</summary>
    private readonly Dictionary<EntityUid, (Task<LlmIntentDecision?> Task, TimeSpan StartedAt)> _pendingIntent = new();
    private readonly List<EntityUid> _finishedIntentBuffer = new();

    /// <summary>The hierarchical decision's second stage - only ever populated from within
    /// <see cref="HandleCompletedIntentRequest"/> once stage one resolves with a category that has a genuine
    /// choice among its eligible actions (see <see cref="TryRequestCognitiveDecision"/>'s doc comment for the
    /// single-eligible-action skip case that never reaches this dictionary at all). Carries the stage-one
    /// <see cref="LlmIntentDecision"/> forward so both results can be synthesized back into one
    /// <see cref="LlmCognitiveDecision"/> once stage two also resolves.</summary>
    private readonly Dictionary<EntityUid, (Task<LlmActionSelectionDecision?> Task, TimeSpan StartedAt, LlmIntentDecision Intent)> _pendingActionSelection = new();
    private readonly List<EntityUid> _finishedActionSelectionBuffer = new();

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("aiplayers.llm");

        SubscribeLocalEvent<AiPlayerMetNewCharacterEvent>(OnMetNewCharacter);

        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersLlmEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersCognitiveEnabled, v => _cognitiveEnabled = v, true);
        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersLlmDecisionCooldownSeconds, v => _decisionCooldownSeconds = v, true);
        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersLlmMaxConcurrentRequests, v => _maxConcurrentRequests = v, true);
        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersLlmTimeoutSeconds, v => _timeoutSeconds = v, true);
    }

    private void OnMetNewCharacter(ref AiPlayerMetNewCharacterEvent ev)
    {
        // AI Players 2.0 Milestone 1: a cognitive AI player gets the cognitive pipeline for every trigger
        // (this event + the periodic reflection scan in Update()); a legacy one keeps its single existing
        // event-driven trigger, completely unchanged.
        if (HasComp<CognitiveModeComponent>(ev.AiPlayer))
            TryRequestCognitiveDecision(ev.AiPlayer);
        else
            TryRequestDecision(ev.AiPlayer);
    }

    /// <summary>Every intent name a decision will actually be accepted for - the same combined whitelist
    /// <see cref="TryApplyDecision"/> checks against, told to the LLM up front so it never proposes something
    /// outside it (this used to silently omit professional-goal ids - see <see cref="PromptBuilder.BuildSystemPrompt"/>).</summary>
    private IReadOnlyCollection<string> GetAllowedIntents() =>
        AIGoals.All.Concat(_proto.EnumeratePrototypes<AiProfessionalGoalPrototype>().Select(p => p.ID)).ToList();

    /// <summary>
    /// Whether an LLM request for this entity is currently in flight. Mainly useful for tests/debug tooling
    /// to wait out an async request without reaching into private state.
    /// </summary>
    public bool HasPendingRequest(EntityUid uid) => _pending.ContainsKey(uid);

    /// <summary>
    /// Attempts to kick off an async LLM decision request for <paramref name="uid"/>. Returns false (no-op)
    /// if the gateway is disabled, the entity already has a request in flight, its cooldown hasn't elapsed,
    /// the global concurrency budget is exhausted, it's a Background-tier AI player nobody can currently see
    /// (spec Milestone 8's LLM budgeting - that budget goes to AI players someone might actually notice), or
    /// it isn't a valid AI player.
    /// </summary>
    public bool TryRequestDecision(EntityUid uid)
    {
        if (!_enabled)
            return false;

        if (_lod.IsBackground(uid))
            return false;

        if (_pending.ContainsKey(uid))
            return false;

        if (_lastRequestAt.TryGetValue(uid, out var last) &&
            _timing.CurTime - last < TimeSpan.FromSeconds(_decisionCooldownSeconds))
        {
            return false;
        }

        if (_inFlight >= _maxConcurrentRequests)
            return false;

        var context = _contextBuilder.BuildContext(uid);
        if (context is null)
            return false;

        _lastRequestAt[uid] = _timing.CurTime;
        _inFlight++;
        LlmRequestsMetric.Inc();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        _pending[uid] = (_client.DecideAsync(context, GetAllowedIntents(), cts.Token), _timing.CurTime);
        return true;
    }

    /// <summary>
    /// Whether a cognitive decision (either hierarchical stage) is currently in flight for this entity.
    /// </summary>
    public bool HasPendingCognitiveRequest(EntityUid uid) => _pendingIntent.ContainsKey(uid) || _pendingActionSelection.ContainsKey(uid);

    /// <summary>
    /// AI Players 0.4 Milestone 3: kicks off the hierarchical decision's first stage. Same gating as before
    /// (disabled, Background LOD, already-pending, cooldown, concurrency budget, cognitive-mode master
    /// switch), using a <see cref="CognitiveState"/> instead of the narrower <see cref="AiContext"/>. Shares
    /// the same <see cref="_lastRequestAt"/> cooldown dict and <see cref="_inFlight"/> budget as the legacy
    /// path - an entity is never both cognitive and legacy. A genuine second HTTP call (Action Selection) may
    /// follow once this stage resolves - see <see cref="HandleCompletedIntentRequest"/> - but that
    /// continuation reuses this same in-flight decision rather than re-entering this method's gating, so it
    /// can never be blocked by this entity's own cooldown mid-decision.
    /// </summary>
    public bool TryRequestCognitiveDecision(EntityUid uid)
    {
        if (!_enabled || !_cognitiveEnabled)
            return false;

        if (_lod.IsBackground(uid))
            return false;

        if (HasPendingCognitiveRequest(uid))
            return false;

        if (_lastRequestAt.TryGetValue(uid, out var last) &&
            _timing.CurTime - last < TimeSpan.FromSeconds(_decisionCooldownSeconds))
        {
            return false;
        }

        if (_inFlight >= _maxConcurrentRequests)
            return false;

        var context = _contextBuilder.BuildCognitiveState(uid);
        if (context is null)
            return false;

        var eligibleCategories = _actionRegistry.GetEligibleCategories(uid);

        _lastRequestAt[uid] = _timing.CurTime;
        _inFlight++;
        LlmRequestsMetric.Inc();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        _pendingIntent[uid] = (_client.DecideIntentAsync(context, GetAllowedIntents(), eligibleCategories, cts.Token), _timing.CurTime);
        return true;
    }

    /// <summary>
    /// Whether an LLM line request for this entity is currently in flight.
    /// </summary>
    public bool HasPendingLineRequest(EntityUid uid) => _pendingLines.ContainsKey(uid);

    /// <summary>
    /// Attempts to kick off an async LLM conversation-line request for <paramref name="uid"/>, invoking
    /// <paramref name="onComplete"/> with the generated line (or null on any failure) once it resolves.
    /// Returns false immediately (no callback will fire) if the gateway is disabled, the entity already has
    /// a line request in flight, or the global concurrency budget is exhausted - callers (see
    /// <see cref="SocialSystem"/>) are expected to fall back to a non-LLM line in that case, same as
    /// goal decisions fall back to the ordinary Goal System.
    /// </summary>
    public bool TryRequestLine(EntityUid uid, DialogueContext context, Action<EntityUid, string?> onComplete)
    {
        if (!_enabled)
            return false;

        if (_lod.IsBackground(uid))
            return false;

        if (_pendingLines.ContainsKey(uid))
            return false;

        if (_inFlight >= _maxConcurrentRequests)
            return false;

        _inFlight++;
        LlmRequestsMetric.Inc();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        var task = _client.GenerateLineAsync(context, cts.Token);
        _pendingLines[uid] = (task, onComplete);
        return true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        UpdatePendingDecisions();
        UpdatePendingIntentDecisions();
        UpdatePendingActionSelections();
        UpdatePendingLines();
        UpdateReflectionTriggers(frameTime);
    }

    /// <summary>
    /// AI Players 2.0 Milestone 1 (spec section 32's "periodic thought interval"): the only trigger a
    /// cognitive AI player had until now was "just met someone new" (<see cref="OnMetNewCharacter"/>) - this
    /// gives it a standing heartbeat too, LOD-scaled the same way <see cref="GoalSystem"/>'s own reconsider
    /// cadence is. <see cref="TryRequestCognitiveDecision"/> already enforces the same cooldown/budget checks
    /// as every other trigger, so this can never blow the budget - worst case it's a harmless same-tick no-op.
    /// </summary>
    private void UpdateReflectionTriggers(float frameTime)
    {
        var query = EntityQueryEnumerator<CognitiveModeComponent>();
        while (query.MoveNext(out var uid, out var cognitive))
        {
            cognitive.ReflectionAccumulator -= frameTime;
            if (cognitive.ReflectionAccumulator > 0f)
                continue;

            cognitive.ReflectionAccumulator = cognitive.ReflectionCooldown * _lod.GetMultiplier(uid);
            TryRequestCognitiveDecision(uid);
        }
    }

    private void UpdatePendingDecisions()
    {
        if (_pending.Count == 0)
            return;

        _finishedBuffer.Clear();
        foreach (var (uid, entry) in _pending)
        {
            if (entry.Task.IsCompleted)
                _finishedBuffer.Add(uid);
        }

        foreach (var uid in _finishedBuffer)
        {
            var entry = _pending[uid];
            _pending.Remove(uid);
            _inFlight--;

            LlmDecisionLatencyMetric.Observe((_timing.CurTime - entry.StartedAt).TotalSeconds);
            HandleCompletedRequest(uid, entry.Task);
        }
    }

    private void UpdatePendingIntentDecisions()
    {
        if (_pendingIntent.Count == 0)
            return;

        _finishedIntentBuffer.Clear();
        foreach (var (uid, entry) in _pendingIntent)
        {
            if (entry.Task.IsCompleted)
                _finishedIntentBuffer.Add(uid);
        }

        foreach (var uid in _finishedIntentBuffer)
        {
            var entry = _pendingIntent[uid];
            _pendingIntent.Remove(uid);
            _inFlight--;

            LlmDecisionLatencyMetric.Observe((_timing.CurTime - entry.StartedAt).TotalSeconds);
            HandleCompletedIntentRequest(uid, entry.Task);
        }
    }

    /// <summary>
    /// AI Players 0.4 Milestone 3: the hierarchical decision's first stage resolved. On any failure, this is
    /// exactly the same graceful failure the old single-call path had - no state changed, nothing left
    /// pending. On success: writes <see cref="IntentComponent"/> immediately and unconditionally (the same
    /// "the AI still wanted it even if the concrete action then fails" invariant <see cref="IntentComponent"/>'s
    /// own doc comment establishes, now extended one step earlier - the AI can "want" something even before
    /// it's figured out which concrete action gets there). Then either applies a synthesized decision directly
    /// (the sole-eligible-action skip case - no second HTTP call for a choice that was never real, spec
    /// Milestone 2's "use LLM reasoning only when the question is genuinely cognitive") or kicks off stage two.
    /// </summary>
    private void HandleCompletedIntentRequest(EntityUid uid, Task<LlmIntentDecision?> task)
    {
        if (task.IsFaulted)
        {
            _sawmill.Warning($"LLM intent request for {ToPrettyString(uid)} threw: {task.Exception?.GetBaseException().Message}");
            _trace.LlmFailure(uid, "RequestThrew");
            return;
        }

        if (task.IsCanceled)
        {
            _sawmill.Debug($"LLM intent request for {ToPrettyString(uid)} timed out or was cancelled.");
            _trace.LlmFailure(uid, "TimeoutOrCancelled");
            return;
        }

        // Safe: Update() only calls this once task.IsCompleted is true, and IsFaulted/IsCanceled are
        // excluded above, so the task has already run to completion - this cannot block.
#pragma warning disable RA0004
        var result = task.Result;
#pragma warning restore RA0004

        if (result is not { } intent)
        {
            _sawmill.Debug($"LLM returned no usable intent decision for {ToPrettyString(uid)}.");
            _trace.LlmFailure(uid, "NoUsableDecision");
            return;
        }

        if (Deleted(uid) || !TryComp<IntentComponent>(uid, out var intentComp))
            return;

        intentComp.Name = intent.Intention;
        intentComp.Priority = Math.Clamp(intent.Priority, 0f, 1f);
        intentComp.Confidence = Math.Clamp(intent.Confidence, 0f, 1f);
        intentComp.DesireServed = intent.Desire;
        intentComp.ChosenAt = _timing.CurTime;

        var eligibleInCategory = _actionRegistry.GetEligibleActions(uid).Where(a => a.Category == intent.Category).ToList();

        if (eligibleInCategory.Count == 0)
        {
            _sawmill.Warning($"LLM proposed category \"{intent.Category}\" for {ToPrettyString(uid)}, which has no eligible actions right now.");
            _trace.LlmFailure(uid, "NoEligibleActionsInCategory");
            return;
        }

        if (eligibleInCategory.Count == 1 && eligibleInCategory[0].Name == ContinueActivityAction.ActionName)
        {
            TryApplyCognitiveDecision(uid, new LlmCognitiveDecision(
                intent.Desire, intent.Intention, intent.Priority, intent.Confidence, intent.Reason,
                ContinueActivityAction.ActionName, new Dictionary<string, string>()));
            return;
        }

        var context = _contextBuilder.BuildCognitiveState(uid);
        if (context is null)
            return;

        _inFlight++;
        LlmRequestsMetric.Inc();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        _pendingActionSelection[uid] = (_client.SelectActionAsync(context, intent, eligibleInCategory, cts.Token), _timing.CurTime, intent);
    }

    private void UpdatePendingActionSelections()
    {
        if (_pendingActionSelection.Count == 0)
            return;

        _finishedActionSelectionBuffer.Clear();
        foreach (var (uid, entry) in _pendingActionSelection)
        {
            if (entry.Task.IsCompleted)
                _finishedActionSelectionBuffer.Add(uid);
        }

        foreach (var uid in _finishedActionSelectionBuffer)
        {
            var entry = _pendingActionSelection[uid];
            _pendingActionSelection.Remove(uid);
            _inFlight--;

            LlmDecisionLatencyMetric.Observe((_timing.CurTime - entry.StartedAt).TotalSeconds);
            HandleCompletedActionSelectionRequest(uid, entry.Task, entry.Intent);
        }
    }

    /// <summary>
    /// AI Players 0.4 Milestone 3: the hierarchical decision's second stage resolved. Intent was already
    /// written when stage one completed (see <see cref="HandleCompletedIntentRequest"/>) - a failure here
    /// only means the concrete action never got chosen/executed, exactly like a failed proposal in the old
    /// single-call path.
    /// </summary>
    private void HandleCompletedActionSelectionRequest(EntityUid uid, Task<LlmActionSelectionDecision?> task, LlmIntentDecision intent)
    {
        if (task.IsFaulted)
        {
            _sawmill.Warning($"LLM action selection request for {ToPrettyString(uid)} threw: {task.Exception?.GetBaseException().Message}");
            _trace.LlmFailure(uid, "RequestThrew");
            return;
        }

        if (task.IsCanceled)
        {
            _sawmill.Debug($"LLM action selection request for {ToPrettyString(uid)} timed out or was cancelled.");
            _trace.LlmFailure(uid, "TimeoutOrCancelled");
            return;
        }

#pragma warning disable RA0004
        var result = task.Result;
#pragma warning restore RA0004

        if (result is not { } selection)
        {
            _sawmill.Debug($"LLM returned no usable action selection for {ToPrettyString(uid)}.");
            _trace.LlmFailure(uid, "NoUsableDecision");
            return;
        }

        // Re-checked fresh rather than trusting the eligible list stage two was originally offered - the same
        // "CanDo/Do never trust an earlier scan" convention every IAiAction already follows, since eligibility
        // can genuinely change during the round-trip (e.g. someone else picked up the same item).
        if (!_actionRegistry.GetEligibleActions(uid).Any(a => a.Name == selection.Action && a.Category == intent.Category))
        {
            _sawmill.Warning($"LLM selected action \"{selection.Action}\" for {ToPrettyString(uid)}, which wasn't among the eligible actions it was offered.");
            _trace.LlmFailure(uid, "ActionNotEligible");
            return;
        }

        TryApplyCognitiveDecision(uid, new LlmCognitiveDecision(
            intent.Desire, intent.Intention, intent.Priority, intent.Confidence, selection.Reason,
            selection.Action, selection.ActionParameters));
    }

    private void UpdatePendingLines()
    {
        if (_pendingLines.Count == 0)
            return;

        _finishedLinesBuffer.Clear();
        foreach (var (uid, entry) in _pendingLines)
        {
            if (entry.Task.IsCompleted)
                _finishedLinesBuffer.Add(uid);
        }

        foreach (var uid in _finishedLinesBuffer)
        {
            var (task, onComplete) = _pendingLines[uid];
            _pendingLines.Remove(uid);
            _inFlight--;

            string? result = null;
            if (task.IsFaulted)
            {
                _sawmill.Warning($"LLM line request for {ToPrettyString(uid)} threw: {task.Exception?.GetBaseException().Message}");
            }
            else if (task.IsCanceled)
            {
                _sawmill.Debug($"LLM line request for {ToPrettyString(uid)} timed out or was cancelled.");
            }
            else
            {
                // Safe: only reached once task.IsCompleted is true and IsFaulted/IsCanceled are excluded,
                // so the task has already run to completion - this cannot block.
#pragma warning disable RA0004
                result = task.Result;
#pragma warning restore RA0004
            }

            onComplete(uid, result);
        }
    }

    private void HandleCompletedRequest(EntityUid uid, Task<LlmDecision?> task)
    {
        if (task.IsFaulted)
        {
            _sawmill.Warning($"LLM request for {ToPrettyString(uid)} threw: {task.Exception?.GetBaseException().Message}");
            _trace.LlmFailure(uid, "RequestThrew");
            return;
        }

        if (task.IsCanceled)
        {
            _sawmill.Debug($"LLM request for {ToPrettyString(uid)} timed out or was cancelled.");
            _trace.LlmFailure(uid, "TimeoutOrCancelled");
            return;
        }

        // Safe: Update() only calls this once task.IsCompleted is true, and IsFaulted/IsCanceled are
        // excluded above, so the task has already run to completion - this cannot block.
#pragma warning disable RA0004
        var result = task.Result;
#pragma warning restore RA0004

        if (result is not { } decision)
        {
            _sawmill.Debug($"LLM returned no usable decision for {ToPrettyString(uid)}.");
            _trace.LlmFailure(uid, "NoUsableDecision");
            return;
        }

        TryApplyDecision(uid, decision);
    }

    /// <summary>
    /// Validates and applies an LLM decision to the entity's <see cref="GoalComponent"/>. Public (and
    /// separate from the async plumbing above) so it can be exercised directly in tests without a real
    /// network round-trip. Thin wrapper around <see cref="GoalSystem.TrySetExternalGoal"/> - the actual
    /// whitelist/clamp/write logic lives there now (AI Players 0.3), shared with the cognitive
    /// <c>PursueGoal</c> action, but this method's own external contract (reason prefixed with "llm: ",
    /// <see cref="AiTraceSystem.LlmDecision"/>/<see cref="AiTraceSystem.LlmFailure"/> tracing) is unchanged.
    /// </summary>
    public bool TryApplyDecision(EntityUid uid, LlmDecision decision)
    {
        var priority = Math.Clamp(decision.Priority, 0f, 1f);

        if (!_goal.TrySetExternalGoal(uid, decision.Intent, decision.Priority, $"llm: {decision.Reason}", out _))
        {
            _sawmill.Warning($"LLM proposed an unknown intent \"{decision.Intent}\" for {ToPrettyString(uid)}; ignoring.");
            _trace.LlmFailure(uid, "UnknownIntent");
            return false;
        }

        _trace.LlmDecision(uid, decision.Intent, priority, decision.Reason);
        return true;
    }

    /// <summary>
    /// AI Players 0.3: validates and applies a cognitive decision. Unlike the legacy path, this no longer
    /// forces the decision through <see cref="TryApplyDecision"/>/<see cref="GoalComponent"/> at all - Intent
    /// and Goal are now independent writes (see <see cref="IntentComponent"/>'s own doc comment). Steps:
    /// (1) write <see cref="IntentComponent"/> unconditionally - the AI's self-reported intent persists
    /// regardless of whether the concrete action attempt below succeeds; (2) resolve the raw proposed
    /// action/parameters into a validated <see cref="ActionProposal"/> (structural validation - known action
    /// name, required parameters present); (3) AI Players 0.5: confirm the resolved action is still one of
    /// <see cref="AiActionRegistrySystem.GetEligibleActions"/> - previously only the two real production
    /// callers (<see cref="HandleCompletedIntentRequest"/>'s skip case, <see cref="HandleCompletedActionSelectionRequest"/>)
    /// happened to check this before calling in; this makes it a property of the boundary itself, so no future
    /// caller (this method is public) can apply a decision the eligibility pre-filter never actually offered;
    /// (4) run it through <see cref="AiActionRegistrySystem.TryDoAction"/> (semantic validation - e.g. an
    /// unknown goal name inside <c>PursueGoal</c> - happens in the action's own CanDo). A failure at (2), (3)
    /// or (4) is real feedback, not a silent drop: (2)/(3) mean the LLM's output itself was malformed or stale
    /// (<see cref="AiTraceSystem.LlmFailure"/>, no memory write); (4) means the AI proposed something
    /// well-formed and eligible but wrong (<see cref="AiTraceSystem.ActionFailed"/>, which writes an outcome
    /// memory and forces a fast re-reflection).
    /// </summary>
    public bool TryApplyCognitiveDecision(EntityUid uid, LlmCognitiveDecision decision)
    {
        if (Deleted(uid) || !TryComp<IntentComponent>(uid, out var intent))
            return false;

        intent.Name = decision.Intention;
        intent.Priority = Math.Clamp(decision.Priority, 0f, 1f);
        intent.Confidence = Math.Clamp(decision.Confidence, 0f, 1f);
        intent.DesireServed = decision.Desire;
        intent.ChosenAt = _timing.CurTime;

        if (!ActionProposalResolver.TryResolve(decision, out var proposal, out var resolveFailReason))
        {
            _sawmill.Warning($"LLM proposed an invalid action for {ToPrettyString(uid)}: {resolveFailReason}");
            _trace.LlmFailure(uid, "InvalidActionProposal");
            return false;
        }

        if (!_actionRegistry.GetEligibleActions(uid).Any(a => a.Name == proposal.ActionName))
        {
            _sawmill.Warning($"LLM proposed action \"{proposal.ActionName}\" for {ToPrettyString(uid)}, which isn't currently eligible.");
            _trace.LlmFailure(uid, "ActionNotEligible");
            return false;
        }

        if (!_actionRegistry.TryDoAction(uid, proposal.ActionName, proposal.Parameters, decision.Reason, out var doFailReason))
        {
            _trace.ActionFailed(uid, proposal.ActionName, doFailReason);
            return false;
        }

        _trace.ActionProposed(uid, proposal.ActionName, decision.Reason);

        // AI Players 0.6, spec section 34: a richer trace specifically for travel decisions, on top of the
        // generic ActionProposed line above.
        if (proposal is { ActionName: GoToKnownLocationAction.ActionName, Parameters: GoToKnownLocationActionParams goTo })
            _trace.TravelDecided(uid, decision.Desire, decision.Intention, goTo.LocationHint, decision.Reason);

        return true;
    }
}
