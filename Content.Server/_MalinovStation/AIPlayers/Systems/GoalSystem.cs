using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Prototypes;
using Content.Shared._MalinovStation.AIPlayers;
using Content.Shared.Damage.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Prometheus;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Picks the AI player's current goal from needs + personality, and is the single source of truth HTN
/// arbitrates between simultaneously-eligible branches with: Flee/HelpInjured/RepairMachine/Rest (see htn.yml)
/// each require <c>CurrentGoalPrecondition</c> to match this system's pick, in addition to their own
/// independent real-world fact precondition (defense-in-depth - CurrentGoal alone can never make a branch run
/// when its underlying fact isn't true; see Milestone 1). SatisfyHunger/SatisfyThirst remain
/// observability-only: the vanilla FoodCompound HTN branch reacts to the same SatiationComponent directly, and
/// this system mirrors its exact thresholds so the two never disagree about when hunger/thirst is urgent.
/// While an LLM override is in effect, this system leaves CurrentGoal alone until it expires - this is also
/// what makes an external decision to abandon/prioritize a goal actually take effect on HTN behaviour.
///
/// <see cref="GoalComponent.CurrentGoal"/> has exactly two write sites in the whole codebase -
/// <see cref="Reconsider"/> below (the formula-driven arbitration this system owns) and
/// <see cref="TrySetExternalGoal"/> (AI Players 0.3: the single shared method both
/// <see cref="LlmGatewaySystem.TryApplyDecision"/> for a legacy AI and the cognitive <c>PursueGoal</c> action
/// for a cognitive one call into - this system will happily resume arbitrating once
/// <see cref="GoalComponent.LlmOverrideExpiresAt"/> passes either way). DangerSystem is not a third writer
/// despite reacting to attacks/fires first: it only clears a stale LLM override and forces
/// <see cref="GoalComponent.ReconsiderAccumulator"/> to 0 so this system reacts on the very next tick instead
/// of waiting out the routine cooldown - the actual Flee/HelpInjured decision still goes through
/// <see cref="Reconsider"/>. Every HTN branch either gates on <c>CurrentGoalPrecondition</c> matching this
/// system's pick (Flee/HelpInjured/RepairMachine/Rest), is a deliberately independent direct consumer that
/// mirrors the same underlying fact this system also reads (Food/Thirst vs SatiationComponent, Socialize vs
/// SocialSystem), or is an explicit directive channel of its own (ForcedMove). No branch can run purely
/// because HTN's own top-to-bottom compound order reached it. This system is purely a reflex layer - see
/// <see cref="IntentComponent"/> for a cognitive AI's independent, free-form "what it actually wants".
/// </summary>
public sealed partial class GoalSystem : EntitySystem
{
    [Dependency] private SatiationSystem _satiation = default!;
    [Dependency] private AiLodSystem _lod = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private AiTraceSystem _trace = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private DesireSystem _desire = default!;
    [Dependency] private IConfigurationManager _cfg = default!;

    /// <summary>Minimum change in <see cref="GetProgressValue"/> to count as genuine progress rather than
    /// noise (background need decay, floating-point jitter).</summary>
    private const float ProgressEpsilon = 0.01f;

    /// <summary>How long a goal stays excluded from re-selection after being abandoned for lack of
    /// progress, before GoalSystem is willing to give it another try (circumstances may have changed - a
    /// welder was found, the AI wandered near food, etc).</summary>
    private static readonly TimeSpan AbandonCooldown = TimeSpan.FromSeconds(60);

    /// <summary>Reason strings that correspond to a need becoming the dominant priority (see Reconsider),
    /// used to fire NeedChanged/NeedSatisfied trace events on top of the usual GoalChanged one.</summary>
    private static readonly IReadOnlyDictionary<string, string> NeedReasons = new Dictionary<string, string>
    {
        ["fatigue"] = "Fatigue",
        ["social-need"] = "SocialNeed",
        ["hungry"] = "Hunger",
        ["thirsty"] = "Thirst",
        ["boredom"] = "Boredom",
    };

