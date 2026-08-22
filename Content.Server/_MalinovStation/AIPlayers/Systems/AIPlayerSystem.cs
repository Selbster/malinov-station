using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.Mind;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Server.Station.Systems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Spawns and manages "AI players": NPC-driven entities that get a job and body exactly like a real
/// player character, but are never bound to a client session/mind. Attaches every AI-player-specific
/// component (Personality, Needs, Goal, Perception, Memory, Relationship, Conversation, Danger, Lod) at
/// spawn time - see each component's own doc comment for what it's responsible for.
/// </summary>
public sealed partial class AIPlayerSystem : EntitySystem
{
    [Dependency] private StationSpawningSystem _stationSpawning = default!;
    [Dependency] private MindSystem _mind = default!;
    [Dependency] private NPCSystem _npc = default!;
    [Dependency] private PersonalitySystem _personality = default!;
    [Dependency] private AiPlayerPersistenceSystem _persistence = default!;
    [Dependency] private LandmarkPerceptionSystem _landmarkPerception = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>
    /// Root HTN compound every AI player starts on. Milestone 1 just wanders (proves navigation);
    /// later milestones will replace this with a goal-driven root once the Goal System exists.
    /// </summary>
    private const string RootCompound = "AIPlayerRootCompound";

    /// <summary>
    /// Spawns a new AI player with the given job on a station.
    /// </summary>
    /// <param name="job">The job to assign, including its starting gear/loadout.</param>
    /// <param name="station">Station to spawn on. If null, a random active station is picked.</param>
    /// <param name="profile">Appearance/name profile to use. If null, a random one is generated.</param>
    /// <param name="persistentId">
    /// Admin-chosen stable identity for cross-round persistence (Milestone 9). If given and persistence is
    /// enabled, a saved personality/memories from a previous spawn under this same ID load in shortly after
    /// spawn (async), overwriting the fresh random roll below. Omit for a fully ephemeral AI player - the
    /// default, and the only behaviour available before this milestone.
    /// </param>
    /// <param name="cognitiveMode">
    /// Defaults to false, so every existing call site (and every AI player spawned before this parameter
    /// existed) is unaffected - only <c>aiplayer_spawn_cognitive</c> passes true. When true, adds
    /// <see cref="CognitiveModeComponent"/> plus the cognitive-only overlay components
    /// (<see cref="IntentComponent"/>/<see cref="DesireComponent"/>/<see cref="EmotionComponent"/>/
    /// <see cref="BeliefComponent"/>/<see cref="LandmarkPerceptionComponent"/>/
    /// <see cref="InteractionOpportunityComponent"/>/<see cref="ItemOpportunityComponent"/>) - everything else
    /// about spawning stays identical either way.
    /// </param>
    /// <returns>The spawned entity, or null if spawning failed (e.g. no station/spawn point available).</returns>
    public EntityUid? SpawnAiPlayer(
        ProtoId<JobPrototype> job,
        EntityUid? station = null,
        HumanoidCharacterProfile? profile = null,
        string? persistentId = null,
        bool cognitiveMode = false)
    {
        station ??= PickStation();
        if (station is null)
        {
            Log.Warning("Tried to spawn an AI player but no station is available.");
            return null;
        }

        profile ??= HumanoidCharacterProfile.Random();

        var mob = _stationSpawning.SpawnPlayerCharacterOnStation(station, job, profile);
        if (mob is not { } mobUid)
        {
            Log.Warning($"Failed to spawn an AI player body for job \"{job}\" on {ToPrettyString(station.Value)}.");
            return null;
        }

        // No CreateMind/SetUserId/ActorComponent: this entity is never bound to a client session.
        // MakeSentient still gives it movement, speech and examine, matching how ghost-role/job-entity
        // mobs (e.g. StationAiBrain) work today.
        _mind.MakeSentient(mobUid);

        var aiPlayer = AddComp<AIPlayerComponent>(mobUid);
        aiPlayer.Job = job;
        aiPlayer.PersistentId = persistentId;

        _personality.RandomizePersonality(mobUid);
        AddComp<NeedsComponent>(mobUid);
        AddComp<GoalComponent>(mobUid);
        AddComp<PerceptionComponent>(mobUid);
        AddComp<MemoryComponent>(mobUid);
        AddComp<RelationshipComponent>(mobUid);
        AddComp<ConversationComponent>(mobUid);
        AddComp<DangerComponent>(mobUid);
        AddComp<AiLodComponent>(mobUid);
        AddComp<RepairOpportunityComponent>(mobUid);
        AddComp<AiTraceStateComponent>(mobUid);
        AddComp<DoorApproachComponent>(mobUid);

        if (cognitiveMode)
        {
            AddComp<CognitiveModeComponent>(mobUid);
            AddComp<IntentComponent>(mobUid);
            AddComp<DesireComponent>(mobUid);
            AddComp<EmotionComponent>(mobUid);
            AddComp<BeliefComponent>(mobUid);
            AddComp<LandmarkPerceptionComponent>(mobUid);
            AddComp<InteractionOpportunityComponent>(mobUid);
            AddComp<ItemOpportunityComponent>(mobUid);

            // Pre-seed the station's real beacons as known landmarks (general "I already work here" layout
            // knowledge, not live perception) so a cognitive AI doesn't have to rediscover its own workplace
            // by wandering past every beacon first - see LandmarkPerceptionSystem.SeedKnownBeacons's own doc
            // comment for why this doesn't violate the "no omniscience" discipline elsewhere in this AI.
            _landmarkPerception.SeedKnownBeacons(mobUid, Transform(mobUid));
        }

        if (persistentId is not null)
            _persistence.RequestLoad(mobUid, persistentId);

        var htn = AddComp<HTNComponent>(mobUid);
        htn.RootTask = new HTNCompoundTask { Task = RootCompound };
        // Without this the pathfinder treats every closed door as impassable (see
        // PathfindingSystem.Common.cs / NPCSteeringSystem.Obstacles.cs), so AI players would never
        // leave a sealed room like arrivals. Vanilla door-using NPCs (dragon, xenos, monkeys) all set
        // this the same way via their prototype's blackboard.
        htn.Blackboard.SetValue(NPCBlackboard.NavInteract, true);
        // NavInteract alone only covers doors with no access requirement. Without this, every
        // access-locked door (e.g. a department's own airlock) is a wall to the pathfinder even for a
        // legitimately-badged AI player - see PathFlags.AccessInteract's doc comment for the real
        // per-door access check this triggers at steering time.
        htn.Blackboard.SetValue(NPCBlackboard.NavAccessInteract, true);
        // Without this, collision avoidance treats every nearby closed door as a threat to steer away
        // from - including the one the AI player is deliberately walking up to open - producing a visible
        // approach/retreat "dance" right up until the door finally opens (see PathFlags.GentleApproach).
        htn.Blackboard.SetValue(NPCBlackboard.NavGentleApproach, true);
        _npc.WakeNPC(mobUid, htn);

        return mobUid;
    }

    private EntityUid? PickStation()
    {
        var stations = new List<EntityUid>();
        var query = EntityQueryEnumerator<StationDataComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            stations.Add(uid);
        }

        return stations.Count == 0 ? null : _random.Pick(stations);
    }
}
