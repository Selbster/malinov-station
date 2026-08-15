namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Tracks the two kinds of "dynamic danger event" an AI player currently reacts to (spec Milestone 7):
/// being attacked (drives Flee) and seeing another character incapacitated nearby (drives HelpInjured).
/// Populated by <see cref="Systems.DangerSystem"/>; read by <see cref="Systems.GoalSystem"/> and the
/// FleeCompound/HelpInjuredCompound HTN preconditions/operators.
/// </summary>
[RegisterComponent]
public sealed partial class DangerComponent : Component
{
    /// <summary>
    /// Who attacked this AI player most recently, if the threat is still considered "active"
    /// (see <see cref="ThreatExpiresAt"/>).
    /// </summary>
    [ViewVariables]
    public EntityUid? ThreatSource;

    [ViewVariables]
    public TimeSpan ThreatExpiresAt;

    /// <summary>
    /// How long a threat stays "active" (driving Flee) after the last hit, absent a new one.
    /// </summary>
    [DataField]
    public float ThreatDurationSeconds = 15f;

    /// <summary>
    /// A currently-visible character in a critical/dead mob state, if any (refreshed each Danger scan).
    /// </summary>
    [ViewVariables]
    public EntityUid? NearbyInjured;

    /// <summary>
    /// When each entity was last comforted, so HelpInjuredCompound doesn't repeat itself on the same
    /// person indefinitely.
    /// </summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> ComfortedAt = new();

    [DataField]
    public float ComfortCooldownSeconds = 60f;

    [ViewVariables]
    public float ScanAccumulator;

    [DataField]
    public float ScanCooldown = 2f;
}
