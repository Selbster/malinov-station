namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// How much attention an AI player currently deserves, based on distance to the nearest connected player
/// (spec section 25). Systems doing genuinely expensive per-tick work (spatial queries, raycasts) scale
/// their update cooldowns by <see cref="AiLodComponent.GetMultiplier"/>; cheap arithmetic (e.g. Needs decay)
/// deliberately stays un-throttled so an AI player's state doesn't visibly "freeze" and jump when a player
/// wanders back into range.
/// </summary>
public enum AiLevelOfDetail : byte
{
    /// <summary>A real player is nearby - full update frequency.</summary>
    Full,

    /// <summary>No player is close, but one is on the same map within <see cref="AiLodComponent.ReducedRange"/>.</summary>
    Reduced,

    /// <summary>No player is anywhere near - minimum update frequency, and the LLM budget is reserved for
    /// AI players someone could actually notice.</summary>
    Background,
}

[RegisterComponent]
public sealed partial class AiLodComponent : Component
{
    [ViewVariables]
    public AiLevelOfDetail Level = AiLevelOfDetail.Full;

    [DataField]
    public float FullRange = 20f;

    [DataField]
    public float ReducedRange = 40f;

    [DataField]
    public float ReducedMultiplier = 4f;

    [DataField]
    public float BackgroundMultiplier = 15f;

    /// <summary>How often (in seconds) to reassess distance to players and update <see cref="Level"/>.</summary>
    [DataField]
    public float ReassessCooldown = 2f;

    [ViewVariables]
    public float ReassessAccumulator;

    public float GetMultiplier()
    {
        return Level switch
        {
            AiLevelOfDetail.Reduced => ReducedMultiplier,
            AiLevelOfDetail.Background => BackgroundMultiplier,
            _ => 1f,
        };
    }
}
