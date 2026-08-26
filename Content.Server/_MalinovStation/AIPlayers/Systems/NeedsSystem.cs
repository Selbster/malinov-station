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
    /// AI Players 0.6: positive while the current intent and current area have both been unchanged for longer
    /// than <see cref="NeedsComponent.BoredomGraceSeconds"/> (scaled by <see cref="PersonalityComponent.Curiosity"/>),
    /// negative (draining back toward 0) while either is still fresh or a conversation only just ended. Reads
    /// <see cref="IntentComponent"/>/<see cref="LandmarkPerceptionComponent"/> directly rather than needing a
    /// separate "did this change since last tick" flag - both already carry a timestamp of when they last
    /// changed, and neither exists on a legacy (non-cognitive) AI player, which is what keeps boredom
    /// naturally inert for one without an explicit <see cref="CognitiveModeComponent"/> check.
    /// </summary>
    private float BoredomDelta(EntityUid uid, NeedsComponent needs, float curiosity)
    {
        if (!TryComp<IntentComponent>(uid, out var intent) || !TryComp<LandmarkPerceptionComponent>(uid, out var landmark))
            return 0f;

        var intentAge = (_timing.CurTime - intent.ChosenAt).TotalSeconds;
        var areaAge = (_timing.CurTime - landmark.AreaEnteredAt).TotalSeconds;
        var justFinishedTalking = TryComp<ConversationComponent>(uid, out var conversation) &&
            conversation.State == ConversationState.None &&
            _timing.CurTime < conversation.CooldownUntil;

        var isFresh = intentAge < needs.BoredomGraceSeconds || areaAge < needs.BoredomGraceSeconds || justFinishedTalking;
        if (isFresh)
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
