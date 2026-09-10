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
using Content.Shared._MalinovStation.AIPlayers;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>Intentional travel telemetry and physical completion of a cognitive destination choice.</summary>
[TestFixture]
public sealed class PassengerTravelTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string Map = "PassengerTravelTestMap";

    [TestPrototypes]
    private static readonly string PassengerTravelTestMap = @$"
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

    private async Task<EntityUid> StartRoundAndGetStation(TestPair pair)
    {
        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.GameMap, Map);
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled, false);
        var ticker = server.System<GameTicker>();

        ticker.ToggleReadyAll(true);
        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        var station = server.EntMan.EntityQuery<StationDataComponent>().Select(x => x.Owner).FirstOrDefault();
        Assert.That(server.EntMan.EntityExists(station), "Expected a station to exist after round start.");
        return station;
    }

    [Test]
    public async Task TravelDecided_FiresOnlyForGoToKnownLocation()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Kitchen\".", importance: 0.25f, source: "landmark",
                location: coords.Offset(new Vector2(4, 0)), subject: "Kitchen");
        });

        var before = AiTraceSystem.TraceEventsMetric.WithLabels("TravelDecided").Value;

        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var continueDecision = new LlmCognitiveDecision(
                "curiosity", "продолжить", 0.3f, 0.5f, "Всё в порядке, продолжу как есть.",
                ContinueActivityAction.ActionName, new Dictionary<string, string>());
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, continueDecision), Is.True);
        });

        Assert.That(AiTraceSystem.TraceEventsMetric.WithLabels("TravelDecided").Value, Is.EqualTo(before),
            "ContinueActivity is not a travel decision and must not produce a TravelDecided trace.");

        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var travelDecision = new LlmCognitiveDecision(
                "boredom", "сменить_обстановку", 0.6f, 0.7f, "Хочу сменить обстановку.",
                GoToKnownLocationAction.ActionName, new Dictionary<string, string> { ["location"] = "Kitchen" });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, travelDecision), Is.True);
        });

        Assert.That(AiTraceSystem.TraceEventsMetric.WithLabels("TravelDecided").Value, Is.EqualTo(before + 1),
            "A successful GoToKnownLocation decision should produce exactly one TravelDecided trace.");

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>A named destination stays committed while the actor walks to it on a navigable floor.</summary>
    [Test]
    public async Task GoToKnownLocation_CreatesAGenuinePersistentMovementCommitment_NotJustAnIntentLabel()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityCoordinates destination = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var xform = server.EntMan.GetComponent<TransformComponent>(aiPlayer);
            destination = xform.Coordinates.Offset(new Vector2(-6, 0));
            var gridUid = xform.GridUid!.Value;
            var grid = server.EntMan.GetComponent<MapGridComponent>(gridUid);
            grid.CanSplit = false;
            var maps = server.System<SharedMapSystem>();
            var origin = maps.CoordinatesToTile(gridUid, grid, xform.Coordinates);
            var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            for (var x = -8; x <= 1; x++)
            for (var y = -1; y <= 1; y++)
                maps.SetTile(gridUid, grid, origin + new Vector2i(x, y), tile);
            server.System<Content.Server.Gravity.GravitySystem>().EnableGravity(gridUid);
            server.EntMan.GetComponent<Content.Shared.Gravity.GravityComponent>(gridUid).Inherent = true;
            server.EntMan.GetComponent<HTNComponent>(aiPlayer).RootTask = new HTNCompoundTask { Task = "ForcedMoveCompound" };
            server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator = 10000f;
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            goal.CurrentGoal = "Idle";
            goal.ReconsiderAccumulator = 10000f;

            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Bar\".", importance: 0.25f, source: "landmark",
                location: destination, subject: "Bar");
        });

        await pair.RunTicksSync(60);
        var startCoords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
        Assert.That(startCoords.TryDistance(server.EntMan, destination, out var initialDistance) && initialDistance > 3f, Is.True,
            "Test setup: the AI should start meaningfully far from the destination.");

        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "boredom", "исследовать_бар", 0.6f, 0.7f, "Хочу посмотреть, что происходит в баре.",
                GoToKnownLocationAction.ActionName, new Dictionary<string, string> { ["location"] = "Bar" });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);

            var htn = server.EntMan.GetComponent<HTNComponent>(aiPlayer);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out var stored, server.EntMan), Is.True);
            Assert.That(stored, Is.EqualTo(destination),
                "The commitment's target must be the real destination coordinates, not a placeholder.");
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.EqualTo(GoToKnownLocationAction.ActionName));
        });

        // Not an instantaneous label: the commitment must still be in effect several ticks later, exactly as an
        // in-progress extended action should be (AiBusyStateTests already proves the general busy-state
        // lifecycle end to end; this confirms GoToKnownLocation specifically participates in it for real).
        await pair.RunTicksSync(20);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.EqualTo(GoToKnownLocationAction.ActionName),
                "The movement commitment should still be active a moment later, not already resolved/discarded.");
            var current = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            Assert.That(current.TryDistance(server.EntMan, destination, out var stillFar) && stillFar > 1f, Is.True,
                "Applying the decision must not itself teleport the AI to the destination.");
        });

        var completed = false;
        for (var i = 0; i < 100 && !completed; i++)
        {
            await pair.RunTicksSync(5);
            await server.WaitPost(() => completed = server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction is null);
        }
        await server.WaitAssertion(() =>
        {
            var busy = server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer);
            Assert.That(completed, Is.True);
            Assert.That(busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed));
            var actual = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            Assert.That(actual.TryDistance(server.EntMan, destination, out var remaining) && remaining <= 1.5f, Is.True);
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
