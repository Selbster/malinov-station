using Content.Server._MalinovStation.AIPlayers.Perception;

namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Drives periodic perception for an AI player: what it can currently see, gated by vision range and
/// unobstructed line-of-sight (see <see cref="Systems.PerceptionSystem"/>).
/// </summary>
[RegisterComponent]
public sealed partial class PerceptionComponent : Component
{
    [DataField]
    public float VisionRadius = 10f;

    /// <summary>
    /// How often (in seconds) to re-scan the surroundings.
    /// </summary>
    [DataField]
    public float PerceiveCooldown = 3f;

    /// <summary>
    /// Minimum time between logging a new "saw X" memory about the same entity, so standing in the same
    /// room as someone for minutes doesn't spam memory with duplicate low-importance sightings.
    /// </summary>
    [DataField]
    public float ResightCooldown = 300f;

    [ViewVariables]
    public float PerceiveAccumulator;

    [ViewVariables]
    public WorldObservation? LastObservation;

    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> LastSeen = new();
}
