using System.Threading;
using System.Threading.Tasks;
using Content.Shared.NPC;
using Robust.Shared.Map;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.NPC.Pathfinding;

/// <summary>
/// Stores the in-progress data of a pathfinding request.
/// </summary>
public abstract class PathRequest
{
    public EntityCoordinates Start;

    public Task<PathResult> Task => Tcs.Task;
    public readonly TaskCompletionSource<PathResult> Tcs;

    public List<PathPoly> Polys = new();

    public bool Started = false;

    #region Pathfinding state

    public readonly Stopwatch Stopwatch = new();
    public PriorityQueue<ValueTuple<float, PathPoly>> Frontier = default!;
    public readonly Dictionary<PathPoly, float> CostSoFar = new();
    public readonly Dictionary<PathPoly, PathPoly> CameFrom = new();

    #endregion

    #region Data

    public readonly PathFlags Flags;
    public readonly int CollisionLayer;
    public readonly int CollisionMask;

    /// <summary>
    /// Tile keys (GraphUid/ChunkOrigin/TileIndex) to treat as impassable regardless of flags, even though
    /// they'd otherwise be allowed - doors this entity is known to have been denied real access to
    /// recently. Snapshotted up front since pathfinding runs across worker threads (see
    /// PathfindingSystem.GetDeniedTiles), so it can't safely be resolved from the entity manager mid-search.
    /// Lives on the base class (not just AStarPathRequest) so BFSPathRequest (PickAccessibleOperator's random
    /// idle/rest destination search) respects it too - otherwise a BFS search would keep optimistically
    /// routing an AI's random wander target back through/near a door it's already been denied, producing a
    /// plan that looks valid at planning time but reliably fails at steering time, replans, and repeats.
    /// </summary>
    public readonly IReadOnlySet<(EntityUid, Vector2i, byte)>? DeniedTiles;

    #endregion

    public PathRequest(
        EntityCoordinates start,
        PathFlags flags,
        int layer,
        int mask,
        CancellationToken cancelToken,
        IReadOnlySet<(EntityUid, Vector2i, byte)>? deniedTiles = null)
    {
        Start = start;
        Flags = flags;
        CollisionLayer = layer;
        CollisionMask = mask;
        DeniedTiles = deniedTiles;
        Tcs = new TaskCompletionSource<PathResult>(cancelToken);
    }
}

public sealed class AStarPathRequest : PathRequest
{
    public EntityCoordinates End;

    /// <summary>
    /// How close we need to be to the end node to be considered as arrived.
    /// </summary>
    public float Distance;

    public AStarPathRequest(
        EntityCoordinates start,
        EntityCoordinates end,
        PathFlags flags,
        float distance,
        int layer,
        int mask,
        CancellationToken cancelToken,
        IReadOnlySet<(EntityUid, Vector2i, byte)>? deniedTiles = null) : base(start, flags, layer, mask, cancelToken, deniedTiles)
    {
        Distance = distance;
        End = end;
    }
}

public sealed class BFSPathRequest : PathRequest
{
    /// <summary>
    /// How far away we're allowed to expand in distance.
    /// </summary>
    public float ExpansionRange;

    /// <summary>
    /// How many nodes we're allowed to expand
    /// </summary>
    public int ExpansionLimit;

    public BFSPathRequest(
        float expansionRange,
        int expansionLimit,
        EntityCoordinates start,
        PathFlags flags,
        int layer,
        int mask,
        CancellationToken cancelToken,
        IReadOnlySet<(EntityUid, Vector2i, byte)>? deniedTiles = null) : base(start, flags, layer, mask, cancelToken, deniedTiles)
        {
            ExpansionRange = expansionRange;
            ExpansionLimit = expansionLimit;
        }
}

/// <summary>
/// Stores the final result of a pathfinding request
/// </summary>
public sealed class PathResultEvent
{
    public PathResult Result;
    public readonly List<PathPoly> Path;

    public PathResultEvent(PathResult result, List<PathPoly> path)
    {
        Result = result;
        Path = path;
    }
}
