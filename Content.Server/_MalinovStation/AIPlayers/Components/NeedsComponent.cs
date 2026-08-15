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
}
