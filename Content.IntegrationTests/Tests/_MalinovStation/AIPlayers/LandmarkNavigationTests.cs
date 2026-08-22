#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Navigation Controller v1 acceptance checks: an AI notices a real (test) station beacon it can actually
/// perceive, remembers it by name, and can later choose to go back to it via the "GoToKnownLocation" action -
/// with clean feedback if it guesses a name it doesn't actually know. Also proves the non-omniscience
/// invariant extends to location knowledge (mirrors <see cref="CognitiveStateTests"/>) and that the new scan
/// never runs for a legacy AI player (mirrors <see cref="CognitiveModeGatingTests"/>).
/// </summary>
[TestFixture]
public sealed class LandmarkNavigationTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string TestBeacon = "TestNavigationBeacon";
    private const string BeaconText = "Test Kitchen";

    private const string Map = "LandmarkNavigationTestMap";

    [TestPrototypes]
    private static readonly string LandmarkNavigationTestMap = @$"
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

- type: entity
  id: {TestBeacon}
  name: test beacon
  components:
  - type: ConfigurableNavMapBeacon
  - type: NavMapBeacon
    text: {BeaconText}
";

    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true,
        Dirty = true,
    };

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

    private int LandmarkMemoryCount(TestPair pair, EntityUid uid) =>
        pair.Server.EntMan.GetComponent<MemoryComponent>(uid).Memories.Count(m => m.Source == "landmark");

    /// <summary>Forces LandmarkPerceptionSystem to scan on the very next tick, then gives it a couple of ticks
    /// to actually run.</summary>
    private async Task ForceScan(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        await server.WaitPost(() => server.EntMan.GetComponent<LandmarkPerceptionComponent>(uid).ScanAccumulator = 0f);
        await pair.RunTicksSync(2);
    }

    /// <summary>
    /// The shared spawn area on this test map isn't actually empty - other incidental mobs (e.g. ambient
    /// wildlife spawned by station setup) can land nearby, and a mob's own collision fixture legitimately
    /// counts as "Opaque" the same way a wall does (you can't see through a person standing in your way
    /// either) - exactly the LOS gate LandmarkPerceptionSystem itself relies on. Tests that need a guaranteed
    /// clear line of sight call this once, right after spawning, to remove that incidental interference -
    /// distinct from BeaconBehindWall's *deliberate* obstruction test, and irrelevant to the radius/far-away
    /// tests, which only need the beacon itself to be out of range regardless of what else is nearby.
    /// </summary>
    private async Task ClearOtherMobsNearby(TestPair pair, EntityUid keep, EntityCoordinates center, float radius)
    {
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            var lookup = server.EntMan.System<EntityLookupSystem>();
            var nearby = new HashSet<Entity<Content.Shared.Mobs.Components.MobStateComponent>>();
            lookup.GetEntitiesInRange(center, radius, nearby);
            foreach (var mob in nearby)
            {
                if (mob.Owner != keep)
                    server.EntMan.DeleteEntity(mob.Owner);
            }
        });
    }

    /// <summary>
    /// SeedKnownBeacons pre-loads every real station beacon that already exists at spawn time as a known
    /// landmark - unlike Scan's own passive, LOS-gated discovery, this deliberately doesn't require the AI
    /// to have ever perceived the beacon: a real station worker already knows the general layout on day one.
    /// The beacon here is spawned BEFORE the AI player (order matters - this is a one-shot spawn-time seed,
    /// not a continuous scan), far enough away that Scan's own LOS/radius gate would never have found it -
    /// proving this really is the seed path, not a coincidental passive discovery.
    /// </summary>
    [Test]
    public async Task SpawnAiPlayer_SeedsKnownBeaconsAlreadyOnTheMap_RegardlessOfDistance()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            // The station entity's own Transform isn't necessarily on the same map as where characters
            // actually spawn (it's an organisational container, not a point on the playable grid) - spawn a
            // disposable AI first purely to get a real, on-grid coordinate reference to place the beacon at,
            // matching the same-map guarantee every other test in this file gets for free by spawning its
            // beacon relative to an already-real AI player's own coordinates.
            var scratchAi = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(scratchAi).Coordinates;
            server.EntMan.DeleteEntity(scratchAi);

            beacon = server.EntMan.SpawnEntity(TestBeacon, coords.Offset(new Vector2(500, 500)));

            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
        });

        await server.WaitAssertion(() =>
        {
            var memory = server.EntMan.GetComponent<MemoryComponent>(aiPlayer);
            var landmark = memory.Memories.SingleOrDefault(m => m.Source == "landmark" && m.Participants.Contains(beacon));

            Assert.That(landmark, Is.Not.Null,
                "A real beacon already on the map when the AI spawns should be pre-seeded as a known landmark immediately, with no scan/wait needed.");
            Assert.That(landmark!.Subject, Is.EqualTo(BeaconText));

            var known = server.System<MemorySystem>().FindKnownLocation(aiPlayer, BeaconText);
            Assert.That(known, Is.Not.Null, "The pre-seeded landmark should be resolvable through the same MemorySystem rails GoToKnownLocation already uses.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(beacon);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task LandmarkPerceptionSystem_BeaconWithinRangeAndLos_WritesLandmarkMemory()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            beacon = server.EntMan.SpawnEntity(TestBeacon, coords.Offset(new Vector2(3, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            var memory = server.EntMan.GetComponent<MemoryComponent>(aiPlayer);
            var landmark = memory.Memories.SingleOrDefault(m => m.Source == "landmark");

            Assert.That(landmark, Is.Not.Null, "Expected a landmark memory to have been recorded.");
            Assert.Multiple(() =>
            {
                Assert.That(landmark!.Subject, Is.EqualTo(BeaconText));
                Assert.That(landmark.Location, Is.Not.Null);
                Assert.That(landmark.Participants, Does.Contain(beacon));
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(beacon);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task LandmarkPerceptionSystem_BeaconOutsideScanRadius_WritesNoMemory()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            var scanRadius = server.EntMan.GetComponent<LandmarkPerceptionComponent>(aiPlayer).ScanRadius;
            // Well outside ScanRadius (default 10) - the AI cannot perceive this by any means.
            beacon = server.EntMan.SpawnEntity(TestBeacon, coords.Offset(new Vector2(scanRadius + 20f, 0)));
        });

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            Assert.That(LandmarkMemoryCount(pair, aiPlayer), Is.EqualTo(0),
                "A beacon well outside scan radius should leave no trace in memory.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(beacon);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task LandmarkPerceptionSystem_BeaconBehindWall_WritesNoMemory()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid beacon = default;
        EntityUid wall = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;

            // Within ScanRadius, but with a wall directly between the AI and the beacon.
            wall = server.EntMan.SpawnEntity("WallSolid", coords.Offset(new Vector2(1, 0)));
            beacon = server.EntMan.SpawnEntity(TestBeacon, coords.Offset(new Vector2(2, 0)));

            // Test setup sanity: confirm the wall really does block a raw unobstructed check between them.
            var interaction = server.System<SharedInteractionSystem>();
            Assert.That(interaction.InRangeUnobstructed(aiPlayer, beacon, 10f, CollisionGroup.Opaque), Is.False,
                "Test setup: the wall should actually block line of sight between the AI and the beacon.");
        });

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            Assert.That(LandmarkMemoryCount(pair, aiPlayer), Is.EqualTo(0),
                "A beacon behind a wall should leave no trace in memory, even within radius.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(beacon);
            server.EntMan.DeleteEntity(wall);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task LandmarkPerceptionSystem_SameBeaconScannedTwice_DedupesToOneMemory()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            beacon = server.EntMan.SpawnEntity(TestBeacon, coords.Offset(new Vector2(3, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        await ForceScan(pair, aiPlayer);
        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            Assert.That(LandmarkMemoryCount(pair, aiPlayer), Is.EqualTo(1),
                "Scanning the same beacon across multiple cooldown cycles should not create duplicate memories.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(beacon);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task GoToKnownLocationAction_KnownLocation_SetsForcedDestinationOnBlackboard()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityCoordinates destination = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            destination = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates.Offset(new Vector2(5, 5));

            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Kitchen\".", importance: 0.25f, source: "landmark",
                location: destination, subject: "Kitchen");

            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, "GoToKnownLocation", new GoToKnownLocationActionParams("Kitchen"), out var reason);
            Assert.That(ok, Is.True, reason);

            var htn = server.EntMan.GetComponent<HTNComponent>(aiPlayer);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out var stored, server.EntMan), Is.True);
            Assert.That(stored, Is.EqualTo(destination));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task GoToKnownLocationAction_UnknownLocation_IsRejectedAndBlackboardUntouched()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, "GoToKnownLocation", new GoToKnownLocationActionParams("Nonexistent Place"), out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("don't know"));

            var htn = server.EntMan.GetComponent<HTNComponent>(aiPlayer);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out _, server.EntMan), Is.False);
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Perception-boundary proof mirroring CognitiveStateTests: a beacon the AI never perceived must leave
    /// zero trace, including in CognitiveState.KnownLocations - proving that field is memory-backed, not a
    /// fresh NavMapSystem query at prompt-build time.
    /// </summary>
    [Test]
    public async Task UnperceivedBeacon_LeavesNoTraceInMemoryOrCognitiveState()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            beacon = server.EntMan.SpawnEntity(TestBeacon, coords.Offset(new Vector2(500, 500)));
        });

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            Assert.That(LandmarkMemoryCount(pair, aiPlayer), Is.EqualTo(0));

            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(aiPlayer);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.KnownLocations, Is.Empty,
                "An unperceived beacon should never appear in KnownLocations.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(beacon);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Legacy-gating proof mirroring CognitiveModeGatingTests: a plain AI player never gets
    /// LandmarkPerceptionComponent at all, so the scan provably never runs for it, not just "produces nothing
    /// useful."
    /// </summary>
    [Test]
    public async Task LegacyAiPlayer_NeverGetsLandmarkPerceptionOrMemories()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid beacon = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value; // legacy, no cognitiveMode
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            beacon = server.EntMan.SpawnEntity(TestBeacon, coords.Offset(new Vector2(3, 0)));
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<LandmarkPerceptionComponent>(aiPlayer), Is.False,
                "A legacy AI player should never receive LandmarkPerceptionComponent.");
            Assert.That(LandmarkMemoryCount(pair, aiPlayer), Is.EqualTo(0),
                "A legacy AI player standing right next to a real beacon should still gain zero landmark memories, since the scan never runs for it.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(beacon);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// End-to-end wiring proof: a hand-built cognitive decision proposing GoToKnownLocation for a remembered
    /// place actually reaches the blackboard, mirroring CognitiveActionLoopTests' style (no real LLM).
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_GoToKnownLocation_ForARememberedPlace_SetsForcedDestination()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityCoordinates destination = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            destination = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates.Offset(new Vector2(4, 0));

            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Kitchen\".", importance: 0.25f, source: "landmark",
                location: destination, subject: "Kitchen");

            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "hunger", "find_food", 0.8f, 0.9f, "I remember the kitchen is over there",
                "GoToKnownLocation", new Dictionary<string, string> { ["location"] = "Kitchen" });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);

            var htn = server.EntMan.GetComponent<HTNComponent>(aiPlayer);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out var stored, server.EntMan), Is.True);
            Assert.That(stored, Is.EqualTo(destination));

            var intent = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.That(intent.Name, Is.EqualTo("find_food"));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
