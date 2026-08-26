using Content.Shared.Roles;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Marks an entity as a server-controlled autonomous AI player. The entity has a job and body like
/// a normal player character (spawned via <see cref="Content.Server.Station.Systems.StationSpawningSystem"/>),
/// but is driven by the HTN NPC framework instead of a client session/mind.
/// </summary>
[RegisterComponent]
public sealed partial class AIPlayerComponent : Component
{
    /// <summary>
    /// The job this AI player was spawned with.
    /// </summary>
    [DataField]
    public ProtoId<JobPrototype>? Job;

    /// <summary>
    /// Admin-chosen stable identity for cross-round persistence (Milestone 9), or null for a fully ephemeral
    /// AI player (the default - matches every milestone before this one). Two AI players spawned with the
    /// same PersistentId (in the same or different rounds) share saved personality/memory state; see
    /// <see cref="Systems.AiPlayerPersistenceSystem"/>.
    /// </summary>
    [DataField]
    public string? PersistentId;

    /// <summary>
    /// AI Players 0.6: where this AI player woke up, captured once at spawn - purely informational (spec
    /// section 10: "must NOT become a mandatory return point"). Nothing reads this except prompt rendering
    /// (surfaced as one more place the LLM could reference if it ever has an actual in-character reason to go
    /// back); no movement/navigation code consumes it, by construction.
    /// </summary>
    [ViewVariables]
    public EntityCoordinates? SpawnCoordinates;
}
