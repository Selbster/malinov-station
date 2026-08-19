namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Drives <see cref="Systems.LandmarkPerceptionSystem"/>'s own periodic scan for a nearby vanilla station
/// beacon (see <c>NavMapBeaconComponent</c>) the AI player hasn't already remembered, mirroring
/// <see cref="RepairOpportunityComponent"/>'s shape (its own accumulator/cooldown/radius, not folded into
/// <see cref="PerceptionComponent"/>'s character-only scope). Only ever added to AI players spawned in
/// cognitive mode - see <see cref="Systems.LandmarkPerceptionSystem"/> for why.
/// </summary>
[RegisterComponent]
public sealed partial class LandmarkPerceptionComponent : Component
{
    [ViewVariables]
    public float ScanAccumulator;

    [DataField]
    public float ScanCooldown = 3f;

    /// <summary>Same scale as <see cref="PerceptionComponent.VisionRadius"/>'s default - an AI can only come
    /// to know a place it could actually see.</summary>
    [DataField]
    public float ScanRadius = 10f;
}
