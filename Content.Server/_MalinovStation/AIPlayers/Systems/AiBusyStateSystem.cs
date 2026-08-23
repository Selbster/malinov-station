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

    /// <summary>How often (in seconds) busy AI players are re-checked - scaled by LOD like every other
    /// periodic scan in this subsystem. Not per-tick: busy-state only ever changes on a real HTN/goal
    /// transition, never faster than that.</summary>
    private const float ScanCooldown = 1f;

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

        if (busy.CancellationAllowed && !busy.InterruptionNoticed && IsDangerActive(uid))
        {
            busy.InterruptionNoticed = true;

            if (TryComp<CognitiveModeComponent>(uid, out var cognitive))
                cognitive.ReflectionAccumulator = 0f;
        }

        if (!IsStillActuallyBusy(uid, busy))
            Clear(busy);
    }

    private bool IsDangerActive(EntityUid uid) =>
        TryComp<DangerComponent>(uid, out var danger) &&
        ((danger.ThreatSource is not null && _timing.CurTime < danger.ThreatExpiresAt) || danger.FireHazardLocation is not null);

    private bool IsStillActuallyBusy(EntityUid uid, AiBusyStateComponent busy)
    {
        if (busy.CurrentAction == PursueGoalAction.ActionName)
            return TryComp<GoalComponent>(uid, out var goal) && goal.IsLlmOverride;

        if (busy.CurrentAction == GoToKnownLocationAction.ActionName)
        {
            return TryComp<HTNComponent>(uid, out var htn) &&
                htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out _, EntityManager);
        }

        // Not one of the two IsExtended actions this system knows how to track - shouldn't happen (only
        // AiActionRegistrySystem.TryDoAction ever sets CurrentAction, gated on IsExtended), but never leave an
        // unrecognized commitment stuck open forever.
        return false;
    }

    private static void Clear(AiBusyStateComponent busy)
    {
        busy.CurrentAction = null;
        busy.Reason = string.Empty;
        busy.InterruptionNoticed = false;
    }
}
