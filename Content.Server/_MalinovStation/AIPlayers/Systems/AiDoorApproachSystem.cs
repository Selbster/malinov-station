using System.Numerics;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Interaction;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
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
/// </summary>
public sealed partial class AiDoorApproachSystem : EntitySystem
{
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private AccessReaderSystem _accessReader = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AiLodSystem _lod = default!;

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
            if (!HasComp<NPCSteeringComponent>(uid))
                continue;

            approach.ScanAccumulator -= frameTime;
            if (approach.ScanAccumulator > 0f)
                continue;

            approach.ScanAccumulator = approach.ScanCooldown * _lod.GetMultiplier(uid);
            TryFindDoor(uid, approach, xform);
        }
    }

    private void TryFindDoor(EntityUid uid, DoorApproachComponent approach, TransformComponent xform)
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
}
