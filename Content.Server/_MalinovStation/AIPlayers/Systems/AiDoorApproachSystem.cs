using System.Linq;
using System.Numerics;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.NPC.Systems;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
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
/// (<see cref="SharedDoorSystem.CanOpen"/>, <see cref="SharedInteractionSystem.InteractionActivate"/>)
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
    [Dependency] private SharedDoorSystem _door = default!;
    [Dependency] private SharedMoverController _mover = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private AiLodSystem _lod = default!;
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private AiTraceSystem _trace = default!;
    [Dependency] private HTNSystem _htn = default!;

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

    /// <summary>How long to let a prying do-after run before concluding it is not going to finish. Generous
    /// next to <see cref="ApproachTimeout"/> because levering a door is genuinely slow work.</summary>
    private static readonly TimeSpan PryTimeout = TimeSpan.FromSeconds(25);

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

            // Runs whether or not the AI is currently moving: the whole point is for pathfinding to already
            // know which doors are shut to this character by the time it picks a route.
            approach.AccessScanAccumulator -= frameTime;
            if (approach.AccessScanAccumulator <= 0f)
            {
                approach.AccessScanAccumulator = approach.AccessScanCooldown * _lod.GetMultiplier(uid);
                ScanForInaccessibleDoors(uid, approach, xform);
            }

            // Only look for a new door to approach while actually mid-move - nothing to override otherwise.
            if (!TryComp<NPCSteeringComponent>(uid, out var steering))
                continue;

            approach.ScanAccumulator -= frameTime;
            if (approach.ScanAccumulator > 0f)
                continue;

            approach.ScanAccumulator = approach.ScanCooldown * _lod.GetMultiplier(uid);

            // Judge the whole journey before judging the next few steps of it. If the route leads through
            // something that will not open, there is no point looking for a door to walk up to on the way.
            ReviewCurrentRoute(uid, approach, steering);
            if (approach.ActiveDoor is not null)
                continue;

            TryFindDoor(uid, approach, xform, steering);
        }
    }

    /// <summary>
    /// AI Players 0.6.3: works out in advance which nearby doors this AI has no access to, and tells
    /// pathfinding, so it never routes through them in the first place.
    ///
    /// This is the fix for a problem every previous attempt missed by looking in the wrong place. AI players
    /// are spawned with <c>NavAccessInteract</c> (see <c>AIPlayerSystem</c>), which becomes
    /// <see cref="PathFlags.AccessInteract"/> - telling the pathfinder that access-controlled doors are
    /// passable. That flag says nothing about whether <em>this</em> character holds the access, so the router
    /// happily plans straight through a door the AI can never open; vanilla <c>NPCSteeringSystem</c> then walks
    /// it over and clicks, discovers the denial, and only *then* records it. The AI trying doors it cannot open
    /// was therefore never this system's approach behaviour at all - gating that (as earlier milestones did)
    /// could not have fixed it.
    ///
    /// <see cref="NPCDeniedAccessComponent"/> is vanilla's own channel for "do not route me through this" -
    /// <c>PathfindingSystem.GetDeniedTiles</c> reads it when building a path request. Populating it up front
    /// rather than by collision is the whole change.
    ///
    /// Uses <see cref="SharedDoorSystem.HasAccess"/> rather than <c>CanOpen</c>: access is the stable,
    /// knowable-in-advance part and costs only component lookups, whereas CanOpen raises an event per door and
    /// answers questions (bolts, power) that change moment to moment and are better caught close up, which
    /// <see cref="TryFindDoor"/> still does.
    /// </summary>
    private void ScanForInaccessibleDoors(EntityUid uid, DoorApproachComponent approach, TransformComponent xform)
    {
        PruneExpiredDenials(approach);

        _nearbyDoors.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, approach.AccessScanRadius, _nearbyDoors);

        foreach (var (doorUid, door) in _nearbyDoors)
        {
            if (doorUid == uid || Deleted(doorUid))
                continue;

            // An open door is passable regardless of what it would take to open it.
            if (door.State != DoorState.Closed)
                continue;

            // AI Players 0.6.3: the ordered judgement, asked without the momentary half - see JudgeDoor.
            // A door it could pry open is a way through, not a wall, so only a genuine Blocked is written off.
            var judgement = JudgeDoor(uid, doorUid, door, includeMomentaryState: false);
            if (judgement.Passability != DoorPassability.Blocked)
                continue;

            // Re-affirmed on every sweep rather than skipped once known: entries expire after
            // DeniedMemoryDuration, and letting one lapse while the AI is still standing next to an impassable
            // door just invites it to path into the thing again ninety seconds later.
            var alreadyKnown = IsDenied(approach, doorUid);

            // Silent: this is routing knowledge, not an experience. Being unable to enter Cargo is not
            // something a passenger needs to have walked into a door to know.
            MarkDenied(uid, approach, doorUid, judgement.Reason, blocking: false);

            // Deliberately does NOT ask HTN to replan here, though an earlier version of this scan did and it
            // was a bad mistake: idle wandering picks a *random* remembered place on every planning pass, so
            // forcing a replan always produces a different destination, which aborts the move already in
            // flight. Walking across a station full of locked doors meant a steady trickle of newly-discovered
            // ones, hence a steady stream of replans, hence the AI re-targeting every few seconds and never
            // arriving anywhere - a flood of "MoveToOperator (reason=PlanAborted)" in the trace.
            //
            // Nothing is lost by leaving the current route alone: the denial is recorded, so the *next* path
            // request routes around this door anyway, and the case that genuinely cannot wait - the door being
            // in the way right now - is handled precisely by AbandonRouteThrough as the AI reaches it.
            _ = alreadyKnown;
        }
    }

    /// <summary>Keeps both denial maps from growing for the whole round as an AI wanders past more and more
    /// doors. Expired entries are meaningless to <c>GetDeniedTiles</c> anyway - this just stops us carrying
    /// them around.</summary>
    private void PruneExpiredDenials(DoorApproachComponent approach)
    {
        foreach (var (doorUid, expiry) in approach.DeniedDoors)
        {
            if (_timing.CurTime < expiry && !Deleted(doorUid))
                continue;

            approach.DeniedDoors.Remove(doorUid);
        }
    }

    private void TryFindDoor(EntityUid uid, DoorApproachComponent approach, TransformComponent xform, NPCSteeringComponent steering)
    {
        _nearbyDoors.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, approach.ApproachRadius, _nearbyDoors);

        EntityUid? nearest = null;
        EntityUid? nearestTool = null;
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
            var onRoute = IsOnCurrentRoute(doorUid, steering, xform);

            // AI Players 0.6.3: whether this door will open for this AI is settled HERE, before a single step
            // is taken toward it - walking over to find out is itself the dithering reported from live play.
            // Same ordered judgement as the long-range sweep, plus the momentary half now that the answer will
            // still be current when it is acted on.
            var judgement = JudgeDoor(uid, doorUid, door, includeMomentaryState: true);

            if (judgement.Passability == DoorPassability.Blocked)
            {
                // Remembered as impassable either way (so pathfinding routes around it), but only treated as a
                // real event - memory, abandoned journey - when it was genuinely in the way. Otherwise merely
                // walking down a corridor past a dozen locked doors would fill the AI's head with them.
                MarkDenied(uid, approach, doorUid, judgement.Reason, blocking: onRoute);

                // Calling off a journey needs the stronger of the two route signals. LiesAheadOnTheWay is
                // geometric - roughly along the line to the destination - which is the right test for "should
                // I walk over and open this", because it still answers while a path request is in flight. It
                // is the wrong test for "is my trip impossible": a door set into the side of a corridor the AI
                // is merely passing through satisfies it without being in the way at all. Live play showed the
                // cost - an exploration trip abandoned over a counter hatch beside the route, reported as
                // "AccessDenied: это окошко на стойке, а не проход".
                //
                // The computed path is the honest answer to that question, so only it may end a journey.
                if (IsBlockingPath(doorUid, steering))
                    AbandonRouteThrough(uid, judgement.Reason);

                continue;
            }

            if (!onRoute)
                continue;

            if (IsDenied(approach, doorUid))
                continue;

            var distance = (Transform(doorUid).Coordinates.Position - xform.Coordinates.Position).LengthSquared();
            if (distance >= nearestDistance)
                continue;

            nearestDistance = distance;
            nearest = doorUid;
            nearestTool = judgement.Passability == DoorPassability.Pry ? judgement.Tool : null;
        }

        if (nearest is not { } chosen)
            return;

        approach.ActiveDoor = chosen;
        approach.ActiveSince = _timing.CurTime;
        approach.LastAttemptAt = null;
        approach.PryTool = nearestTool;
        approach.PryingSince = null;
    }

    private void ProcessActiveApproach(EntityUid uid, DoorApproachComponent approach, EntityUid doorUid, TransformComponent xform, InputMoverComponent mover)
    {
        if (Deleted(doorUid) || !TryComp<DoorComponent>(doorUid, out var door))
        {
            ClearApproach(approach);
            return;
        }

        if (door.State != DoorState.Closed)
        {
            // Already opening (we clicked it, we pried it, or something/someone else did) - GentleApproach
            // already lets vanilla steering walk an opening door through cleanly from here. Job done.
            ClearApproach(approach);
            return;
        }

        // Levering a door apart takes far longer than walking to one, so a pry already under way is judged
        // against its own clock rather than being written off as a failed approach.
        if (approach.PryingSince is { } pryStarted)
        {
            if (_timing.CurTime - pryStarted <= PryTimeout)
            {
                HoldStill(mover);
                return;
            }

            MarkDenied(uid, approach, doorUid, "вскрыть её не вышло");
            ClearApproach(approach);
            return;
        }

        if (_timing.CurTime - approach.ActiveSince > ApproachTimeout)
        {
            MarkDenied(uid, approach, doorUid, "до неё не получается дойти");
            ClearApproach(approach);
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
            {
                // Already clicked it - stand still and see whether it opens.
                //
                // Standing still has to be said out loud. Writing no movement at all does not mean the AI
                // stops: this system runs after vanilla steering, so a tick we leave alone is a tick steering
                // owns, and steering goes on pushing toward a destination that lies beyond the very door we
                // are waiting on. Half a second of that, then our own straight approach again, then steering
                // again - which from the outside is a character jittering back and forth against a door, and
                // is what live play reported as the most irritating thing to watch.
                //
                // While this system owns an approach it owns every tick of it, waiting included.
                HoldStill(mover);
                return;
            }

            // Grace period elapsed and it's still Closed (the check at the top of this method would have
            // already handed back if it changed) - no power, welded, or any other silent failure. Back off.
            MarkDenied(uid, approach, doorUid, "она не открывается");
            ClearApproach(approach);
            return;
        }

        // Re-asked on arrival with the same ordered judgement the approach was authorised by - a door can be
        // bolted, welded or lose power during the few seconds it took to walk over.
        var judgement = JudgeDoor(uid, doorUid, door, includeMomentaryState: true);

        if (judgement.Passability == DoorPassability.Blocked)
        {
            MarkDenied(uid, approach, doorUid, judgement.Reason);
            ClearApproach(approach);
            return;
        }

        if (judgement.Passability == DoorPassability.Pry)
        {
            TryStartPrying(uid, approach, doorUid, judgement.Tool);
            return;
        }

        _interaction.InteractionActivate(uid, doorUid);
        approach.LastAttemptAt = _timing.CurTime;
    }

    /// <summary>
    /// Levers an unpowered door apart. The tool has to reach a hand first - a crowbar in the backpack opens
    /// nothing - and a character with both hands full simply cannot do this, which is a denial like any other
    /// rather than a silent no-op.
    /// </summary>
    private void TryStartPrying(EntityUid uid, DoorApproachComponent approach, EntityUid doorUid, EntityUid? tool)
    {
        if (tool is not { } pryTool || Deleted(pryTool))
        {
            MarkDenied(uid, approach, doorUid, "вскрывать её нечем");
            ClearApproach(approach);
            return;
        }

        if (!_hands.IsHolding(uid, pryTool) && !_hands.TryPickupAnyHand(uid, pryTool))
        {
            MarkDenied(uid, approach, doorUid, "лом не взять в руки");
            ClearApproach(approach);
            return;
        }

        if (!_prying.TryPry(doorUid, uid, out var doAfter, pryTool) || doAfter is null)
        {
            MarkDenied(uid, approach, doorUid, "вскрыть её не выходит");
            ClearApproach(approach);
            return;
        }

        approach.PryingSince = _timing.CurTime;
    }

    /// <summary>Drops every trace of the current approach, including a half-planned pry. Kept in one place so
    /// a new exit path cannot leave <see cref="DoorApproachComponent.PryTool"/> pointing at a door the AI has
    /// already given up on.</summary>
    private static void ClearApproach(DoorApproachComponent approach)
    {
        approach.ActiveDoor = null;
        approach.PryTool = null;
        approach.PryingSince = null;
        approach.LastAttemptAt = null;
    }

    /// <summary>The same 4 field writes NPCSteeringSystem.SetDirection performs internally on these exact
    /// public fields - direction must be pre-rotated by -GetParentGridAngle before writing here, matching
    /// SharedMoverController.AssertValidWish's own downstream RotateVec(parentRotation), the same transform
    /// NPCSteeringSystem.Context.cs's own offsetRot already applies for the identical reason.</summary>
    /// <summary>
    /// Plants the AI where it stands for this tick.
    ///
    /// The counterpart to <see cref="WalkToward"/>, and needed for the same reason: this system runs after
    /// vanilla steering and overwrites its movement input, so "do nothing" and "stand still" are different
    /// instructions. Doing nothing hands the tick back to steering.
    /// </summary>
    private void HoldStill(InputMoverComponent mover)
    {
        mover.CurTickSprintMovement = Vector2.Zero;
        mover.LastInputTick = _timing.CurTick;
        mover.LastInputSubTick = ushort.MaxValue;
    }

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

    /// <summary>
    /// AI Players 0.6.2: records a door this AI cannot get through - in three places, because each one is read
    /// by something different and only having the first was the cause of the "dancing at a door it will never
    /// open" behaviour reported from live play.
    ///
    /// (1) This system's own map, so it stops re-approaching the door itself. This is all that used to happen.
    /// (2) Vanilla <see cref="NPCDeniedAccessComponent"/>, which <c>PathfindingSystem.GetDeniedTiles</c> reads
    ///     when building a path request. Without it the router kept producing routes straight back through the
    ///     same locked door, so the AI walked up, failed, walked away, replanned into it again - forever. This
    ///     is the difference between "I refuse to touch that door" and "that door is not a way through".
    /// (3) A memory, so the cognitive layer actually learns it - "the bot does not understand it has no
    ///     access" was the other half of the same report. Cognitive-only, matching the convention every other
    ///     memory-writing side effect in this tree follows.
    ///
    /// Entries expire (see <see cref="DeniedMemoryDuration"/>): access can legitimately change, and a shutter
    /// someone opens from a console should stop being a wall.
    /// </summary>
    private void MarkDenied(EntityUid uid, DoorApproachComponent approach, EntityUid doorUid, string reason, bool blocking = true)
    {
        var expiry = _timing.CurTime + DeniedMemoryDuration;

        approach.DeniedDoors[doorUid] = expiry;
        EnsureComp<NPCDeniedAccessComponent>(uid).DeniedDoors[doorUid] = expiry;

        // A door that was never in the way is worth routing around, but is not an experience worth having.
        if (!blocking || !HasComp<CognitiveModeComponent>(uid))
            return;

        // Deduped against the recent past rather than written every retry - otherwise a door the AI passes
        // repeatedly would crowd out everything else it remembers.
        if (TryComp<MemoryComponent>(uid, out var memory) &&
            memory.Memories.Any(m => m.Source == "door-denied" && m.Participants.Contains(doorUid) &&
                _timing.CurTime - m.Timestamp < DeniedMemoryDuration))
        {
            return;
        }

        var where = TryComp<LandmarkPerceptionComponent>(uid, out var landmark) && landmark.CurrentAreaLabel is { } area
            ? $" в «{area}»"
            : string.Empty;

        _memory.AddMemory(
            uid,
            content: $"Ты не смог(ла) пройти через дверь{where}: {reason}.",
            importance: 0.35f,
            source: "door-denied",
            participants: new[] { doorUid },
            location: Transform(doorUid).Coordinates,
            emotionalWeight: -0.2f);
    }

    private bool IsDenied(DoorApproachComponent approach, EntityUid doorUid) =>
        approach.DeniedDoors.TryGetValue(doorUid, out var expiry) && _timing.CurTime < expiry;

    /// <summary>
    /// AI Players 0.6.3: gives up on the current destination when the door in the way is one this AI can never
    /// get through.
    ///
    /// Marking the door impassable stops future <em>path requests</em> going through it, but on its own it
    /// leaves the AI still committed to a destination it now has no route to: HTN keeps re-planning toward it,
    /// failing, and re-planning, which is the other half of what looks like dithering at a door. Dropping the
    /// destination ends that immediately and hands the decision back to cognition, with the reason attached so
    /// it picks somewhere else rather than the same place again - spec 0.6.2 section 19's AccessDenied
    /// feedback, arriving at the moment the route is actually known to be dead.
    /// </summary>
    private void AbandonRouteThrough(EntityUid uid, string reason)
    {
        if (!TryComp<HTNComponent>(uid, out var htn) ||
            !htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out _, EntityManager))
        {
            return;
        }

        htn.Blackboard.Remove<EntityCoordinates>(MoveToAction.ForcedDestinationKey);
        _trace.DecisionFeedback(uid, $"AccessDenied: {reason}");

        // Decide again now rather than standing about until the routine reflection interval comes round.
        if (TryComp<CognitiveModeComponent>(uid, out var cognitive))
            cognitive.ReflectionAccumulator = 0f;
    }

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
