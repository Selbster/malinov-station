namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Needs for an AI player that aren't already modelled by an existing vanilla component: hunger and
/// thirst come from <see cref="Content.Shared.Nutrition.Components.SatiationComponent"/>, health from
/// mob state/damage. All values are normalized: 0 is fully satisfied, 1 is critical.
/// </summary>
[RegisterComponent]
public sealed partial class NeedsComponent : Component
{
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Fatigue;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Stress;

    /// <summary>
    /// 0 = safe, 1 = actively threatened. Raised by <see cref="Systems.DangerSystem"/> when attacked; decays
    /// back toward 0 over time once the threat passes.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Safety;

    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float SocialNeed;

    /// <summary>
    /// AI Players 0.6: restlessness from doing the same thing in the same place for too long. Cognitive-only
    /// in practice - <see cref="Systems.NeedsSystem"/> only grows this when both
    /// <see cref="Components.IntentComponent"/> and <see cref="Components.LandmarkPerceptionComponent"/> are
    /// present (legacy AI players have neither), so this stays 0 and inert for a legacy AI player without
    /// needing an explicit <see cref="Components.CognitiveModeComponent"/> check. Scaled by
    /// <see cref="PersonalityComponent.Curiosity"/> - see <see cref="Systems.GoalSystem.ComputeCandidates"/>
    /// for how this turns into a "Restlessness" desire the cognitive LLM can actually reason about, never a
    /// hardcoded "boredom > X -&gt; random room" rule.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public float Boredom;

    /// <summary>
    /// Base fatigue gained per second while awake. Scaled by <see cref="PersonalityComponent.Laziness"/>.
    /// </summary>
    [DataField]
    public float BaseFatigueGainPerSecond = 0.0015f;

    /// <summary>
    /// Base social need gained per second while alone. Scaled by <see cref="PersonalityComponent.Sociability"/>.
    /// </summary>
    [DataField]
    public float BaseSocialNeedGainPerSecond = 0.0006f;

    [DataField]
    public float StressDecayPerSecond = 0.01f;

    [DataField]
    public float SafetyDecayPerSecond = 0.02f;

    /// <summary>Base boredom gained per second once both current intent and current area have been unchanged
    /// for longer than <see cref="BoredomGraceSeconds"/>. Scaled by <see cref="PersonalityComponent.Curiosity"/>.</summary>
    [DataField]
    public float BaseBoredomGainPerSecond = 0.003f;

    /// <summary>How fast boredom drains back down while the intent/area is still "fresh" (within
    /// <see cref="BoredomGraceSeconds"/> of having last changed), or a conversation only just ended.</summary>
    [DataField]
    public float BoredomResetPerSecond = 0.15f;

    /// <summary>How long the current intent/area must have been unchanged before boredom starts accumulating -
    /// avoids boredom growing the instant a new intent/area is picked.</summary>
    [DataField]
    public float BoredomGraceSeconds = 20f;
}
