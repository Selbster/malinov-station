using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Pathfinding;
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
/// Deliberately does not pre-verify a remembered location is still reachable (no pathfind up front - HTN's
/// own routine replan cadence, a few hundred ms later, already recovers cleanly if it isn't): if
/// <c>MoveToOperator</c> fails to reach it, that single idle-wander attempt fails and the next planning pass
/// simply tries again, possibly picking a different remembered place or falling back to a random one - the
/// same "always finishes, a legitimate outcome, not a bug" philosophy <c>ActivateHeldToolOperator</c>'s own
/// doc comment establishes elsewhere in this tree, and never a worse guarantee than an idle AI occasionally
/// re-rolling where to wander.
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
        var knownLocations = _memory.GetKnownLocationNames(owner);
        if (knownLocations.Count == 0)
            return null;

        var name = _random.Pick(knownLocations);
        if (_memory.FindKnownLocation(owner, name) is not { } destination)
            return null;

        return new Dictionary<string, object> { { TargetCoordinates, destination } };
    }
}
