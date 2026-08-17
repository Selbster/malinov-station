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

    private ISawmill _sawmill = default!;

    private bool _enabled;
    private float _decisionCooldownSeconds;
    private int _maxConcurrentRequests;
    private float _timeoutSeconds;
    private float _overrideDurationSeconds;

    private int _inFlight;
    private readonly Dictionary<EntityUid, (Task<LlmDecision?> Task, TimeSpan StartedAt)> _pending = new();
    private readonly Dictionary<EntityUid, TimeSpan> _lastRequestAt = new();
    private readonly List<EntityUid> _finishedBuffer = new();

    private readonly Dictionary<EntityUid, (Task<string?> Task, Action<EntityUid, string?> OnComplete)> _pendingLines = new();
    private readonly List<EntityUid> _finishedLinesBuffer = new();

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("aiplayers.llm");

        SubscribeLocalEvent<AiPlayerMetNewCharacterEvent>(OnMetNewCharacter);

        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersLlmEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersLlmDecisionCooldownSeconds, v => _decisionCooldownSeconds = v, true);
        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersLlmMaxConcurrentRequests, v => _maxConcurrentRequests = v, true);
        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersLlmTimeoutSeconds, v => _timeoutSeconds = v, true);
        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersLlmOverrideDurationSeconds, v => _overrideDurationSeconds = v, true);
    }

    private void OnMetNewCharacter(ref AiPlayerMetNewCharacterEvent ev)
    {
        TryRequestDecision(ev.AiPlayer);
    }

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
        _pending[uid] = (_client.DecideAsync(context, cts.Token), _timing.CurTime);
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
        UpdatePendingLines();
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
    /// network round-trip. Rejects anything whose intent isn't in <see cref="AIGoals.All"/> or a loaded
    /// <see cref="AiProfessionalGoalPrototype"/> (Milestone 11) - the two whitelists combine, but either one
    /// alone is sufficient to accept an intent; nothing outside both is ever accepted.
    /// </summary>
    public bool TryApplyDecision(EntityUid uid, LlmDecision decision)
    {
        if (!AIGoals.All.Contains(decision.Intent) && !_proto.HasIndex<AiProfessionalGoalPrototype>(decision.Intent))
        {
            _sawmill.Warning($"LLM proposed an unknown intent \"{decision.Intent}\" for {ToPrettyString(uid)}; ignoring.");
            _trace.LlmFailure(uid, "UnknownIntent");
            return false;
        }

        if (Deleted(uid) || !TryComp<GoalComponent>(uid, out var goal))
            return false;

        var priority = Math.Clamp(decision.Priority, 0f, 1f);

        if (goal.CurrentGoal != decision.Intent)
            goal.CurrentGoalSince = _timing.CurTime;

        goal.CurrentGoal = decision.Intent;
        goal.CurrentPriority = priority;
        goal.Reason = $"llm: {decision.Reason}";
        goal.IsLlmOverride = true;
        goal.LlmOverrideExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(_overrideDurationSeconds);
        goal.LastLlmDecision = $"{decision.Intent} (priority {priority:0.00}) - {decision.Reason}";
        goal.LastLlmDecisionAt = _timing.CurTime;

        _trace.LlmDecision(uid, decision.Intent, priority, decision.Reason);
        return true;
    }
}
