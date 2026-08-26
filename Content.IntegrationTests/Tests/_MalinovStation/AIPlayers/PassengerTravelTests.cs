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
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.6, spec section 8/31: local reflex wander (untouched this milestone) must never be mistaken
/// for proof of intentional travel capability. Proves a <c>GoToKnownLocation</c> cognitive decision produces a
/// genuine, specific, persistent movement commitment - not an instantaneous label - that correctly resolves
/// once arrival happens (simulated the same established way <see cref="AiBusyStateTests"/> already does, since
/// real HTN pathfinding-to-completion isn't observable inside this integration test harness - see the
/// commitment test's own doc comment for what was directly confirmed while building this), and that the spec
/// section 34 travel telemetry (<see cref="AiTraceSystem.TravelDecided"/>) fires specifically for that decision
/// and nothing else.
/// </summary>
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

    /// <summary>
    /// Real HTN pathfinding-to-completion isn't observable inside this integration test harness (confirmed by
    /// direct instrumentation while building this test: <see cref="Content.Server.NPC.HTN.HTNComponent.Plan"/>
    /// stays null and no <see cref="Content.Server.NPC.Components.NPCSteeringComponent"/> is ever registered,
    /// across 1000+ ticks, for an otherwise-healthy awake NPC - vanilla HTN's own async planning job apparently
    /// never resolves under this harness's synchronous tick-stepping, a pre-existing engine/harness interaction
    /// unrelated to this milestone's own code). Every existing test in this codebase that touches
    /// <c>GoToKnownLocation</c> already works around exactly this by simulating arrival directly rather than
    /// watching real pathfinding complete (see <see cref="AiBusyStateTests.AiBusyStateSystem_GoToKnownLocationBusy_ClearsOnceForcedDestinationKeyIsGone"/>'s
    /// own "Simulate arrival, same as MoveToOperator's own RemoveKeyOnFinish would do" comment,
    /// <see cref="LandmarkNavigationTests"/> never asserting real arrival). This test follows that same
    /// established, honest precedent: it proves the decision produces a genuine, specific, persistent movement
    /// commitment (not an instantaneous no-op label - the exact real destination coordinates, still in effect
    /// several ticks later), then simulates the arrival a working pathfind would eventually produce and proves
    /// the commitment correctly resolves in response.
    /// </summary>
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
            destination = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates.Offset(new Vector2(-6, 0));

            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Bar\".", importance: 0.25f, source: "landmark",
                location: destination, subject: "Bar");
        });

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

        // Simulate the arrival a working pathfind would eventually produce (see this test's own doc comment).
        var transform = server.System<SharedTransformSystem>();
        await server.WaitPost(() =>
        {
            transform.SetCoordinates(aiPlayer, destination);
            server.EntMan.GetComponent<HTNComponent>(aiPlayer).Blackboard.Remove<EntityCoordinates>(MoveToAction.ForcedDestinationKey);
        });
        await pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.Null,
                "The commitment should resolve once the AI actually reaches the destination.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
