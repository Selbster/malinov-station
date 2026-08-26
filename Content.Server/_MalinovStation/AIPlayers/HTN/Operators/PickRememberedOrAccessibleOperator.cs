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
/// Deliberately does not pre-verify a remembered location is still reachable itself (no pathfind up front here -
/// nothing in this class excludes a candidate just because it might be unreachable, "a bias, not a hard range
/// cutoff" per the weighting note below). Reachability is instead validated by the Idle branch's own
/// <c>MoveToOperator</c> step in htn.yml (<c>pathfindInPlanning: true</c>, AI Players 0.5) - if the pick this
/// call returns isn't actually reachable, that follow-up step's planning fails fast and the next replan simply
/// tries again, possibly picking a different remembered place or falling back to the random flood-fill - the
/// same "always finishes, a legitimate outcome, not a bug" philosophy <c>ActivateHeldToolOperator</c>'s own
/// doc comment establishes elsewhere in this tree.
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
    private PathfindingSystem _pathfinding = default!;
    private MemorySystem _memory = default!;

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
    }

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard, CancellationToken cancelToken)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (TryPickRemembered(owner) is { } remembered)
            return (true, remembered);

        var flags = _pathfinding.GetFlags(blackboard);
        blackboard.TryGetValue<float>(RangeKey, out var maxRange, _entManager);
        if (maxRange == 0f)
            maxRange = 7f;

        var path = await _pathfinding.GetRandomPath(owner, maxRange, cancelToken, flags: flags);
        if (path.Result != PathResult.Path)
            return (false, null);

        var target = path.Path.Last().Coordinates;
        return (true, new Dictionary<string, object> { { TargetCoordinates, target }, { PathfindKey, path } });
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
