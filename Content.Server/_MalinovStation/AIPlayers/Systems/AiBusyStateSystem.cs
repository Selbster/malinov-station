using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC.HTN;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// AI Players 0.4 Milestones 5-6: clears <see cref="AiBusyStateComponent"/> once the commitment it recorded
/// has actually run its course, and forces a fast cognitive re-reflection (Milestone 6's interruption) when a
/// real danger event interrupts a cancellable one. Deliberately does NOT track completion/interruption as an
/// independent state machine - it polls the exact same signals each <see cref="IAiAction.IsExtended"/>
/// action's own <c>Do</c> already writes into (<see cref="Components.GoalComponent.IsLlmOverride"/> for
/// <c>PursueGoal</c>, the HTN blackboard's <c>ForcedDestination</c> key for <c>GoToKnownLocation</c>), so this
/// component can never drift out of sync with what's actually happening.
///
/// Milestone 6 in practice is narrower than it first sounds: <see cref="DangerSystem"/> already clears
/// <c>IsLlmOverride</c> and forces an immediate <see cref="GoalComponent.ReconsiderAccumulator"/> the instant a
/// real threat/fire is detected (so a <c>PursueGoal</c> commitment is already correctly preemptable at the
/// reflex/HTN level), and <c>GoToKnownLocation</c>'s <c>ForcedDestination</c> key was never gated by
/// <c>IsLlmOverride</c> in the first place - <see cref="Components.GoalComponent"/>'s own reconsider cadence
/// keeps running underneath it regardless, and <c>FleeCompound</c> is evaluated before <c>ForcedMoveCompound</c>
/// in <c>AIPlayerRootCompound</c>, so a genuine Flee already wins the moment <c>CurrentGoal</c> changes. The one
/// real gap this system closes: the *cognitive* layer (Intent/LLM) had no reason to reconsider promptly just
/// because the *reflex* layer already reacted - "current action is now busy with something else" is exactly
/// the kind of event spec Milestone 6 says shouldn't wait for the routine periodic reflection interval.
/// </summary>
public sealed partial class AiBusyStateSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AiLodSystem _lod = default!;
    [Dependency] private HTNSystem _htn = default!;

    /// <summary>How often (in seconds) busy AI players are re-checked - scaled by LOD like every other
    /// periodic scan in this subsystem. Not per-tick: busy-state only ever changes on a real HTN/goal
    /// transition, never faster than that.</summary>
    private const float ScanCooldown = 1f;

    /// <summary>
    /// AI Players 0.6: the fastest an AI player could plausibly cover ground under its own steering, in
    /// tiles/second - well above any real run speed, so a higher implied speed between two checks means
    /// something moved this entity out of band (an admin teleport, a test setting <c>Transform.Coordinates</c>
    /// directly) rather than its own steering. A speed, not a flat per-check distance: this system's own scan
    /// interval is LOD-scaled up to <see cref="AiLodComponent.BackgroundMultiplier"/>x for a Background-tier AI
    /// (~15s between checks, not ~1s), and a flat distance threshold sized for the common case would misfire on
    /// perfectly ordinary long-distance travel once that much real time has actually passed.
    /// </summary>
    private const float MaxPlausibleWalkSpeed = 10f;

    /// <summary>
    /// AI Players 0.6: how close counts as "landed at the destination" for
    /// <see cref="LandedAtOwnDestination"/> - loose enough to tolerate the same kind of margin
    /// <c>MoveToOperator</c>'s own <c>MovementRange</c> arrival tolerance allows.
    /// </summary>
    private const float NearDestinationTolerance = 3f;

    private float _accumulator;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _accumulator -= frameTime;
        if (_accumulator > 0f)
            return;

        // A single shared accumulator rather than one per entity: this system's own work per busy entity is
        // trivial (a couple of component reads), so there's nothing to gain from staggering entities against
        // each other the way a per-entity scan (e.g. PerceptionSystem) does to spread out expensive spatial
        // queries - LOD still scales the shared cadence itself via the lowest-multiplier busy entity below.
        var multiplier = 1f;

        var query = EntityQueryEnumerator<AiBusyStateComponent>();
        while (query.MoveNext(out var uid, out var busy))
        {
            if (busy.CurrentAction is null)
                continue;

            multiplier = MathF.Min(multiplier, _lod.GetMultiplier(uid));
            CheckBusyState(uid, busy);
        }

        _accumulator = ScanCooldown * multiplier;
    }

    private void CheckBusyState(EntityUid uid, AiBusyStateComponent busy)
    {
        if (_timing.CurTime - busy.StartedAt > TimeSpan.FromSeconds(busy.MaxBusyDurationSeconds))
        {
            Clear(busy);
            return;
        }

        if (TryComp(uid, out TransformComponent? xform))
        {
            var current = xform.Coordinates;
            var elapsed = (_timing.CurTime - busy.LastCheckedAt).TotalSeconds;
            var movedImplausibly = elapsed > 0 &&
                busy.LastCheckedPosition is { } last &&
                last.TryDistance(EntityManager, current, out var moved) &&
                moved / elapsed > MaxPlausibleWalkSpeed;

            busy.LastCheckedPosition = current;
            busy.LastCheckedAt = _timing.CurTime;

            // A sudden jump that lands the AI at (or very near) its own intended destination isn't a
            // disruptive relocation to abort - it's arrival, however it actually happened (a real pathfind
            // finishing between checks, an external shortcut). Only a jump that leaves the AI somewhere
            // unrelated to what it was already doing counts as the kind of relocation spec section 9/30 means.
            if (movedImplausibly && !LandedAtOwnDestination(uid, busy, current))
            {
                AbortForRelocation(uid, busy);
                return;
            }
        }

        if (busy.CancellationAllowed && !busy.InterruptionNoticed && IsDangerActive(uid))
        {
            busy.InterruptionNoticed = true;

            if (TryComp<CognitiveModeComponent>(uid, out var cognitive))
                cognitive.ReflectionAccumulator = 0f;
        }

        if (!IsStillActuallyBusy(uid, busy))
        {
            // AI Players 0.6.2: arriving is itself a meaningful event. A journey ending used to just clear the
            // commitment silently, leaving the AI standing on the beacon it walked to until the routine
            // reflection interval came round - up to 45s of doing visibly nothing at the very moment it has
            // just reached somewhere new and has the most to react to. Treated like every other interruption
            // here: decide what this place is for, now, rather than on the next tick of the clock.
            var wasTravelling = IsForcedDestinationAction(busy.CurrentAction);

            Clear(busy);

            if (wasTravelling && TryComp<CognitiveModeComponent>(uid, out var arrived))
                arrived.ReflectionAccumulator = 0f;
        }
    }

    /// <summary>
    /// AI Players 0.6: the concrete fix for "passenger drifts back toward where it was headed after being
    /// moved" (spec section 9/30) - identified by audit as a stale-plan bug, not a spawn-anchor feature: a
    /// <c>GoToKnownLocation</c> commitment's destination lives on the HTN blackboard
    /// (<see cref="Actions.MoveToAction.ForcedDestinationKey"/>) and nothing previously invalidated it after an
    /// out-of-band relocation, so HTN just kept pathfinding toward wherever it was already headed (which,
    /// absent any real reason to have travelled far, was usually somewhere close to the AI's original
    /// position). Aborts the stale destination and forces an immediate fresh cognitive decision - the same
    /// "meaningful event, don't wait for the routine reflection interval" treatment every other interruption
    /// here already gets.
    /// </summary>
    private void AbortForRelocation(EntityUid uid, AiBusyStateComponent busy)
    {
        if (IsForcedDestinationAction(busy.CurrentAction) && TryComp<HTNComponent>(uid, out var htn))
        {
            htn.Blackboard.Remove<EntityCoordinates>(MoveToAction.ForcedDestinationKey);

            // AI Players 0.6.2: removing the key is not enough on its own. Anything HTN has already planned -
            // or is still planning, asynchronously - may contain a MoveTo task that reads this exact key at
            // startup, unconditionally (MoveToOperator.Startup uses GetValue, not TryGetValue). Left alone,
            // such a task starts a moment later against a key that no longer exists and takes the whole NPC
            // update loop down with a KeyNotFoundException. So the stale plan and the in-flight planning job
            // are torn down together with the key that justified them, using the engine's own public API and
            // the same cancel-and-null idiom HTNSystem.SetHTNEnabled uses - no vanilla file is touched.
            htn.PlanningToken?.Cancel();
            htn.PlanningToken = null;

            if (htn.Plan is { } plan)
            {
                // Both calls, in this order - exactly what HTNSystem.SetHTNEnabled does when it tears an NPC's
                // plan down, and the order matters. MoveToOperator's cleanup (unregistering NPCSteeringComponent,
                // cancelling the movement token, dropping the cached path) hangs off IHtnConditionalShutdown
                // with ShutdownState = TaskFinished, and ShutdownPlan only fires conditional shutdowns flagged
                // PlanFinished. ShutdownPlan alone therefore leaves steering still registered against the
                // destination we just invalidated - the AI would keep being steered toward exactly the stale
                // target this whole method exists to abandon.
                _htn.ShutdownTask(plan.CurrentOperator, htn.Blackboard, HTNOperatorStatus.Failed);
                _htn.ShutdownPlan(htn);
            }
        }

        // A relocated PursueGoal commitment (e.g. mid-repair) is just as stale as a relocated GoToKnownLocation
        // one - without this, GoalSystem.Reconsider would keep deferring to the abandoned override until it
        // naturally expires instead of resuming normal arbitration immediately.
        if (busy.CurrentAction == PursueGoalAction.ActionName && TryComp<GoalComponent>(uid, out var goal))
            goal.IsLlmOverride = false;

        Clear(busy);

        if (TryComp<CognitiveModeComponent>(uid, out var cognitive))
            cognitive.ReflectionAccumulator = 0f;
    }

    /// <summary>
    /// AI Players 0.6: whether <paramref name="current"/> is close enough to the AI's own <c>GoToKnownLocation</c>
    /// target to count as having arrived there, rather than having been relocated somewhere unrelated.
    /// </summary>
    private bool LandedAtOwnDestination(EntityUid uid, AiBusyStateComponent busy, EntityCoordinates current)
    {
        return IsForcedDestinationAction(busy.CurrentAction) &&
            TryComp<HTNComponent>(uid, out var htn) &&
            htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out var destination, EntityManager) &&
            current.TryDistance(EntityManager, destination, out var distanceToDestination) &&
            distanceToDestination <= NearDestinationTolerance;
    }

    private bool IsDangerActive(EntityUid uid) =>
        TryComp<DangerComponent>(uid, out var danger) &&
        ((danger.ThreatSource is not null && _timing.CurTime < danger.ThreatExpiresAt) || danger.FireHazardLocation is not null);

    private bool IsStillActuallyBusy(EntityUid uid, AiBusyStateComponent busy)
    {
        if (busy.CurrentAction == PursueGoalAction.ActionName)
            return TryComp<GoalComponent>(uid, out var goal) && goal.IsLlmOverride;

        if (IsForcedDestinationAction(busy.CurrentAction))
        {
            return TryComp<HTNComponent>(uid, out var htn) &&
                htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out _, EntityManager);
        }

        // Not one of the two IsExtended actions this system knows how to track - shouldn't happen (only
        // AiActionRegistrySystem.TryDoAction ever sets CurrentAction, gated on IsExtended), but never leave an
        // unrecognized commitment stuck open forever.
        return false;
    }

    /// <summary>
    /// AI Players 0.6.2: the extended actions whose whole commitment *is* a destination on the HTN
    /// blackboard, and which therefore share identical completion, relocation and arrival semantics here.
    /// Kept as one predicate rather than repeated name comparisons because an unrecognised name silently
    /// clears the busy state on the very next scan - so a new travelling action that forgot to appear here
    /// would look like it had finished the instant it began.
    /// </summary>
    private static bool IsForcedDestinationAction(string? actionName) =>
        actionName == GoToKnownLocationAction.ActionName || actionName == ExploreStationAction.ActionName;

    private static void Clear(AiBusyStateComponent busy)
    {
        busy.CurrentAction = null;
        busy.Reason = string.Empty;
        busy.InterruptionNoticed = false;
    }
}