    // Same threshold keys the vanilla FoodCompound gates on, so our goal reporting doesn't drift from
    // what actually triggers eating/drinking behaviour.
    private static readonly SatiationValue PeckishThreshold = "Peckish";
    private static readonly SatiationValue ParchedThreshold = "Parched";
    private static readonly SatiationValue? NoLowerBound = null;

    /// <summary>
    /// Default: how long an actionable goal (not Idle/Rest/Socialize, which are expected to run for a while)
    /// can keep getting reselected without resolving before GoalSystem logs a warning - and how often that
    /// warning can repeat while still stuck. Spec Milestone 1 section 19: an AI should never silently loop
    /// forever. Suits goals whose target is already known/in sight (Flee, HelpInjured, RepairMachine) - see
    /// <see cref="StuckThresholdOverrides"/> for goals that legitimately need longer.
    /// </summary>
    public static readonly TimeSpan StuckWarningThreshold = TimeSpan.FromSeconds(20);

    /// <summary>
    /// SatisfyHunger/SatisfyThirst have no "go to a known food/drink source" behaviour (spec Stabilization
    /// milestone stage 6/9: the threshold must account for what a goal actually requires) - the vanilla
    /// FoodCompound/NearbyFood query only notices something edible already within 10 tiles, so resolving
    /// depends entirely on passively wandering close enough by chance. Flagging that as "stuck" after the
    /// same 20s used for a target already in sight would fire as a false alarm almost every time, not a real
    /// stuck loop - so these get a much longer, wandering-appropriate threshold instead.
    /// </summary>
    private static readonly TimeSpan WanderingNeedStuckThreshold = TimeSpan.FromSeconds(180);

    private static readonly IReadOnlyDictionary<string, TimeSpan> StuckThresholdOverrides = new Dictionary<string, TimeSpan>
    {
        [AIGoals.SatisfyHunger] = WanderingNeedStuckThreshold,
        [AIGoals.SatisfyThirst] = WanderingNeedStuckThreshold,
    };

    private static readonly IReadOnlySet<string> ExemptFromStuckWarning = new HashSet<string>
    {
        AIGoals.Idle,
        AIGoals.Rest,
        AIGoals.Socialize,
    };

    /// <summary>Stabilization milestone stage 10: total goal reconsiderations, for "AI decisions/sec".</summary>
    public static readonly Counter ReconsiderationsMetric = Metrics.CreateCounter(
        "aiplayers_goal_reconsiderations_total",
        "Total number of AI player goal reconsiderations performed.");

    /// <summary>How many of those reconsiderations actually changed the current goal, vs confirmed the same
    /// one - a high reconsideration count with a low change count is healthy; the two converging means goals
    /// are thrashing.</summary>
    public static readonly Counter GoalChangesMetric = Metrics.CreateCounter(
        "aiplayers_goal_changes_total",
        "Total number of times an AI player's current goal actually changed.");

