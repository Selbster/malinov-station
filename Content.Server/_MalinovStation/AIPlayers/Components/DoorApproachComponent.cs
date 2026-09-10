using Content.Shared.DoAfter;
using Robust.Shared.Map;

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

    [ViewVariables]
    public float AccessScanAccumulator;

    /// <summary>How often (seconds) to work out, in advance, which doors nearby this AI simply has no access
    /// to. Far less often than the approach scan: access changes rarely, and this exists to inform pathfinding
    /// before it commits to a route, not to react to anything.</summary>
    [DataField]
    public float AccessScanCooldown = 5f;

    /// <summary>How far ahead to work that out. Deliberately much wider than
    /// <see cref="ApproachRadius"/> - the point is to know a door is shut to us while it is still a routing
    /// decision, not once we are already standing in front of it.</summary>
    [DataField]
    public float AccessScanRadius = 20f;

    /// <summary>The door currently being walked straight at, if any - while set, this system overrides
    /// movement input for this entity every tick.</summary>
    [ViewVariables]
    public EntityUid? ActiveDoor;

    /// <summary>Destination whose route authorized the current approach.</summary>
    public EntityCoordinates? ApproachDestination;

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

    public TimeSpan NextActivateAttempt;

    /// <summary>Minimum pause after opening begins, before handing movement back to steering.</summary>
    [DataField]
    public float OpeningWaitSeconds = 1f;

    [ViewVariables]
    public TimeSpan? OpeningWaitUntil;

    /// <summary>The prying tool this AI decided to force <see cref="ActiveDoor"/> with, if that is the plan.
    /// Null for an ordinary door it means to click.</summary>
    [ViewVariables]
    public EntityUid? PryTool;

    /// <summary>When the prying do-after was started, so a pry in progress is neither timed out as a failed
    /// walk nor mistaken for a click that silently did nothing.</summary>
    [ViewVariables]
    public TimeSpan? PryingSince;

    /// <summary>Limits permission and inventory rechecks while holding still for a pry.</summary>
    public TimeSpan NextPryCheck;

    /// <summary>The do-after owned by this approach, cancelled when its journey ends.</summary>
    [ViewVariables]
    public DoAfterId? PryDoAfter;
}
