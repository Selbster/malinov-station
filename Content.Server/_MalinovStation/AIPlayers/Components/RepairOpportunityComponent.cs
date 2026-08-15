namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Tracks whether the AI player currently perceives a nearby damaged, repairable machine (Milestone 11).
/// Populated by <see cref="Systems.RepairOpportunitySystem"/>'s own line-of-sight-gated scan, mirroring
/// <see cref="DangerComponent"/>'s shape; read by <see cref="Systems.GoalSystem"/> and the
/// RepairMachineCompound HTN precondition/operator. Kept separate from <see cref="DangerComponent"/> and
/// <see cref="PerceptionComponent"/> since machines aren't characters and don't belong in either's scan.
/// </summary>
[RegisterComponent]
public sealed partial class RepairOpportunityComponent : Component
{
    /// <summary>The nearest currently-visible damaged machine with a RepairableComponent, if any.</summary>
    [ViewVariables]
    public EntityUid? NearbyRepairTarget;

    [ViewVariables]
    public float ScanAccumulator;

    [DataField]
    public float ScanCooldown = 3f;

    [DataField]
    public float ScanRadius = 10f;
}
