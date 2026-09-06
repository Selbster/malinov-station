using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>
/// AI-player-specific replacement for vanilla <c>PickAccessibleOperator</c> (used by vanilla <c>IdleCompound</c>,
/// which stays untouched - shared by every NPC in the game) - idle wandering should look purposeful rather
/// than uniformly random, per live playtest feedback. Tries a randomly-chosen remembered place first
/// (<see cref="MemorySystem.GetKnownLocationNames"/> - landmark/search-result memories, the same rails
/// Navigation's <c>GoToKnownLocation</c> action already uses). Falls back to the exact same random-accessible-
/// point flood-fill vanilla <c>PickAccessibleOperator</c> itself uses whenever there's no known location yet
/// - so legacy AI players (which never populate location memory, since LandmarkPerceptionSystem is
/// cognitive-only) always take this same fallback path unchanged.
///
/// AI Players 0.6.3 changed two things here, both from live play.
///
/// A remembered place is now checked for reachability before it is offered, which this operator previously
/// and deliberately did not do - leaving it to the Idle branch's own <c>MoveToOperator</c>
/// (<c>pathfindInPlanning: true</c>) to fail fast on a bad pick. That was adequate while the failure was
/// merely wasted planning; it stopped being adequate once live play showed what it looks like from the
/// outside, which is a passenger idly walking into a departmental airlock it has no access to, over and over.
/// The pathfinder is optimistic about access doors by design, so a route existing on paper is not the same as
/// this character being able to walk it - see <see cref="Systems.AiDoorApproachSystem.TryFindImpassableDoorOnRoute"/>.
///
/// And a destination, once chosen, is held onto until the AI gets there. HTN replans on its own cadence, and
/// every replan used to re-run this operator, which rolled a fresh point - aborting the plan already in
/// flight and sending the AI somewhere else mid-stride. About once a second, that reads as a character
/// shuffling on the spot rather than crossing a room, and it was the single behaviour live play found most
/// irritating to watch. Returning the same answer to the same question leaves the replanned plan identical,
/// so nothing is aborted.
///
/// The pick among remembered places is weighted toward nearer ones (weight <c>1 / (1 + distance)</c>), not
/// uniform: <see cref="Systems.LandmarkPerceptionSystem.SeedKnownBeacons"/> puts every station beacon in
/// memory at spawn regardless of distance, so a uniform pick here would send idle wandering across the whole
/// station roughly as often as next door - an earlier live playtest showed exactly that, as a cluster of
/// repeated MoveToOperator "PlanAborted" failures right after an Idle replan (a distant pick crossing more
/// doors/access checks than the fallback flood-fill's inherently-local, guaranteed-reachable point ever would).
/// A distant place stays reachable (never fully excluded, just rarer) - this is a bias, not a hard range
/// cutoff, and on its own wasn't enough to stop a low-access job from still hitting this near-every-replan on
/// a real map (AI Players 0.5's live validation) - hence htn.yml's own <c>pathfindInPlanning: true</c> fix
/// alongside this existing bias, rather than replacing it.
/// </summary>
public sealed partial class PickRememberedOrAccessibleOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;
    private PathfindingSystem _pathfinding = default!;
    private MemorySystem _memory = default!;
    private AiDoorApproachSystem _doors = default!;

    /// <summary>Blackboard key holding the destination this AI has already committed to wandering toward.</summary>
    private const string WanderTargetKey = "AiWanderTarget";

    /// <summary>Blackboard key holding when that commitment lapses.</summary>
    private const string WanderExpiryKey = "AiWanderTargetExpiry";

    /// <summary>
    /// How long a wander destination is held onto before being reconsidered. Long enough to actually get
    /// somewhere on foot, short enough that a route which quietly became impossible is not clung to.
    /// </summary>
    private static readonly TimeSpan WanderCommitment = TimeSpan.FromSeconds(20);

    /// <summary>How close counts as having arrived, at which point a fresh destination is due.</summary>
    private const float ArrivedDistance = 1.5f;

    [DataField("rangeKey", required: true)]
    public string RangeKey = string.Empty;

    [DataField("targetCoordinates")]
    public string TargetCoordinates = "TargetCoordinates";

    [DataField("pathfindKey")]
    public string PathfindKey = NPCBlackboard.PathfindKey;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _pathfinding = sysManager.GetEntitySystem<PathfindingSystem>();
        _memory = sysManager.GetEntitySystem<MemorySystem>();
        _doors = sysManager.GetEntitySystem<AiDoorApproachSystem>();
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var flags = _pathfinding.GetFlags(blackboard);
        blackboard.TryGetValue<float>(RangeKey, out var maxRange, _entManager);
        if (maxRange == 0f)
            maxRange = 7f;

        // AI Players 0.6.3: stick with the destination already chosen.
        //
        // This is the fix for the weaving that live play found most irritating to watch. HTN replans on its
        // own cadence, and every replan used to run this operator afresh, which rolled a *new* destination -
        // so the plan in flight was aborted mid-stride and the AI set off somewhere else. Repeat once a
        // second and a passenger crossing a room instead shuffles back and forth on the spot, which is
        // exactly what the trace showed as a steady run of MoveToOperator PlanAborted.
        //
        // Returning the same answer to the same question makes the replanned plan identical, so nothing is
        // aborted and the AI simply keeps walking. The commitment is dropped on arrival or on expiry, so this
        // is stickiness rather than stubbornness.
        if (IsStillHeadingSomewhere(blackboard, owner, out var committed))
        {
            return (true, new Dictionary<string, object>
            {
                { TargetCoordinates, committed },
                { WanderTargetKey, committed },
                { WanderExpiryKey, blackboard.GetValue<TimeSpan>(WanderExpiryKey) },
            });
        }

        // AI Players 0.6.3: a remembered place used to be handed straight over as a destination, with nobody
        // ever asking whether this character could actually get to it. That is the exact shape of the bug live
        // play kept showing - a passenger idly wandering toward a room behind a departmental airlock, walking
        // until the door stopped it, replanning, and setting off again. The random branch below never had this
        // problem, because a random destination comes back attached to a real path by construction.
        //
        // So the remembered branch now has to earn its destination the same way. If the route through is one
        // this character cannot make, the memory is simply not used this time and wandering falls through to
        // somewhere it can genuinely reach.
        if (TryPickRemembered(owner) is { } remembered
            && await IsWorthWalkingTo(owner, remembered, maxRange, flags, cancelToken))
        {
            return (true, Commit(remembered, (EntityCoordinates) remembered[TargetCoordinates]));
        }

        var path = await _pathfinding.GetRandomPath(owner, maxRange, cancelToken, flags: flags);
        if (path.Result != PathResult.Path)
            return (false, null);

        var target = path.Path.Last().Coordinates;
        return (true, Commit(
            new Dictionary<string, object> { { TargetCoordinates, target }, { PathfindKey, path } },
            target));
    }

    /// <summary>Stamps a freshly-chosen destination as the one this AI is now committed to.</summary>
    private Dictionary<string, object> Commit(Dictionary<string, object> effects, EntityCoordinates target)
    {
        effects[WanderTargetKey] = target;
        effects[WanderExpiryKey] = _timing.CurTime + WanderCommitment;
        return effects;
    }

    /// <summary>
    /// Whether this AI is already on its way somewhere and should be left to get on with it.
    ///
    /// Three ways the commitment ends, and each is a real answer rather than a timeout dressed up as one:
    /// it arrived, the destination stopped being a valid place, or it has been trying long enough that
    /// something has plainly gone wrong and a fresh look is due.
    /// </summary>
    private bool IsStillHeadingSomewhere(NPCBlackboard blackboard, EntityUid owner, out EntityCoordinates target)
    {
        target = default;

        if (!blackboard.TryGetValue<EntityCoordinates>(WanderTargetKey, out var held, _entManager) ||
            !blackboard.TryGetValue<TimeSpan>(WanderExpiryKey, out var expiry, _entManager))
        {
            return false;
        }

        if (_timing.CurTime >= expiry || !held.IsValid(_entManager))
            return false;

        if (!_entManager.TryGetComponent<TransformComponent>(owner, out var xform) ||
            !xform.Coordinates.TryDistance(_entManager, held, out var distance) ||
            distance <= ArrivedDistance)
        {
            return false;
        }

        target = held;
        return true;
    }

    /// <summary>
    /// Whether there is a route to this remembered place that this character can actually walk.
    ///
    /// Two ways to fail, and they are different failures: no path at all (walled off, off-grid, nothing
    /// navigable), or a path that exists on paper but runs through a door that will not open for this
    /// character. The pathfinder answers the first and is deliberately optimistic about the second, so the
    /// second is asked separately - see <see cref="AiDoorApproachSystem.TryFindImpassableDoorOnRoute"/>.
    /// </summary>
    private async Task<bool> IsWorthWalkingTo(
        EntityUid owner,
        Dictionary<string, object> remembered,
        float maxRange,
        PathFlags flags,
        CancellationToken cancelToken)
    {
        if (remembered[TargetCoordinates] is not EntityCoordinates target ||
            !_entManager.TryGetComponent<TransformComponent>(owner, out var xform))
        {
            return false;
        }

        var path = await _pathfinding.GetPathSafe(owner, xform.Coordinates, target, maxRange, cancelToken, flags);

        return path.Result == PathResult.Path &&
            !_doors.TryFindImpassableDoorOnRoute(owner, path.Path, out _, out _);
    }

    private Dictionary<string, object>? TryPickRemembered(EntityUid owner)
    {
        // AI Players 0.6.2: ranked candidates, not GetKnownLocationNames. That method orders by memory
        // importance, which SeedKnownBeacons gives every station beacon identically - so idle wander drew from
        // the same arbitrary five beacons for an entire round, and since the weighting below is strongly
        // distance-biased, an AI effectively ping-ponged between whichever two of those five happened to be
        // nearest. Live play showed exactly that: movement only ever between adjacent rooms, never anywhere
        // else. GetExplorationCandidates ranks by real familiarity and penalises somewhere just left, so idle
        // wander now varies sensibly among nearby places.
        //
        // The distance weighting itself is deliberately kept: idle wander is meant to be local pottering
        // about. Crossing the station is travel, which is the cognitive layer's job (GoToKnownLocation /
        // ExploreStation) - collapsing that distinction here would make the two indistinguishable.
        var knownLocations = _memory.GetExplorationCandidates(owner);
        if (knownLocations.Count == 0)
            return null;

        if (!_entManager.TryGetComponent<TransformComponent>(owner, out var ownerXform))
            return null;

        var ownerCoords = ownerXform.Coordinates;

        var candidates = new List<(EntityCoordinates Coordinates, float Weight)>();
        foreach (var name in knownLocations)
        {
            if (_memory.FindKnownLocation(owner, name) is not { } destination)
                continue;

            // Same-map distance only; a location TryDistance can't compare (different map) still gets picked
            // sometimes rather than silently excluded - falls back to the same weight a nearby pick would get.
            var weight = ownerCoords.TryDistance(_entManager, destination, out var distance)
                ? 1f / (1f + distance)
                : 1f;

            candidates.Add((destination, weight));
        }

        if (candidates.Count == 0)
            return null;

        var totalWeight = candidates.Sum(c => c.Weight);
        var roll = _random.NextFloat() * totalWeight;
        foreach (var candidate in candidates)
        {
            roll -= candidate.Weight;
            if (roll <= 0f)
                return new Dictionary<string, object> { { TargetCoordinates, candidate.Coordinates } };
        }

        return new Dictionary<string, object> { { TargetCoordinates, candidates[^1].Coordinates } };
    }
}
