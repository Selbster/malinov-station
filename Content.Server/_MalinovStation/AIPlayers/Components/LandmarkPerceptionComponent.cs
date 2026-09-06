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

    /// <summary>
    /// AI Players 0.6: the nearest in-range beacon's <c>Text</c> right now, or null if none is in range -
    /// distinct from "ever remembered this beacon" (the landmark-memory check <see cref="Systems.LandmarkPerceptionSystem.Scan"/>
    /// already did before this milestone). Doubles as the "which area am I currently in" signal
    /// <see cref="Systems.NeedsSystem"/>'s boredom tracking reads and the visit-arrival trigger for
    /// <see cref="LocationKnowledgeComponent"/>.
    /// </summary>
    [ViewVariables]
    public string? CurrentAreaLabel;

    /// <summary>
    /// When this AI last arrived somewhere genuinely different - the clock
    /// <see cref="Systems.NeedsSystem"/> measures boredom against.
    ///
    /// Deliberately not "when CurrentAreaLabel last changed". Beacons are dense, so an idly wandering AI
    /// crosses in and out of range every few seconds, and each of those flips - including the ones into
    /// unnamed corridors - used to count as something happening. Boredom drains fifty times faster than it
    /// grows, so it never got more than a few seconds to accumulate, which is why live dumps kept reading
    /// скука=0,01 on a passenger that had been doing nothing at all for a minute.
    /// </summary>
    [ViewVariables]
    public TimeSpan AreaEnteredAt;

    /// <summary>The last <em>named</em> area this AI was in, so walking out into a corridor and back again is
    /// recognised as returning rather than as arriving somewhere new.</summary>
    [ViewVariables]
    public string? LastNamedArea;
}