    /// <summary>Wall-clock time per reconsideration - GoalSystem's own share of "AI system tick time".</summary>
    public static readonly Histogram ReconsiderDurationMetric = Metrics.CreateHistogram(
        "aiplayers_goal_reconsider_duration_seconds",
        "Wall-clock time spent per AI player goal reconsideration.",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.0001, 2, 14) });

    /// <summary>How long an externally-set goal (<see cref="TrySetExternalGoal"/>) stays in effect before
    /// this system resumes normal arbitration. Subscribed here (moved from <see cref="LlmGatewaySystem"/> in
    /// AI Players 0.3) since it now gates the one shared write path both the legacy LLM decision and the
    /// cognitive <c>PursueGoal</c> action go through.</summary>
    private float _overrideDurationSeconds;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersLlmOverrideDurationSeconds, v => _overrideDurationSeconds = v, true);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<GoalComponent, NeedsComponent, PersonalityComponent>();
        while (query.MoveNext(out var uid, out var goal, out var needs, out var personality))
        {
            goal.ReconsiderAccumulator -= frameTime;
            if (goal.ReconsiderAccumulator > 0f)
                continue;

            // Note: DangerSystem forces ReconsiderAccumulator to 0 directly on attack, so an urgent Flee
            // reconsideration always happens on the very next tick regardless of this LOD scaling - only
            // the routine cadence slows down for distant AI players.
            goal.ReconsiderAccumulator = goal.ReconsiderCooldown * _lod.GetMultiplier(uid);

            ReconsiderationsMetric.Inc();
            var before = goal.CurrentGoal;
            using (ReconsiderDurationMetric.NewTimer())
            {
                Reconsider(uid, goal, needs, personality);
            }

            if (goal.CurrentGoal != before)
                GoalChangesMetric.Inc();
        }
    }

    private void Reconsider(EntityUid uid, GoalComponent goal, NeedsComponent needs, PersonalityComponent personality)
    {
        if (goal.IsLlmOverride)
        {
            if (_timing.CurTime < goal.LlmOverrideExpiresAt)
                return;

            goal.IsLlmOverride = false;
        }

        var candidates = ComputeCandidates(uid, goal, needs, personality);

        // AI Players 2.0 Milestone 1: materialize every candidate this pass considered, not just the winner -
        // a no-op for a legacy AI player (no DesireComponent), but this is the plural "what do I currently
        // want, and how strongly" state a cognitive-mode AI's LLM decision gets to see and reason about.
        _desire.Record(uid, candidates);

        // Idle is always candidates[0] and is unconditionally added below, so this is never empty - the same
        // "first candidate to reach a given priority wins ties" behaviour as the original sequential
        // if (x > bestPriority) chain this replaced (see ComputeCandidates' doc comment for the equivalence
        // proof), just as an explicit scan over a list instead of inline running-max variables.
        var winner = candidates[0];
        for (var i = 1; i < candidates.Count; i++)
        {
            if (candidates[i].Priority > winner.Priority)
                winner = candidates[i];
        }

        goal.CurrentPriority = winner.Priority;

        if (winner.Name == goal.CurrentGoal)
        {
            WarnIfStuck(uid, goal, winner.Name);
            return;
        }

        _trace.GoalChanged(uid, goal.CurrentGoal, winner.Name, winner.Reason, winner.Priority);

        if (NeedReasons.TryGetValue(winner.Reason, out var startedNeed))
            _trace.NeedChanged(uid, startedNeed);

        // Only claim a need actually resolved (not just got outranked by a different urgent need this tick)
        // when nothing else is driving behaviour either - the safest signal we have without per-need
        // threshold tracking (deferred to the progress-tracking stage).
        if (winner.Reason == "nothing-urgent" && NeedReasons.TryGetValue(goal.Reason, out var resolvedNeed))
            _trace.NeedSatisfied(uid, resolvedNeed);

        goal.CurrentGoal = winner.Name;
        goal.Reason = winner.Reason;
        goal.CurrentGoalSince = _timing.CurTime;
        // Fresh pursuit, fresh baseline - see WarnIfStuck.
        goal.ProgressBaseline = null;

        // AI Players 0.3: deliberately does NOT touch IntentComponent, even for a cognitive-mode AI player.
        // Intent is now independent of this reflex layer (see IntentComponent's own doc comment) - if this
        // formula's pick were still mirrored here, it would silently overwrite a cognitive AI's free-form
        // intent the instant its PursueGoal override expires, which is exactly the leak this milestone
        // removes. IntentComponent is written exclusively by LlmGatewaySystem.TryApplyCognitiveDecision.
    }

    /// <summary>Whether <paramref name="goalName"/> is a legal external goal - the same combined whitelist
    /// <see cref="TrySetExternalGoal"/> checks against, exposed read-only so an <see cref="Actions.IAiAction.CanDo"/>
    /// implementation (e.g. the cognitive <c>PursueGoal</c> action) can validate without mutating state.</summary>
    public bool IsKnownGoalName(string goalName) =>
        AIGoals.All.Contains(goalName) || _proto.HasIndex<AiProfessionalGoalPrototype>(goalName);

    /// <summary>
    /// Validates and applies an externally-provided goal directly to <see cref="GoalComponent"/>, bypassing
    /// <see cref="Reconsider"/>'s own needs/personality arbitration - the caller is asserting this goal should
    /// take effect right now. The single shared write path for both <see cref="LlmGatewaySystem.TryApplyDecision"/>
    /// (legacy AI) and the cognitive <c>PursueGoal</c> action, so <see cref="GoalComponent"/>'s writer surface
    /// and "legal goal name" whitelist stay one source of truth (see <see cref="GoalComponent"/>'s own doc
    /// comment for the resulting invariant).
    /// </summary>
    public bool TrySetExternalGoal(EntityUid uid, string goalName, float priority, string reason, [NotNullWhen(false)] out string? failReason)
    {
        if (!IsKnownGoalName(goalName))
        {
            failReason = $"Unknown goal \"{goalName}\".";
            return false;
        }

        if (Deleted(uid) || !TryComp<GoalComponent>(uid, out var goal))
        {
            failReason = "Entity is not a valid AI player.";
            return false;
        }

        var clamped = Math.Clamp(priority, 0f, 1f);

        if (goal.CurrentGoal != goalName)
            goal.CurrentGoalSince = _timing.CurTime;

        goal.CurrentGoal = goalName;
        goal.CurrentPriority = clamped;
        goal.Reason = reason;
        goal.IsLlmOverride = true;
        goal.LlmOverrideExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(_overrideDurationSeconds);
        goal.LastLlmDecision = $"{goalName} (priority {clamped:0.00}) - {reason}";
        goal.LastLlmDecisionAt = _timing.CurTime;

        failReason = null;
        return true;
    }

    /// <summary>
    /// Every candidate goal this AI player could pursue right now and its computed priority - the exact same
    /// formulas <see cref="Reconsider"/> used to run inline as a sequential "if better than the running best"
    /// chain, extracted so a cognitive-mode AI can see the whole set (<see cref="DesireSystem"/>) instead of
    /// just the winner. <see cref="Reconsider"/>'s winner-selection (highest priority, first-evaluated wins
    /// ties) reproduces the original chain's behaviour exactly as long as this returns the same (name,
    /// priority) pairs in the same order for every candidate whose original gate condition held - which is
    /// the entire contract this method must keep. Idle is always present as candidates[0] (the original's
    /// starting baseline), so the result is never empty.
    /// </summary>
    private List<Desire> ComputeCandidates(EntityUid uid, GoalComponent goal, NeedsComponent needs, PersonalityComponent personality)
    {
        var candidates = new List<Desire> { new(AIGoals.Idle, 0.1f, "nothing-urgent") };

        if (TryComp<DangerComponent>(uid, out var danger))
        {
            // Braver/more risk-tolerant AI players still flee, just a little less readily than fearful ones.
            // A nearby fire is just as urgent as an attacker - both are handled by the same FleeCompound branch.
            var hasThreat = danger.ThreatSource is not null || danger.FireHazardLocation is not null;
            if (hasThreat)
            {
                var fleePriority = MathF.Max(0.5f, 0.95f - personality.Courage * 0.25f - personality.RiskTolerance * 0.1f);
                candidates.Add(new Desire(AIGoals.Flee, fleePriority, danger.ThreatSource is not null ? "attacked" : "saw-fire"));
            }

            // Empathetic/brave AI players are more likely to go check on someone than to look away.
            if (danger.NearbyInjured is not null)
            {
                var helpPriority = MathF.Max(0f, 0.3f + personality.Empathy * 0.4f + personality.Courage * 0.1f);
                candidates.Add(new Desire(AIGoals.HelpInjured, helpPriority, "saw-someone-hurt"));
            }
        }

        // Lazier AI players want to rest sooner; more professional/authority-respecting ones push through longer.
        if (!IsAbandoned(goal, AIGoals.Rest))
        {
            var restPriority = MathF.Max(0f, needs.Fatigue * (0.6f + personality.Laziness * 0.4f - personality.Professionalism * 0.2f));
            candidates.Add(new Desire(AIGoals.Rest, restPriority, "fatigue"));
        }

        var socializePriority = needs.SocialNeed * personality.Sociability;
        candidates.Add(new Desire(AIGoals.Socialize, socializePriority, "social-need"));

        // AI Players 0.6: cognitive-only, same precedent LandmarkPerceptionSystem/ItemOpportunitySystem/
        // InteractionOpportunitySystem already established for "new side-effect is cognitive-only" - a legacy
        // AI player has no use for a desire it has no LLM to interpret, and needs.Boredom never grows for one
        // anyway (see NeedsSystem.BoredomDelta), so this candidate would always be priority 0 for it regardless.
        if (HasComp<CognitiveModeComponent>(uid))
        {
            var restlessnessPriority = needs.Boredom * (0.3f + personality.Curiosity * 0.7f);
            candidates.Add(new Desire(AIGoals.Restlessness, restlessnessPriority, "boredom"));
        }

        if (TryComp<SatiationComponent>(uid, out var satiation))
        {
            const float hungerPriority = 0.65f;
            if (!IsAbandoned(goal, AIGoals.SatisfyHunger) &&
                _satiation.IsValueInRange((uid, satiation), SatiationSystem.Hunger, above: NoLowerBound, below: PeckishThreshold))
            {
                candidates.Add(new Desire(AIGoals.SatisfyHunger, hungerPriority, "hungry"));
            }

            const float thirstPriority = 0.65f;
            if (!IsAbandoned(goal, AIGoals.SatisfyThirst) &&
                _satiation.IsValueInRange((uid, satiation), SatiationSystem.Thirst, above: NoLowerBound, below: ParchedThreshold))
            {
                candidates.Add(new Desire(AIGoals.SatisfyThirst, thirstPriority, "thirsty"));
            }
        }

        // Milestone 11: professional/job-specific goals. Purely observability here (same as every other
        // candidate above) - RepairMachineCompound's own HTN precondition independently re-checks the same
        // opportunity/job facts rather than trusting this, mirroring Flee/HelpInjured's defense-in-depth.
        if (TryComp<RepairOpportunityComponent>(uid, out var repairOpportunity) &&
            repairOpportunity.NearbyRepairTarget is { } repairTarget &&
            !Deleted(repairTarget) &&
            TryComp<AIPlayerComponent>(uid, out var aiPlayer) &&
            aiPlayer.Job is { } job &&
            _proto.TryIndex<AiProfessionalGoalPrototype>(ProfessionalGoals.RepairMachine, out var repairGoal) &&
            repairGoal.Jobs.Contains(job) &&
            !IsAbandoned(goal, ProfessionalGoals.RepairMachine))
        {
            // More professional, less lazy AI players prioritize work over idling sooner.
            var repairPriority = MathF.Max(0f,
                repairGoal.BasePriority + personality.Professionalism * 0.3f - personality.Laziness * 0.2f);
            candidates.Add(new Desire(ProfessionalGoals.RepairMachine, repairPriority, "saw-damaged-machine"));
        }

        return candidates;
    }

    private bool IsAbandoned(GoalComponent goal, string candidate) =>
        goal.RecentlyAbandoned.TryGetValue(candidate, out var expiry) && _timing.CurTime < expiry;

    /// <summary>
    /// Logs a throttled warning if an actionable goal has been reselected for longer than its stuck
    /// threshold without resolving - e.g. an AI player stuck replanning the same failing repair attempt
    /// every ~0.45s (HTN's PlanCooldown) with nothing externally visible about it. The threshold itself is
    /// goal-aware (<see cref="StuckThresholdOverrides"/>): a goal that can only resolve by passively
    /// wandering into range of something needs much more patience than one whose target is already known.
    ///
    /// For goals with a measurable resolution signal (<see cref="GetProgressValue"/>), a stuck warning also
    /// checks whether that value has moved at all since the *previous* stuck warning (i.e. across a full
    /// threshold's worth of time, not tick to tick). No movement at all means the goal isn't slow, it's
    /// genuinely not working - so it's abandoned for <see cref="AbandonCooldown"/> instead of being retried
    /// forever (Stabilization milestone stage 4: real recovery, not just a log warning). Movement that just
    /// hasn't finished yet is left alone; it'll keep being warned about (and re-checked) until it either
    /// resolves or truly stalls.
    /// </summary>
    private void WarnIfStuck(EntityUid uid, GoalComponent goal, string currentGoal)
    {
        if (ExemptFromStuckWarning.Contains(currentGoal))
            return;

        var threshold = StuckThresholdOverrides.GetValueOrDefault(currentGoal, StuckWarningThreshold);

        if (_timing.CurTime - goal.CurrentGoalSince < threshold)
            return;

        if (_timing.CurTime - goal.LastStuckWarningAt < threshold)
            return;

        _trace.GoalStuck(uid, currentGoal, goal.Reason);
        goal.LastStuckWarningAt = _timing.CurTime;

        if (GetProgressValue(uid, currentGoal) is not { } current)
            return;

        if (goal.ProgressBaseline is not { } baseline)
        {
            goal.ProgressBaseline = current;
            return;
        }

        if (current > baseline + ProgressEpsilon)
        {
            // Genuinely improving, just slowly - give it a fresh baseline and keep going.
            goal.ProgressBaseline = current;
            return;
        }

        goal.RecentlyAbandoned[currentGoal] = _timing.CurTime + AbandonCooldown;
        _trace.GoalFailed(uid, currentGoal, "NoProgress");
    }

    /// <summary>
    /// The resolution-relevant value for a goal we know how to measure, normalised so that a *higher*
    /// number always means *closer to resolved* (fatigue and damage are negated, since lower is better for
    /// those) - lets <see cref="WarnIfStuck"/> use one direction-agnostic comparison for every goal type.
    /// Returns null for goals with no cheap measurable signal yet (Flee/HelpInjured/Socialize/Idle), which
    /// fall back to pure timer-based warnings with no abandonment.
    /// </summary>
    private float? GetProgressValue(EntityUid uid, string goalName)
    {
        switch (goalName)
        {
            case AIGoals.Rest:
                return TryComp<NeedsComponent>(uid, out var needs) ? -needs.Fatigue : null;

            case AIGoals.SatisfyHunger:
                return TryComp<SatiationComponent>(uid, out var hungerSatiation)
                    ? _satiation.GetValueOrNull((uid, hungerSatiation), SatiationSystem.Hunger)
                    : null;

            case AIGoals.SatisfyThirst:
                return TryComp<SatiationComponent>(uid, out var thirstSatiation)
                    ? _satiation.GetValueOrNull((uid, thirstSatiation), SatiationSystem.Thirst)
                    : null;

            case ProfessionalGoals.RepairMachine:
                if (!TryComp<RepairOpportunityComponent>(uid, out var repair) ||
                    repair.NearbyRepairTarget is not { } target ||
                    Deleted(target) ||
                    !TryComp<DamageableComponent>(target, out var damageable))
                {
                    return null;
                }

                // Safe: mirrors the same (obsolete-but-still-current) API RepairOpportunitySystem/
                // RepairableSystem themselves use to check for damage - see those for the vanilla precedent.
#pragma warning disable CS0618
                return -(float)_damageable.GetTotalDamage((target, damageable));
#pragma warning restore CS0618

            default:
                return null;
        }
    }
}
