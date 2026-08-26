using System.Numerics;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Deterministic "walk straight up to a nearby closed door and click it" behaviour for AI players (spec
/// section 15's movement-quality follow-up, driven by live playtest feedback) - replaces vanilla
/// context-steering's force-blended approach (Seek vs. CollisionAvoidance vs. Separation) for the final few
/// tiles specifically, since fighting between those forces is the documented cause of AI players "dancing"
/// near doors (see <see cref="Content.Server.NPC.Systems.NPCSteeringSystem"/>'s own upstream TODO comment).
/// Never touches any vanilla file: while an AI player isn't actively approaching a door, this system does
/// nothing and vanilla steering behaves completely unmodified - <see cref="NPCSteeringComponent"/> is never
/// unregistered, so <c>MoveToOperator</c>'s own per-tick check keeps working. Only while
/// <see cref="DoorApproachComponent.ActiveDoor"/> is set does this system overwrite that tick's movement
/// input, reusing exactly the same public fields <c>NPCSteeringSystem.SetDirection</c> itself writes
/// (<see cref="InputMoverComponent.CurTickSprintMovement"/> etc.) - never a custom movement/physics
/// implementation, and reusing vanilla's own door/access primitives
/// (<see cref="AccessReaderSystem.IsAllowed"/>, <see cref="SharedInteractionSystem.InteractionActivate"/>)
/// rather than reimplementing them.
///
/// Added unconditionally to every AI player at spawn (not cognitive-gated) - dancing/access-ignoring are
/// baseline movement-quality issues, not an LLM-driven behaviour.
///
/// AI Players 0.6.1: <see cref="TryFindDoor"/>'s candidate search used to be pure radius proximity - "nearest
/// closed door within <see cref="DoorApproachComponent.ApproachRadius"/>", with no regard for whether that
/// door had anything to do with where the AI was actually headed, so a travelling AI would walk off its own
/// route to open a door belonging to some unrelated room it merely happened to pass (spec sections 13-15).
/// The radius search remains (this system only ever handles the last few tiles), but every candidate now has
/// to pass <see cref="IsOnCurrentRoute"/> as well - see that method for the two route signals it uses and why
/// the computed path alone isn't sufficient to gate on.
/// </summary>
public sealed partial class AiDoorApproachSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AiLodSystem _lod = default!;

    /// <summary>How many upcoming polys of <see cref="NPCSteeringComponent.CurrentPath"/> to check for a door
    /// obstacle - bounded so this stays cheap and only ever considers the door the AI is actually about to
    /// walk into, not something far down a long corridor it hasn't reached yet.</summary>
    private const int MaxPathLookahead = 6;

    /// <summary>
    /// How far off the straight line toward the AI's actual destination a door may sit and still count as
    /// being on the way, for <see cref="LiesAheadOnTheWay"/>. Deliberately under one tile: a door set into the
    /// side wall of a corridor the AI is merely walking along sits a full tile off that line and must be
    /// ignored, while one straight ahead stays comfortably inside it even if the AI drifts a little.
    /// </summary>
    private const float RouteCorridorHalfWidth = 0.75f;

    /// <summary>How long a door stays "denied" (no access, or clicked but never opened) before this AI is
    /// willing to try it again - mirrors the existing vanilla NPCDeniedAccessComponent's own memory window.</summary>
    private static readonly TimeSpan DeniedMemoryDuration = TimeSpan.FromSeconds(90);

    /// <summary>After clicking a door, how long to wait and see whether it actually left Closed before
    /// giving up on it (covers "no power"/welded/any other silent CanOpen failure, without needing to
    /// enumerate or special-case the reason).</summary>
    private static readonly TimeSpan AttemptGracePeriod = TimeSpan.FromSeconds(0.5);

    /// <summary>Overall safety timeout for one approach attempt, regardless of what's blocking it - guards
    /// against this system's own simple straight-line walk ever getting permanently stuck (e.g. an awkward
    /// doorway geometry it can't actually reach in a straight line).</summary>
    private static readonly TimeSpan ApproachTimeout = TimeSpan.FromSeconds(3);

    private readonly HashSet<Entity<DoorComponent>> _nearbyDoors = new();

    public override void Initialize()
    {
        base.Initialize();

        // Must run after vanilla steering computes its own (force-blended) movement input, and before
        // physics consumes it, so this system's override - only written while actively approaching a door -
        // is the one that survives into that tick's physics step. Mirrors NPCSteeringSystem's own
        // UpdatesBefore(SharedPhysicsSystem) declaration.
        UpdatesAfter.Add(typeof(NPCSteeringSystem));
        UpdatesBefore.Add(typeof(SharedPhysicsSystem));
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DoorApproachComponent, TransformComponent, InputMoverComponent>();
        while (query.MoveNext(out var uid, out var approach, out var xform, out var mover))
        {
            if (approach.ActiveDoor is { } activeDoor)
            {
                ProcessActiveApproach(uid, approach, activeDoor, xform, mover);
                continue;
            }

            // Only look for a new door to approach while actually mid-move - nothing to override otherwise.
            if (!TryComp<NPCSteeringComponent>(uid, out var steering))
                continue;

            approach.ScanAccumulator -= frameTime;
            if (approach.ScanAccumulator > 0f)
                continue;

            approach.ScanAccumulator = approach.ScanCooldown * _lod.GetMultiplier(uid);
            TryFindDoor(uid, approach, xform, steering);
        }
    }

    private void TryFindDoor(EntityUid uid, DoorApproachComponent approach, TransformComponent xform, NPCSteeringComponent steering)
    {
        _nearbyDoors.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, approach.ApproachRadius, _nearbyDoors);

        EntityUid? nearest = null;
        var nearestDistance = float.MaxValue;

        foreach (var (doorUid, door) in _nearbyDoors)
        {
            if (doorUid == uid || Deleted(doorUid))
                continue;

            if (door.State != DoorState.Closed)
                continue;

            // AI Players 0.6.1: only ever engage a door that's actually part of the AI's current route - see
            // this system's own class doc comment. A door merely nearby but irrelevant to where the AI is
            // going is left alone entirely.
            if (!IsOnCurrentRoute(doorUid, steering, xform))
                continue;

            if (IsDenied(approach, doorUid))
                continue;

            var distance = (Transform(doorUid).Coordinates.Position - xform.Coordinates.Position).LengthSquared();
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearest = doorUid;
        }

        if (nearest is not { } chosen)
            return;

        approach.ActiveDoor = chosen;
        approach.ActiveSince = _timing.CurTime;
        approach.LastAttemptAt = null;
    }

    private void ProcessActiveApproach(EntityUid uid, DoorApproachComponent approach, EntityUid doorUid, TransformComponent xform, InputMoverComponent mover)
    {
        if (Deleted(doorUid) || !TryComp<DoorComponent>(doorUid, out var door))
        {
            approach.ActiveDoor = null;
            return;
        }

        if (door.State != DoorState.Closed)
        {
            // Already opening (we clicked it, or something/someone else did) - GentleApproach already lets
            // vanilla steering walk an opening door through cleanly from here. Job done, hand back.
            approach.ActiveDoor = null;
            return;
        }

        if (_timing.CurTime - approach.ActiveSince > ApproachTimeout)
        {
            MarkDenied(approach, doorUid);
            approach.ActiveDoor = null;
            return;
        }

        var doorXform = Transform(doorUid);
        var myPos = _transform.GetWorldPosition(xform);
        var doorPos = _transform.GetWorldPosition(doorXform);
        var distance = (doorPos - myPos).Length();

        if (distance > SharedInteractionSystem.InteractionRange)
        {
            WalkToward(uid, mover, myPos, doorPos);
            return;
        }

        if (approach.LastAttemptAt is { } lastAttempt)
        {
            if (_timing.CurTime - lastAttempt < AttemptGracePeriod)
                return; // Already clicked it - waiting to see whether it actually opens.

            // Grace period elapsed and it's still Closed (the check at the top of this method would have
            // already handed back if it changed) - no power, welded, or any other silent failure. Back off.
            MarkDenied(approach, doorUid);
            approach.ActiveDoor = null;
            return;
        }

        if (!_accessReader.IsAllowed(uid, doorUid))
        {
            MarkDenied(approach, doorUid);
            approach.ActiveDoor = null;
            return;
        }

        _interaction.InteractionActivate(uid, doorUid);
        approach.LastAttemptAt = _timing.CurTime;
    }

    /// <summary>The same 4 field writes NPCSteeringSystem.SetDirection performs internally on these exact
    /// public fields - direction must be pre-rotated by -GetParentGridAngle before writing here, matching
    /// SharedMoverController.AssertValidWish's own downstream RotateVec(parentRotation), the same transform
    /// NPCSteeringSystem.Context.cs's own offsetRot already applies for the identical reason.</summary>
    private void WalkToward(EntityUid uid, InputMoverComponent mover, Vector2 myWorldPos, Vector2 targetWorldPos)
    {
        var diff = targetWorldPos - myWorldPos;
        if (diff == Vector2.Zero)
            return;

        var worldDir = Vector2.Normalize(diff);
        var offsetRot = -_mover.GetParentGridAngle(mover);

        mover.CurTickSprintMovement = offsetRot.RotateVec(worldDir);
        mover.LastInputTick = _timing.CurTick;
        mover.LastInputSubTick = ushort.MaxValue;

        var ev = new SpriteMoveEvent(true);
        RaiseLocalEvent(uid, ref ev);
    }

    private void MarkDenied(DoorApproachComponent approach, EntityUid doorUid) =>
        approach.DeniedDoors[doorUid] = _timing.CurTime + DeniedMemoryDuration;

    private bool IsDenied(DoorApproachComponent approach, EntityUid doorUid) =>
        approach.DeniedDoors.TryGetValue(doorUid, out var expiry) && _timing.CurTime < expiry;

    /// <summary>
    /// AI Players 0.6.1: whether <paramref name="doorUid"/> is genuinely part of the route this AI is
    /// currently following, by either of two signals.
    ///
    /// <see cref="IsBlockingPath"/> is the precise one - the real computed
    /// <see cref="NPCSteeringComponent.CurrentPath"/> says an upcoming node is blocked by this exact door -
    /// but it can only answer while a path has actually resolved. Steering deliberately keeps seeking the
    /// destination directly in the meantime (an async path request is in flight, and on a freshly-built or
    /// partially off-grid area there may be no navigable graph to produce one at all), and that gap is
    /// precisely the regime this whole system exists to cover: the final few tiles where vanilla
    /// context-steering fights itself. So <see cref="LiesAheadOnTheWay"/> answers the same question
    /// geometrically against the AI's real destination (<see cref="NPCSteeringComponent.Coordinates"/> - the
    /// "current destination" spec section 15 asks this system to be aware of), never against mere proximity.
    ///
    /// Both signals are about the route; neither is a "nearest closed door" search (spec section 14).
    /// </summary>
    private bool IsOnCurrentRoute(EntityUid doorUid, NPCSteeringComponent steering, TransformComponent xform)
    {
        return IsBlockingPath(doorUid, steering) || LiesAheadOnTheWay(doorUid, steering, xform);
    }

    /// <summary>
    /// AI Players 0.6.1: whether <paramref name="doorUid"/> lies ahead of the AI and essentially straight
    /// along the way to its actual destination - "ahead" meaning past the AI in the direction of travel and
    /// not beyond the destination itself, "straight along" meaning within
    /// <see cref="RouteCorridorHalfWidth"/> of that line. A door beside the AI, behind it, or set into the
    /// side wall of the corridor it's passing through all fail this, which is exactly spec section 27's
    /// unrelated neighbouring door.
    /// </summary>
    private bool LiesAheadOnTheWay(EntityUid doorUid, NPCSteeringComponent steering, TransformComponent xform)
    {
        if (!steering.Coordinates.IsValid(EntityManager))
            return false;

        var myPos = _transform.GetWorldPosition(xform);
        var targetPos = _transform.ToMapCoordinates(steering.Coordinates).Position;

        var toTarget = targetPos - myPos;
        var distanceToTarget = toTarget.Length();
        if (distanceToTarget <= 0.01f)
            return false;

        var direction = toTarget / distanceToTarget;
        var toDoor = _transform.GetWorldPosition(Transform(doorUid)) - myPos;

        // How far along the way to the destination the door sits. Behind the AI, or past the destination
        // (plus a tile of slack for a door sitting right at it), means it isn't on this route.
        var along = Vector2.Dot(toDoor, direction);
        if (along <= 0f || along > distanceToTarget + 1f)
            return false;

        return (toDoor - direction * along).Length() <= RouteCorridorHalfWidth;
    }

    /// <summary>
    /// AI Players 0.6.1: whether <paramref name="doorUid"/> is the obstacle behind one of the next
    /// <see cref="MaxPathLookahead"/> polys of <paramref name="steering"/>'s own computed
    /// <see cref="NPCSteeringComponent.CurrentPath"/> - the same route data vanilla steering itself follows.
    /// </summary>
    private bool IsBlockingPath(EntityUid doorUid, NPCSteeringComponent steering)
    {
        var checkedPolys = 0;

        foreach (var poly in steering.CurrentPath)
        {
            if ((poly.Data.Flags & PathfindingBreadcrumbFlag.Door) != 0x0 && PolyAnchors(poly, doorUid))
                return true;

            if (++checkedPolys >= MaxPathLookahead)
                break;
        }

        return false;
    }

    /// <summary>Mirrors vanilla <c>NPCSteeringSystem.Obstacles.GetObstacleEntities</c> (private to that
    /// partial class, so re-read here rather than touching a vanilla file) - resolves the real entities
    /// anchored within a path poly's own tile box on its grid, the same way vanilla identifies which door is
    /// actually blocking a given poly.</summary>
    private bool PolyAnchors(PathPoly poly, EntityUid doorUid)
    {
        if (!TryComp<MapGridComponent>(poly.GraphUid, out var grid))
            return false;

        foreach (var ent in _mapSystem.GetLocalAnchoredEntities(poly.GraphUid, grid, poly.Box))
        {
            if (ent == doorUid)
                return true;
        }

        return false;
    }
}
