namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// A cognitive-mode AI player's current mood, distinct from <see cref="PersonalityComponent"/> (fixed traits)
/// and <see cref="NeedsComponent"/> (physiological pressure). Only ever added to AI players spawned in
/// cognitive mode (see <see cref="Systems.AIPlayerSystem.SpawnAiPlayer"/>) - a legacy AI player never has this
/// component, so every write to it (<see cref="Systems.EmotionSystem.Modify"/>) is a safe no-op for them.
/// Fear/Anger/Sadness/Joy/Confidence are 0 (none) to 1 (intense) clamped accumulators that decay toward 0 over
/// time (see <see cref="Systems.EmotionSystem"/>); Anxiety is not accumulated at all - it's recomputed fresh
/// each tick straight from <see cref="NeedsComponent"/>, since "my needs are unmet" is a level, not a discrete
/// event to accumulate.
/// </summary>
[RegisterComponent]
public sealed partial class EmotionComponent : Component
{
    [ViewVariables]
    public float Fear;

    [ViewVariables]
    public float Anger;

    [ViewVariables]
    public float Sadness;

    [ViewVariables]
    public float Anxiety;

    [ViewVariables]
    public float Joy;

    [ViewVariables]
    public float Confidence;

    /// <summary>How fast each accumulator field (everything except Anxiety) decays back toward 0 per second.</summary>
    [DataField]
    public float DecayPerSecond = 0.02f;
}
