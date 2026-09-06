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
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.6.3, spec section 12 - the milestone's central test.
///
/// A bored, curious passenger with nothing urgent to do must be able to leave the room it started in. Spec
/// section 11 is explicit that neither <c>Intent == ExploreStation</c> nor a non-null target counts: the only
/// thing that counts is the entity itself ending up somewhere else.
///
/// Everything except the two LLM round-trips is exercised here for real - eligibility, the action being
/// offered for selection, the decision being applied through the ordinary gateway, the controller producing a
/// target, navigation receiving it, and finally physical movement on real ticks. The model's own choice is
/// stood in for by a hand-built decision, which is what the rest of this suite does and is the only part that
/// genuinely cannot run without Ollama.
/// </summary>
[TestFixture]
public sealed class PassengerStartingRoomTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "PassengerStartingRoomTestMap";

    [TestPrototypes]
    private static readonly string PassengerStartingRoomTestMap = @$"
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

    /// <summary>The bare test map has no floor, so there is genuinely nowhere to walk. Lays a run of plating
    /// west of the passenger - the physical precondition for going anywhere at all.</summary>
    private static void PaintFloorWestOf(TestPair pair, EntityUid uid, int length)
    {
        var server = pair.Server;
        var xform = server.EntMan.GetComponent<TransformComponent>(uid);

        Assert.That(xform.GridUid, Is.Not.Null, "Test setup: the passenger should have spawned on a grid.");
        var gridUid = xform.GridUid!.Value;
        var grid = server.EntMan.GetComponent<MapGridComponent>(gridUid);

        var mapSys = server.System<SharedMapSystem>();
        var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        var origin = mapSys.CoordinatesToTile(gridUid, grid, xform.Coordinates);

        for (var i = -1; i <= length; i++)
        {
            for (var w = -1; w <= 1; w++)
                mapSys.SetTile(gridUid, grid, new Vector2i(origin.X - i, origin.Y + w), tile);
        }
    }

    /// <summary>
    /// Establishes spec section 12's premise: bored, curious, nothing urgent, and knowing nothing outside the
    /// starting room. Boredom and curiosity are set directly rather than waited out - this test is about what
    /// happens once they are high, and NeedsSystem's own growth is covered by PassengerBoredomTests.
    /// </summary>
    private static void MakeBoredAndCurious(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;

        server.EntMan.GetComponent<NeedsComponent>(uid).Boredom = 0.9f;
        server.EntMan.GetComponent<PersonalityComponent>(uid).Curiosity = 0.9f;

        server.EntMan.GetComponent<MemoryComponent>(uid).Memories.Clear();
        server.EntMan.GetComponent<LocationKnowledgeComponent>(uid).Places.Clear();
    }

    [Test]
    public async Task ABoredCuriousPassenger_DecidesToExplore_AndPhysicallyLeavesItsStartingRoom()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        Vector2 startPosition = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            MakeBoredAndCurious(pair, aiPlayer);
            PaintFloorWestOf(pair, aiPlayer, length: 16);
            startPosition = server.System<SharedTransformSystem>().GetWorldPosition(aiPlayer);
        });

        // Link 6 of the chain: the action has to be offered before anything can choose it.
        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<AiActionRegistrySystem>().GetLlmSelectableActions(aiPlayer).Select(a => a.Name),
                Does.Contain(ExploreStationAction.ActionName),
                "A bored, curious passenger with somewhere to go must be offered exploration (chain link 6).");
        });

        // Links 7-10: the decision is applied through the ordinary gateway, exactly as a real model answer
        // would be, and must come out the other side as a destination on the HTN blackboard.
        await server.WaitAssertion(() =>
        {
            var decision = new LlmCognitiveDecision(
                "Restlessness", "исследовать станцию", 0.9f, 0.9f, "Мне скучно здесь сидеть.",
                ExploreStationAction.ActionName, new Dictionary<string, string>());

            Assert.That(server.System<LlmGatewaySystem>().TryApplyCognitiveDecision(aiPlayer, decision), Is.True,
                "The cognitive decision must survive resolution, eligibility and dispatch (chain links 7-8).");

            var htn = server.EntMan.GetComponent<HTNComponent>(aiPlayer);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out _, server.EntMan),
                Is.True, "Navigation must have actually received a destination (chain links 9-10).");
        });

        // Link 11 - the only thing spec section 11 accepts as success. Real ticks, real steering.
        var moved = 0f;
        for (var i = 0; i < 80 && moved < 3f; i++)
        {
            await pair.RunTicksSync(10);
            moved = (server.System<SharedTransformSystem>().GetWorldPosition(aiPlayer) - startPosition).Length();
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(moved, Is.GreaterThan(3f),
                "The passenger has to physically leave where it started - an intent or a target is not success.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Spec section 3: the trace has to make the chain readable, and a stage that never happened has to be
    /// visibly absent. Without this the trace could silently under-report and nobody would notice.
    /// </summary>
    [Test]
    public async Task TheDecisionTrace_RecordsTheChain_AndShowsWhereItStopped()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            MakeBoredAndCurious(pair, aiPlayer);
            PaintFloorWestOf(pair, aiPlayer, length: 16);
        });

        await server.WaitAssertion(() =>
        {
            var trace = server.System<AiTraceSystem>();

            // Nothing has happened yet, so there is nothing to describe.
            Assert.That(trace.DescribeLastDecision(aiPlayer), Is.Null,
                "A passenger that has not decided anything yet must not report a decision.");

            trace.DecisionStarted(aiPlayer, boredom: 0.9f, curiosity: 0.9f, topDesire: "Restlessness");
            trace.DecisionIntent(aiPlayer, "исследовать станцию", "Movement",
                new[] { ExploreStationAction.ActionName, "ContinueActivity" });

            var described = trace.DescribeLastDecision(aiPlayer);

            Assert.That(described, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(described, Does.Contain("Restlessness").And.Contain("исследовать станцию"),
                    "The trace has to carry what the AI wanted and what it intended.");
                Assert.That(described, Does.Contain(ExploreStationAction.ActionName),
                    "And which actions were actually on offer at that moment.");
                Assert.That(described, Does.Contain("выбрано=-"),
                    "A stage that has not happened must read as absent - the first gap is the broken link.");
            });
        });

        // Now let the decision run for real; the trace must fill in the stages it reaches.
        await server.WaitAssertion(() =>
        {
            var decision = new LlmCognitiveDecision(
                "Restlessness", "исследовать станцию", 0.9f, 0.9f, "Мне скучно.",
                ExploreStationAction.ActionName, new Dictionary<string, string>());

            Assert.That(server.System<LlmGatewaySystem>().TryApplyCognitiveDecision(aiPlayer, decision), Is.True);

            var described = server.System<AiTraceSystem>().DescribeLastDecision(aiPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(described, Does.Contain($"выбрано={ExploreStationAction.ActionName}"),
                    "The chosen action has to appear once it is dispatched.");
                Assert.That(described, Does.Contain("цель=").And.Not.Contain("цель=-"),
                    "And so does the exploration target the controller produced.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
