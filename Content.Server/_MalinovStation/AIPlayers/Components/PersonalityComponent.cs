namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// A fixed-for-the-round set of personality traits for an AI player, normalized 0.0-1.0.
/// Read by <see cref="Content.Server._MalinovStation.AIPlayers.Systems.GoalSystem"/> (and later the Needs/LLM
/// layers) to make different AI players weigh the same situation differently.
/// </summary>
[RegisterComponent]
public sealed partial class PersonalityComponent : Component
{
    [DataField] public float Sociability = 0.5f;
    [DataField] public float Courage = 0.5f;
    [DataField] public float Curiosity = 0.5f;
    [DataField] public float Laziness = 0.5f;
    [DataField] public float Greed = 0.5f;
    [DataField] public float Aggression = 0.5f;
    [DataField] public float Loyalty = 0.5f;
    [DataField] public float RiskTolerance = 0.5f;
    [DataField] public float AuthorityRespect = 0.5f;
    [DataField] public float Professionalism = 0.5f;
    [DataField] public float Empathy = 0.5f;
    [DataField] public float Honesty = 0.5f;
    [DataField] public float Impulsiveness = 0.5f;
}
