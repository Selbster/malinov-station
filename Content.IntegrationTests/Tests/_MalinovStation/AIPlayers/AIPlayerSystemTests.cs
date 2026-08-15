#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mind.Components;
using Content.Shared.NPC;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

[TestFixture]
public sealed class AIPlayerSystemTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "AIPlayerTestMap";

    [TestPrototypes]
    private static readonly string AIPlayerTestMap = @$"
- type: gameMap
  id: {Map}
  mapName: {Map}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
        - type: StationJobs
          availableJobs:
            {Passenger}: [ -1, -1 ]
";

    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true,
        Dirty = true,
    };

    /// <summary>
    /// Starts the round (so a real station with spawn points exists) and returns it, the same way every test
    /// in this fixture needs to before it can spawn an AI player.
    /// </summary>
    private async Task<EntityUid> StartRoundAndGetStation(TestPair pair)
    {
        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.GameMap, Map);
        var ticker = server.System<GameTicker>();

        ticker.ToggleReadyAll(true);
        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        var station = server.EntMan.EntityQuery<StationDataComponent>().Select(x => x.Owner).FirstOrDefault();
        Assert.That(server.EntMan.EntityExists(station), "Expected a station to exist after round start.");
        return station;
    }

    /// <summary>
    /// Milestone 1 acceptance check: an AI player can be spawned onto a real station with a job/loadout and
    /// navigation (HTN) already running, without ever being bound to a client session/mind.
    /// </summary>
    [Test]
    public async Task SpawnAiPlayer_GivesJobAndHtn_WithNoPlayerSession()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;

        await server.WaitAssertion(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station);

            Assert.That(aiPlayer, Is.Not.Null, "AIPlayerSystem failed to spawn an AI player.");
            var uid = aiPlayer!.Value;
            Assert.That(server.EntMan.EntityExists(uid), Is.True);

            // Job assignment.
            Assert.That(server.EntMan.TryGetComponent<AIPlayerComponent>(uid, out var aiComp), Is.True);
            Assert.That(aiComp!.Job, Is.EqualTo(Passenger));

            // Navigation: driven by the HTN framework, not idle.
            Assert.That(server.EntMan.TryGetComponent<HTNComponent>(uid, out var htn), Is.True);
            Assert.That(htn!.RootTask.Task, Is.EqualTo("AIPlayerRootCompound"));
            Assert.That(server.EntMan.HasComponent<ActiveNPCComponent>(uid), Is.True, "AI player should be awake and updating.");

            // Sentient (can move/speak) but never bound to a client session or mind.
            Assert.That(server.EntMan.TryGetComponent<MindContainerComponent>(uid, out var mindContainer), Is.True);
            Assert.That(mindContainer!.HasMind, Is.False);
            Assert.That(server.EntMan.HasComponent<ActorComponent>(uid), Is.False);
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });

        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Milestone 2 acceptance check: a freshly spawned AI player has a randomized personality and blank needs,
    /// and starts out with the Idle goal since nothing is urgent yet.
    /// </summary>
    [Test]
    public async Task SpawnAiPlayer_HasPersonalityNeedsAndGoal_WithSaneDefaults()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;

        await server.WaitAssertion(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station);
            Assert.That(aiPlayer, Is.Not.Null);
            var uid = aiPlayer!.Value;

            Assert.That(server.EntMan.TryGetComponent<PersonalityComponent>(uid, out var personality), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(personality!.Sociability, Is.InRange(0f, 1f));
                Assert.That(personality.Laziness, Is.InRange(0f, 1f));
                Assert.That(personality.Professionalism, Is.InRange(0f, 1f));
            });

            Assert.That(server.EntMan.TryGetComponent<NeedsComponent>(uid, out var needs), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(needs!.Fatigue, Is.EqualTo(0f));
                Assert.That(needs.SocialNeed, Is.EqualTo(0f));
                Assert.That(needs.Stress, Is.EqualTo(0f));
            });

            Assert.That(server.EntMan.TryGetComponent<GoalComponent>(uid, out var goal), Is.True);
            Assert.That(goal!.CurrentGoal, Is.EqualTo(AIGoals.Idle));
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });

        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Milestone 2 acceptance check: once Fatigue is high, the Goal System switches the AI player's goal to
    /// Rest, which is what the RestCompound HTN branch (via FatiguePrecondition) reacts to.
    /// </summary>
    [Test]
    public async Task GoalSystem_PicksRest_WhenFatigueIsHigh()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;

        await server.WaitPost(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station);
            Assert.That(aiPlayer, Is.Not.Null);
            var uid = aiPlayer!.Value;

            var needs = server.EntMan.GetComponent<NeedsComponent>(uid);
            needs.Fatigue = 0.9f;

            // Force the next GoalSystem update to reconsider immediately instead of waiting out the cooldown.
            var goal = server.EntMan.GetComponent<GoalComponent>(uid);
            goal.ReconsiderAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer!.Value);
            Assert.That(goal.CurrentGoal, Is.EqualTo(AIGoals.Rest));
            Assert.That(goal.CurrentPriority, Is.GreaterThan(0.1f));
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });

        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Milestone 3 acceptance check: when another character is within vision range and unobstructed,
    /// PerceptionSystem notices them and records a first-impression memory + a relationship entry - the AI's
    /// only route to learning about other entities.
    /// </summary>
    [Test]
    public async Task Perception_NoticesNearbyCharacter_AndRecordsMemoryAndRelationship()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? observer = null;
        EntityUid? other = null;

        await server.WaitPost(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            observer = aiPlayers.SpawnAiPlayer(Passenger, station);
            other = aiPlayers.SpawnAiPlayer(Passenger, station);
            Assert.That(observer, Is.Not.Null);
            Assert.That(other, Is.Not.Null);

            // Put them in the same spot so line-of-sight is trivially unobstructed, then force an immediate scan.
            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            var observerCoords = server.EntMan.GetComponent<TransformComponent>(observer!.Value).Coordinates;
            xformSystem.SetCoordinates(other!.Value, observerCoords);

            server.EntMan.GetComponent<PerceptionComponent>(observer.Value).PerceiveAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var relationships = server.EntMan.GetComponent<RelationshipComponent>(observer!.Value);
            Assert.That(relationships.Relationships.ContainsKey(other!.Value), Is.True,
                "Observer should have formed a relationship entry for the nearby character.");

            var memory = server.EntMan.GetComponent<MemoryComponent>(observer.Value);
            Assert.That(memory.Memories.Any(m => m.Participants.Contains(other.Value)), Is.True,
                "Observer should have recorded a memory about the nearby character.");
        });

        await server.WaitPost(() =>
        {
            if (observer is { } o)
                server.EntMan.DeleteEntity(o);
            if (other is { } t)
                server.EntMan.DeleteEntity(t);
        });

        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Milestone 3 acceptance check: MemoryComponent never grows past MaxMemories - the least important
    /// (then oldest) entry is dropped to make room instead.
    /// </summary>
    [Test]
    public async Task MemorySystem_PrunesLeastImportant_WhenOverCap()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;

        await server.WaitAssertion(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station);
            Assert.That(aiPlayer, Is.Not.Null);
            var uid = aiPlayer!.Value;

            var memoryComp = server.EntMan.GetComponent<MemoryComponent>(uid);
            memoryComp.MaxMemories = 3;

            var memorySystem = server.System<MemorySystem>();
            memorySystem.AddMemory(uid, "trivial 1", importance: 0.1f, source: "test");
            memorySystem.AddMemory(uid, "trivial 2", importance: 0.1f, source: "test");
            memorySystem.AddMemory(uid, "important", importance: 0.9f, source: "test");
            // This should push out the least important entry (one of the "trivial" ones), not the important one.
            memorySystem.AddMemory(uid, "trivial 3", importance: 0.1f, source: "test");

            Assert.That(memoryComp.Memories, Has.Count.EqualTo(3));
            Assert.That(memoryComp.Memories.Any(m => m.Content == "important"), Is.True,
                "The most important memory should survive pruning.");
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });

        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Milestone 3 acceptance check: RelationshipSystem clamps values instead of letting repeated large
    /// swings run away to unbounded numbers.
    /// </summary>
    [Test]
    public async Task RelationshipSystem_ModifyRelationship_ClampsValues()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;
        EntityUid? other = null;

        await server.WaitAssertion(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station);
            other = aiPlayers.SpawnAiPlayer(Passenger, station);
            Assert.That(aiPlayer, Is.Not.Null);
            Assert.That(other, Is.Not.Null);

            var relationshipSystem = server.System<RelationshipSystem>();
            relationshipSystem.ModifyRelationship(aiPlayer!.Value, other!.Value, trustDelta: 5f, fearDelta: 5f);
            relationshipSystem.ModifyRelationship(aiPlayer.Value, other.Value, trustDelta: 5f, fearDelta: 5f);

            var data = relationshipSystem.GetRelationship(aiPlayer.Value, other.Value);
            Assert.Multiple(() =>
            {
                Assert.That(data.Trust, Is.EqualTo(1f));
                Assert.That(data.Fear, Is.EqualTo(1f));
            });
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
            if (other is { } o)
                server.EntMan.DeleteEntity(o);
        });

        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
