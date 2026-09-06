using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC.Components;
using Content.Server.NPC.Pathfinding;
using Content.Shared.Doors.Components;
using Content.Shared.NPC;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// AI Players 0.6.3: route validation - the missing step between "I have decided where to go" and "I am
/// walking there".
///
/// Vanilla's pathfinder is deliberately optimistic about access-controlled doors, and says so in its own
/// comments: a single per-tile flag cannot encode which characters a given door opens for, so planning assumes
/// it opens and the real check happens later, at the door. That is a sound division of labour - but it only
/// works if whoever commits to the route performs that check. Nobody did. The AI would set off, walk the whole
/// way, discover at the door that it could not pass, abandon, replan, and often set off again the same way.
/// From the outside that is a passenger grinding against a departmental airlock; in the trace it is an endless
/// run of MoveToOperator PlanAborted.
///
/// So this is the check that was missing, and it belongs here because <see cref="JudgeDoor"/> - the only thing
/// that knows whether a door opens for this particular character - already lives in this system. No vanilla
/// file is involved: the pathfinder still plans, steering still walks, and we simply stop asking either of
/// them for journeys this character cannot make.
/// </summary>
public sealed partial class AiDoorApproachSystem
{
    /// <summary>How far around a path node to look for the door occupying it. A path poly is one tile, so
    /// slightly under half a tile finds what stands on it without reaching into its neighbours.</summary>
    private const float PolyDoorSearchRadius = 0.45f;

    private readonly HashSet<Entity<DoorComponent>> _polyDoors = new();

    /// <summary>
    /// Whether <paramref name="path"/> passes through a door this character cannot get through, and which one.
    ///
    /// Judged with <c>includeMomentaryState: false</c> on purpose. A route is planned ahead of being walked,
    /// so the only fair questions are the ones whose answers will still hold on arrival: is this a doorway at
    /// all, does it open by hand, is it welded, does this ID open it. Bolts and power are decided at the door
    /// itself, where the reading is current - deciding them here would throw away good routes over a state
    /// that had already changed by the time the AI got there.
    /// </summary>
    public bool TryFindImpassableDoorOnRoute(
        EntityUid uid,
        IEnumerable<PathPoly> path,
        out EntityUid doorUid,
        out string reason)
    {
        doorUid = default;
        reason = string.Empty;

        foreach (var poly in path)
        {
            // Cheap filter first: the navmesh already knows which polys hold a door, so the anchored-entity
            // lookup below only runs for the handful of tiles that could possibly matter.
            if ((poly.Data.Flags & PathfindingBreadcrumbFlag.Door) == 0x0)
                continue;

            // Deliberately an ordinary lookup rather than the anchored-entity query vanilla steering uses for
            // the same job. Anchoring is a fair assumption for map-placed doors and a poor one in general - a
            // door that is unanchored, or freshly built, would simply become invisible to this check, and an
            // invisible obstacle is exactly the failure this whole system exists to prevent.
            _polyDoors.Clear();
            _lookup.GetEntitiesInRange(poly.Coordinates, PolyDoorSearchRadius, _polyDoors);

            foreach (var (ent, door) in _polyDoors)
            {
                // An open door is not an obstacle, and one already closing will be judged again next time.
                if (door.State != DoorState.Closed)
                    continue;

                var judgement = JudgeDoor(uid, ent, door, includeMomentaryState: false);
                if (judgement.Passability != DoorPassability.Blocked)
                    continue;

                doorUid = ent;
                reason = judgement.Reason;
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks the route the AI is actually walking right now, end to end, and gives up on it early if it leads
    /// through a door that will not open.
    ///
    /// Distinct from <see cref="IsBlockingPath"/>, which asks whether one specific door lies in the next few
    /// steps in order to decide whether to walk up and click it. This asks the opposite and larger question -
    /// is there anything anywhere along this route that makes the whole journey pointless - so the AI turns
    /// around at the moment the path resolves rather than after crossing half the station.
    /// </summary>
    private void ReviewCurrentRoute(EntityUid uid, DoorApproachComponent approach, NPCSteeringComponent steering)
    {
        if (steering.CurrentPath.Count == 0)
            return;

        if (!TryFindImpassableDoorOnRoute(uid, steering.CurrentPath, out var doorUid, out var reason))
            return;

        // Recorded before abandoning, so the next path request routes around it rather than re-proposing the
        // same doorway, and so the AI's own memory carries why the trip was called off.
        MarkDenied(uid, approach, doorUid, reason);
        AbandonRouteThrough(uid, reason);
    }
}
