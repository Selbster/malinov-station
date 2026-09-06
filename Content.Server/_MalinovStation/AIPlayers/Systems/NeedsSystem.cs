using Content.Server._MalinovStation.AIPlayers.Components;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Decays the needs on <see cref="NeedsComponent"/> over time. Hunger/thirst are intentionally not handled
/// here: they already exist on the mob via <see cref="Content.Shared.Nutrition.Components.SatiationComponent"/>
/// and the vanilla FoodCompound HTN branch already reacts to them.
/// Deliberately NOT scaled by AI LOD (spec Milestone 8): this is plain per-entity arithmetic with no spatial
/// queries, so it isn't a real performance cost even at high population, and throttling it would make a
/// distant AI player's needs visibly "freeze" and then jump when a player wanders back into range -
/// LOD belongs on the expensive stuff (Perception's spatial queries, Danger's scans), not here.
/// </summary>
public sealed partial class NeedsSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NeedsComponent>();
        while (query.MoveNext(out var uid, out var needs))
        {
            TryComp<PersonalityComponent>(uid, out var personality);
            var laziness = personality?.Laziness ?? 0.5f;
            var sociability = personality?.Sociability ?? 0.5f;
            var curiosity = personality?.Curiosity ?? 0.5f;

            // Lazier AI players tire faster; more sociable ones crave company faster when alone.
            needs.Fatigue = Math.Clamp(needs.Fatigue + needs.BaseFatigueGainPerSecond * (0.5f + laziness) * frameTime, 0f, 1f);
            needs.SocialNeed = Math.Clamp(needs.SocialNeed + needs.BaseSocialNeedGainPerSecond * (0.5f + sociability) * frameTime, 0f, 1f);

            var stressPressure = MathF.Max(0f, needs.Fatigue - 0.8f) + MathF.Max(0f, needs.SocialNeed - 0.8f);
            needs.Stress = Math.Clamp(needs.Stress + stressPressure * frameTime - needs.StressDecayPerSecond * frameTime, 0f, 1f);

            // Safety only ever rises when DangerSystem detects a threat; it always relaxes back down here.
            needs.Safety = Math.Clamp(needs.Safety - needs.SafetyDecayPerSecond * frameTime, 0f, 1f);

            needs.Boredom = Math.Clamp(needs.Boredom + BoredomDelta(uid, needs, curiosity) * frameTime, 0f, 1f);
        }
    }

    /// <summary>
    /// AI Players 0.6: positive while nothing has actually happened to this AI for longer than
    /// <see cref="NeedsComponent.BoredomGraceSeconds"/>, negative (draining back toward 0) while it has.
    /// Requires <see cref="IntentComponent"/>, which no legacy (non-cognitive) AI player has - that is what
    /// keeps boredom naturally inert for one without an explicit <see cref="CognitiveModeComponent"/> check.
    ///
    /// AI Players 0.6.3: "something happened" deliberately means a change in the *world* - the AI went
    /// somewhere new, or it just finished a conversation - and no longer includes picking a new intent.
    ///
    /// Intent used to count, and 0.6.1 already had to patch around it once by not re-stamping
    /// <see cref="IntentComponent.ChosenAt"/> when a reflection reconfirmed the same intent name. That was not
    /// enough, because it only helps when the wording repeats exactly. In live play the model phrases its
    /// intent afresh nearly every reflection, so the timestamp reset anyway, roughly every
    /// <see cref="CognitiveModeComponent.ReflectionCooldown"/> seconds - and since the drain is some fifty
    /// times the gain, one reset wipes out everything accumulated. The arithmetic left boredom structurally
    /// unable to exceed about 0.08 no matter how long an AI stood in one corridor, which made
    /// <c>Restlessness</c> a permanently near-zero desire and matched exactly what live dumps showed
    /// (скука=0.00 on every passenger, every time).
    ///
    /// Re-narrating an activity is not the same as doing something new, so it should never have relieved
    /// boredom in the first place. Time in one place, and company, are things boredom genuinely responds to.
    /// </summary>
    private float BoredomDelta(EntityUid uid, NeedsComponent needs, float curiosity)
    {
        if (!HasComp<IntentComponent>(uid) || !TryComp<LandmarkPerceptionComponent>(uid, out var landmark))
            return 0f;

        var areaAge = (_timing.CurTime - landmark.AreaEnteredAt).TotalSeconds;
        var justFinishedTalking = TryComp<ConversationComponent>(uid, out var conversation) &&
            conversation.State == ConversationState.None &&
            _timing.CurTime < conversation.CooldownUntil;

        if (areaAge < needs.BoredomGraceSeconds || justFinishedTalking)
            return -needs.BoredomResetPerSecond;

        return needs.BaseBoredomGainPerSecond * (0.5f + curiosity);
    }

    /// <summary>
    /// Applies a one-off change to fatigue, e.g. from an operator while the AI player is resting.
    /// </summary>
    public void ModifyFatigue(EntityUid uid, float delta, NeedsComponent? needs = null)
    {
        if (!Resolve(uid, ref needs, false))
            return;

        needs.Fatigue = Math.Clamp(needs.Fatigue + delta, 0f, 1f);
    }

    /// <summary>
    /// Applies a one-off change to safety, e.g. from <see cref="DangerSystem"/> when the AI player is attacked.
    /// </summary>
    public void ModifySafety(EntityUid uid, float delta, NeedsComponent? needs = null)
    {
        if (!Resolve(uid, ref needs, false))
            return;

        needs.Safety = Math.Clamp(needs.Safety + delta, 0f, 1f);
    }
}
