using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Prototypes;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Picks the AI player's current goal from needs + personality. This is primarily an observability/intent
/// layer for now (see <see cref="GoalComponent"/> docs): SatisfyHunger/SatisfyThirst are reported here purely
/// for visibility, since the existing vanilla FoodCompound HTN branch already reacts to the same
/// SatiationComponent directly. Rest (Milestone 2), Flee/HelpInjured (Milestone 7) and RepairMachine
/// (Milestone 11) each have a dedicated HTN branch whose own precondition independently re-derives the same
/// underlying fact this system reads, rather than trusting CurrentGoal.
/// While a Milestone 4 LLM override is in effect, this system leaves CurrentGoal alone until it expires.
/// </summary>
public sealed partial class GoalSystem : EntitySystem
{
    [Dependency] private SatiationSystem _satiation = default!;
    [Dependency] private AiLodSystem _lod = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    // Same threshold keys the vanilla FoodCompound gates on, so our goal reporting doesn't drift from
    // what actually triggers eating/drinking behaviour.
    private static readonly SatiationValue PeckishThreshold = "Peckish";
    private static readonly SatiationValue ParchedThreshold = "Parched";
    private static readonly SatiationValue? NoLowerBound = null;

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
            Reconsider(uid, goal, needs, personality);
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

        var best = AIGoals.Idle;
        // Baseline: nothing urgent, keep doing routine work/idling.
        var bestPriority = 0.1f;
        var reason = "nothing-urgent";

        if (TryComp<DangerComponent>(uid, out var danger))
        {
            // Braver/more risk-tolerant AI players still flee, just a little less readily than fearful ones.
            var fleePriority = danger.ThreatSource is not null
                ? MathF.Max(0.5f, 0.95f - personality.Courage * 0.25f - personality.RiskTolerance * 0.1f)
                : 0f;

            if (fleePriority > bestPriority)
            {
                best = AIGoals.Flee;
                bestPriority = fleePriority;
                reason = "attacked";
            }

            // Empathetic/brave AI players are more likely to go check on someone than to look away.
            var helpPriority = danger.NearbyInjured is not null
                ? MathF.Max(0f, 0.3f + personality.Empathy * 0.4f + personality.Courage * 0.1f)
                : 0f;

            if (helpPriority > bestPriority)
            {
                best = AIGoals.HelpInjured;
                bestPriority = helpPriority;
                reason = "saw-someone-hurt";
            }
        }

        // Lazier AI players want to rest sooner; more professional/authority-respecting ones push through longer.
        var restPriority = MathF.Max(0f, needs.Fatigue * (0.6f + personality.Laziness * 0.4f - personality.Professionalism * 0.2f));
        if (restPriority > bestPriority)
        {
            best = AIGoals.Rest;
            bestPriority = restPriority;
            reason = "fatigue";
        }

        var socializePriority = needs.SocialNeed * personality.Sociability;
        if (socializePriority > bestPriority)
        {
            best = AIGoals.Socialize;
            bestPriority = socializePriority;
            reason = "social-need";
        }

        if (TryComp<SatiationComponent>(uid, out var satiation))
        {
            const float hungerPriority = 0.65f;
            if (hungerPriority > bestPriority &&
                _satiation.IsValueInRange((uid, satiation), SatiationSystem.Hunger, above: NoLowerBound, below: PeckishThreshold))
            {
                best = AIGoals.SatisfyHunger;
                bestPriority = hungerPriority;
                reason = "hungry";
            }

            const float thirstPriority = 0.65f;
            if (thirstPriority > bestPriority &&
                _satiation.IsValueInRange((uid, satiation), SatiationSystem.Thirst, above: NoLowerBound, below: ParchedThreshold))
            {
                best = AIGoals.SatisfyThirst;
                bestPriority = thirstPriority;
                reason = "thirsty";
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
            repairGoal.Jobs.Contains(job))
        {
            // More professional, less lazy AI players prioritize work over idling sooner.
            var repairPriority = MathF.Max(0f,
                repairGoal.BasePriority + personality.Professionalism * 0.3f - personality.Laziness * 0.2f);

            if (repairPriority > bestPriority)
            {
                best = ProfessionalGoals.RepairMachine;
                bestPriority = repairPriority;
                reason = "saw-damaged-machine";
            }
        }

        goal.CurrentPriority = bestPriority;

        if (best == goal.CurrentGoal)
            return;

        Log.Debug($"{ToPrettyString(uid)} goal changed: {goal.CurrentGoal} -> {best} ({reason}, priority {bestPriority:0.00})");
        goal.CurrentGoal = best;
        goal.Reason = reason;
    }
}
