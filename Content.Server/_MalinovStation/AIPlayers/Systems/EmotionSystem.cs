using Content.Server._MalinovStation.AIPlayers.Components;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Reads/writes <see cref="EmotionComponent"/>. Same plain-clamped-accumulator shape as
/// <see cref="RelationshipSystem"/>: callers decide how much a given happening should move a field,
/// <see cref="Modify"/> just clamps and stores it. Every accumulator field (everything but Anxiety) decays
/// back toward 0 every tick; Anxiety is instead recomputed fresh from <see cref="NeedsComponent"/> each tick,
/// mirroring how <see cref="GoalSystem"/> reads Fatigue directly rather than shadowing it in a counter.
/// </summary>
public sealed partial class EmotionSystem : EntitySystem
{
    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<EmotionComponent, NeedsComponent>();
        while (query.MoveNext(out var uid, out var emotion, out var needs))
        {
            emotion.Fear = DecayTowardZero(emotion.Fear, emotion.DecayPerSecond, frameTime);
            emotion.Anger = DecayTowardZero(emotion.Anger, emotion.DecayPerSecond, frameTime);
            emotion.Sadness = DecayTowardZero(emotion.Sadness, emotion.DecayPerSecond, frameTime);
            emotion.Joy = DecayTowardZero(emotion.Joy, emotion.DecayPerSecond, frameTime);
            emotion.Confidence = DecayTowardZero(emotion.Confidence, emotion.DecayPerSecond, frameTime);

            emotion.Anxiety = Math.Clamp(needs.Fatigue * 0.4f + needs.Stress * 0.4f + needs.Safety * 0.2f, 0f, 1f);
        }
    }

    private static float DecayTowardZero(float value, float decayPerSecond, float frameTime) =>
        MathF.Max(0f, value - decayPerSecond * frameTime);

    /// <summary>
    /// Nudges the given emotion fields by the given deltas (positive only - these are event-driven spikes,
    /// not a general set operation). No-ops for an entity with no <see cref="EmotionComponent"/> (i.e. every
    /// legacy AI player), so this is always safe to call from a shared trigger site.
    /// </summary>
    public void Modify(
        EntityUid uid,
        float fearDelta = 0f,
        float angerDelta = 0f,
        float sadnessDelta = 0f,
        float joyDelta = 0f,
        float confidenceDelta = 0f,
        EmotionComponent? emotion = null)
    {
        if (!Resolve(uid, ref emotion, false))
            return;

        emotion.Fear = Math.Clamp(emotion.Fear + fearDelta, 0f, 1f);
        emotion.Anger = Math.Clamp(emotion.Anger + angerDelta, 0f, 1f);
        emotion.Sadness = Math.Clamp(emotion.Sadness + sadnessDelta, 0f, 1f);
        emotion.Joy = Math.Clamp(emotion.Joy + joyDelta, 0f, 1f);
        emotion.Confidence = Math.Clamp(emotion.Confidence + confidenceDelta, 0f, 1f);
    }
}
