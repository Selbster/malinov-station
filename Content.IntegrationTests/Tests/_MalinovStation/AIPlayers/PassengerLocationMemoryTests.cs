#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.6, spec section 6: "known" (by name - <see cref="MemorySystem.GetKnownLocationNames"/>) and
/// "familiar" (actually visited - <see cref="LocationKnowledgeComponent"/>) are deliberately different things
/// here. Proves the visit/familiarity transition itself (<see cref="LandmarkPerceptionSystem.RecordVisit"/>),
/// that a place known only by name stays at zero familiarity until a real visit, and that
/// <see cref="MemorySystem.GetLocationSentiment"/> picks up the mood stamped at arrival - the concrete
/// "discovery" event <see cref="PassengerExplorationTests"/> then builds a whole decision on top of.
/// </summary>
[TestFixture]
public sealed class PassengerLocationMemoryTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string TestBeacon = "PassengerLocationMemoryTestBeacon";
    private const string BeaconText = "Test Cargo Bay";

    private const string Map = "PassengerLocationMemoryTestMap";

    [TestPrototypes]
    private static readonly string PassengerLocationMemoryTestMap = @$"
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

    /// <summary>Mirrors LandmarkNavigationTests' helper of the same shape.</summary>
    private async Task ForceScan(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        await server.WaitPost(() => server.EntMan.GetComponent<LandmarkPerceptionComponent>(uid).ScanAccumulator = 0f);
        await pair.RunTicksSync(2);
    }

    /// <summary>Mirrors LandmarkNavigationTests'/CognitiveRuntimeIntegrationTests' helper of the same shape.</summary>
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

    [Test]
    public async Task Visit_BumpsVisitCountAndFamiliarity_OnEachRealArrival()
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

        var memory = server.System<MemorySystem>();
        await server.WaitAssertion(() =>
        {
            var (visits, familiarity) = memory.GetLocationFamiliarity(aiPlayer, BeaconText);
            Assert.Multiple(() =>
            {
                Assert.That(visits, Is.EqualTo(1), "The first real arrival should count as one visit.");
                Assert.That(familiarity, Is.GreaterThan(0f).And.LessThan(1f));
            });
        });

        // Leave the beacon's range entirely, then come back - a genuine second arrival, not the same scan
        // tick re-confirming the same area.
        var transform = server.System<SharedTransformSystem>();
        var far = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates.Offset(new Vector2(200, 200));
        await server.WaitPost(() => transform.SetCoordinates(aiPlayer, far));
        await ForceScan(pair, aiPlayer);

        var near = server.EntMan.GetComponent<TransformComponent>(beacon).Coordinates.Offset(new Vector2(3, 0));
        await server.WaitPost(() => transform.SetCoordinates(aiPlayer, near));
        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            var (visits, familiarity) = memory.GetLocationFamiliarity(aiPlayer, BeaconText);
            Assert.Multiple(() =>
            {
                Assert.That(visits, Is.EqualTo(2), "A second genuine arrival (having left and come back) should count as another visit.");
                Assert.That(familiarity, Is.GreaterThan(0.15f), "Familiarity should have grown further after the second visit.");
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
    public async Task KnownByNameOnly_StaysAtZeroFamiliarity_UntilActuallyVisited()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var farAway = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates.Offset(new Vector2(500, 500));

            // Same shape LandmarkPerceptionSystem.SeedKnownBeacons already writes at spawn - "known by name",
            // never actually visited.
            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "Ты уже знаешь дорогу к «Cargo».", importance: 0.25f, source: "landmark",
                location: farAway, subject: "Cargo");

            var (visits, familiarity) = server.System<MemorySystem>().GetLocationFamiliarity(aiPlayer, "Cargo");
            Assert.Multiple(() =>
            {
                Assert.That(visits, Is.EqualTo(0));
                Assert.That(familiarity, Is.EqualTo(0f), "A place known only by name should have zero familiarity until actually visited.");
            });

            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(aiPlayer);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.KnownLocations.Any(l => l.Contains("Cargo") && l.Contains("ни разу не был")), Is.True,
                "The prompt-facing KnownLocations text should distinguish 'known by name' from 'actually familiar'.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task RecordVisit_StampsMoodAsSentiment_AndItSurfacesIntoThePrompt()
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

            var emotion = server.EntMan.GetComponent<EmotionComponent>(aiPlayer);
            emotion.Joy = 0.9f;
            emotion.Sadness = 0f;
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            var sentiment = server.System<MemorySystem>().GetLocationSentiment(aiPlayer, BeaconText);
            Assert.That(sentiment, Is.GreaterThan(0.2f),
                "A visit while in a good mood should leave a positive sentiment for the place.");

            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(aiPlayer);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.KnownLocations.Any(l => l.Contains(BeaconText) && l.Contains("тёплые воспоминания")), Is.True);
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(beacon);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
