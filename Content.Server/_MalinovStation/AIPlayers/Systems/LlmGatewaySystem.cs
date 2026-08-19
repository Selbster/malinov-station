using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

    /// <summary>AI Players 2.0 Milestone 1: same shape as <see cref="_pending"/>, but for cognitive decisions
    /// - shares <see cref="_lastRequestAt"/>/<see cref="_inFlight"/> with the legacy path rather than
    /// tracking a second budget, since an entity is never both cognitive and legacy.</summary>
    private readonly Dictionary<EntityUid, (Task<LlmCognitiveDecision?> Task, TimeSpan StartedAt)> _pendingCognitive = new();
    private readonly List<EntityUid> _finishedCognitiveBuffer = new();

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
    /// Whether a cognitive decision request for this entity is currently in flight.
    /// </summary>
    public bool HasPendingCognitiveRequest(EntityUid uid) => _pendingCognitive.ContainsKey(uid);

    /// <summary>
    /// AI Players 2.0 Milestone 1: same gating as <see cref="TryRequestDecision"/> (disabled, Background LOD,
    /// already-pending, cooldown, concurrency budget), plus the cognitive-mode master switch, using a
    /// <see cref="CognitiveState"/> instead of the narrower <see cref="AiContext"/>. Shares the same
    /// <see cref="_lastRequestAt"/> cooldown dict and <see cref="_inFlight"/> budget as the legacy path - an
    /// entity is never both cognitive and legacy, so there's no real collision, and this keeps the whole
    /// gateway under one shared concurrency cap rather than two independent ones that could double-spend it.
    /// </summary>
    public bool TryRequestCognitiveDecision(EntityUid uid)
    {
        if (!_enabled || !_cognitiveEnabled)
            return false;

        if (_lod.IsBackground(uid))
            return false;

        if (_pendingCognitive.ContainsKey(uid))
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

        _lastRequestAt[uid] = _timing.CurTime;
        _inFlight++;
        LlmRequestsMetric.Inc();

        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(_timeoutSeconds));
        _pendingCognitive[uid] = (_client.DecideCognitiveAsync(context, GetAllowedIntents(), cts.Token), _timing.CurTime);
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
        UpdatePendingCognitiveDecisions();
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

    private void UpdatePendingCognitiveDecisions()
    {
        if (_pendingCognitive.Count == 0)
            return;

        _finishedCognitiveBuffer.Clear();
        foreach (var (uid, entry) in _pendingCognitive)
        {
            if (entry.Task.IsCompleted)
                _finishedCognitiveBuffer.Add(uid);
        }

        foreach (var uid in _finishedCognitiveBuffer)
        {
            var entry = _pendingCognitive[uid];
            _pendingCognitive.Remove(uid);
            _inFlight--;

            LlmDecisionLatencyMetric.Observe((_timing.CurTime - entry.StartedAt).TotalSeconds);
            HandleCompletedCognitiveRequest(uid, entry.Task);
        }
    }

    private void HandleCompletedCognitiveRequest(EntityUid uid, Task<LlmCognitiveDecision?> task)
    {
        if (task.IsFaulted)
        {
            _sawmill.Warning($"LLM cognitive request for {ToPrettyString(uid)} threw: {task.Exception?.GetBaseException().Message}");
            _trace.LlmFailure(uid, "RequestThrew");
            return;
        }

        if (task.IsCanceled)
        {
            _sawmill.Debug($"LLM cognitive request for {ToPrettyString(uid)} timed out or was cancelled.");
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
            _sawmill.Debug($"LLM returned no usable cognitive decision for {ToPrettyString(uid)}.");
            _trace.LlmFailure(uid, "NoUsableDecision");
            return;
        }

        TryApplyCognitiveDecision(uid, decision);
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
    /// name, required parameters present); (3) run it through <see cref="AiActionRegistrySystem.TryDoAction"/>
    /// (semantic validation - e.g. an unknown goal name inside <c>PursueGoal</c> - happens in the action's own
    /// CanDo). A failure at either (2) or (3) is real feedback, not a silent drop: (2) means the LLM's output
    /// itself was malformed (<see cref="AiTraceSystem.LlmFailure"/>, no memory write); (3) means the AI
    /// proposed something well-formed but wrong (<see cref="AiTraceSystem.ActionFailed"/>, which writes an
    /// outcome memory and forces a fast re-reflection).
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

        if (!_actionRegistry.TryDoAction(uid, proposal.ActionName, proposal.Parameters, out var doFailReason))
        {
            _trace.ActionFailed(uid, proposal.ActionName, doFailReason);
            return false;
        }

        _trace.ActionProposed(uid, proposal.ActionName, decision.Reason);
        return true;
    }
}
