namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Drives <see cref="Systems.AiDoorApproachSystem"/>'s deterministic "walk straight up to a nearby closed door
/// and click it" behaviour, replacing vanilla context-steering's force-blended approach (Seek vs.
/// CollisionAvoidance vs. Separation) for the final few tiles specifically - the fight between those forces is
/// the documented cause of AI players "dancing" near doors. Added unconditionally to every AI player (not
/// cognitive-gated): the dancing/access-ignoring complaint is about baseline movement quality, not an
/// LLM-driven behaviour.
/// </summary>
[RegisterComponent]
public sealed partial class DoorApproachComponent : Component
{
    [ViewVariables]
    public float ScanAccumulator;

    /// <summary>Deliberately shorter than the ~3s opportunity-scan cadence elsewhere in this tree - this
    /// needs to notice a blocking door quickly enough that vanilla steering never gets the chance to start
    /// fighting itself over it.</summary>
    [DataField]
    public float ScanCooldown = 0.5f;

    [DataField]
    public float ApproachRadius = 3f;

    /// <summary>The door currently being walked straight at, if any - while set, this system overrides
    /// movement input for this entity every tick.</summary>
    [ViewVariables]
    public EntityUid? ActiveDoor;

    /// <summary>When the current ActiveDoor approach started, for the overall give-up timeout - guards
    /// against this system's own simple straight-line walk ever getting permanently stuck on a door it can
    /// physically reach but never actually approach cleanly (e.g. an awkward doorway geometry).</summary>
    [ViewVariables]
    public TimeSpan ActiveSince;

    /// <summary>Doors this AI recently failed to open (no access, or activated but never left Closed - e.g.
    /// no power/welded) and the time that memory expires. Own local state, deliberately not the vanilla
    /// NPCDeniedAccessComponent the existing door fix uses - keeps this system fully self-contained.</summary>
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> DeniedDoors = new();

    /// <summary>When ActiveDoor was last actually clicked (InteractionActivate called), if at all - null
    /// while still approaching. Gates a single attempt-then-wait-and-see cycle instead of re-clicking every
    /// tick once in range.</summary>
    [ViewVariables]
    public TimeSpan? LastAttemptAt;
}
