namespace Content.Server.NPC.Components;

/// <summary>
/// Tracks doors an NPC has recently been denied real access to (see
/// <see cref="Content.Server.NPC.Pathfinding.PathFlags.AccessInteract"/>), keyed by door entity with the
/// time the entry expires. <see cref="Content.Server.NPC.Pathfinding.PathfindingSystem"/> reads this when
/// building a new path request so it stops optimistically routing the NPC back through a door it's already
/// confirmed it can't open, instead of endlessly re-picking the same unreachable route every replan. Added
/// lazily by NPCSteeringSystem the first time a door denies access; entries are skipped once expired rather
/// than actively pruned, since access can legitimately change later (a schedule, a job change, etc).
/// </summary>
[RegisterComponent]
public sealed partial class NPCDeniedAccessComponent : Component
{
    [ViewVariables]
    public Dictionary<EntityUid, TimeSpan> DeniedDoors = new();
}
