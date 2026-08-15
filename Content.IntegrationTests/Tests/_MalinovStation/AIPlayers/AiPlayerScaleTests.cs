#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Shared._MalinovStation.AIPlayers;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.NPC;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// Milestone 10 acceptance checks: spec section 31's final milestone, "test 10-20 AI players simultaneously".
/// This isn't new functionality - it's the empirical check that everything built in Milestones 1-9 actually
/// holds up at the population size the whole performance/budgeting design (spec sections 23-25, Milestone 8)
/// was built for, rather than just the 1-3 AI players every earlier milestone's own tests used.
/// </summary>
[TestFixture]
public sealed class AiPlayerScaleTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    /// <summary>The upper end of spec section 31 Milestone 10's "10-20 AI players simultaneously".</summary>
    private const int AiPlayerCount = 20;

    private const string Map = "AIPlayerScaleTestMap";

    [TestPrototypes]
    private static readonly string AIPlayerScaleTestMap = @$"
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

    /// <summary>
    /// A full population of 20 AI players, left to run their normal unattended update loop (Needs decay,
    /// Goal reconsideration, Perception, HTN planning/movement, LOD reassessment) for a sustained stretch,
    /// must all remain alive, awake, and in a structurally valid state - no crashes, no corrupted/out-of-range
    /// state from many entities' systems interacting. Robust's integration test harness fails the test on any
    /// unexpected error-level log, so an unhandled exception in any AI system during this run fails it too.
    /// </summary>
    [Test]
    public async Task Population_Of20AiPlayers_RemainsStable_AcrossManyTicks()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var aiPlayers = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var aiPlayerSystem = server.System<AIPlayerSystem>();
            for (var i = 0; i < AiPlayerCount; i++)
            {
                var uid = aiPlayerSystem.SpawnAiPlayer(Passenger, station);
                Assert.That(uid, Is.Not.Null, $"Failed to spawn AI player #{i}.");
                aiPlayers.Add(uid!.Value);
            }
        });

        Assert.That(aiPlayers, Has.Count.EqualTo(AiPlayerCount));

        await pair.RunTicksSync(100);

        await server.WaitAssertion(() =>
        {
            foreach (var uid in aiPlayers)
            {
                Assert.That(server.EntMan.EntityExists(uid), Is.True, $"{uid} should still exist after 100 ticks.");
                Assert.That(server.EntMan.HasComponent<ActiveNPCComponent>(uid), Is.True, $"{uid} should still be awake/updating.");

                var goal = server.EntMan.GetComponent<GoalComponent>(uid);
                Assert.That(AIGoals.All.Contains(goal.CurrentGoal), Is.True, $"{uid} has an invalid goal \"{goal.CurrentGoal}\".");

                var needs = server.EntMan.GetComponent<NeedsComponent>(uid);
                Assert.Multiple(() =>
                {
                    Assert.That(needs.Fatigue, Is.InRange(0f, 1f), $"{uid} Fatigue out of range.");
                    Assert.That(needs.Stress, Is.InRange(0f, 1f), $"{uid} Stress out of range.");
                    Assert.That(needs.Safety, Is.InRange(0f, 1f), $"{uid} Safety out of range.");
                    Assert.That(needs.SocialNeed, Is.InRange(0f, 1f), $"{uid} SocialNeed out of range.");
                });

                var personality = server.EntMan.GetComponent<PersonalityComponent>(uid);
                Assert.Multiple(() =>
                {
                    Assert.That(personality.Sociability, Is.InRange(0f, 1f));
                    Assert.That(personality.Courage, Is.InRange(0f, 1f));
                    Assert.That(personality.Curiosity, Is.InRange(0f, 1f));
                    Assert.That(personality.Laziness, Is.InRange(0f, 1f));
                    Assert.That(personality.Greed, Is.InRange(0f, 1f));
                    Assert.That(personality.Aggression, Is.InRange(0f, 1f));
                    Assert.That(personality.Loyalty, Is.InRange(0f, 1f));
                    Assert.That(personality.RiskTolerance, Is.InRange(0f, 1f));
                    Assert.That(personality.AuthorityRespect, Is.InRange(0f, 1f));
                    Assert.That(personality.Professionalism, Is.InRange(0f, 1f));
                    Assert.That(personality.Empathy, Is.InRange(0f, 1f));
                    Assert.That(personality.Honesty, Is.InRange(0f, 1f));
                    Assert.That(personality.Impulsiveness, Is.InRange(0f, 1f));
                });
            }
        });

        await server.WaitPost(() =>
        {
            foreach (var uid in aiPlayers)
            {
                if (server.EntMan.EntityExists(uid))
                    server.EntMan.DeleteEntity(uid);
            }
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Worst case for spec section 24's concurrency budget: every AI player in a crowded area wants an LLM
    /// decision in the very same tick (e.g. they all just noticed a new character at once). With the budget
    /// set to 3, exactly 3 of 20 simultaneous requests must be accepted and the rest must be rejected
    /// outright (not queued) - proving the cap actually holds at the population size Milestone 8 was built
    /// to protect, not just the one-at-a-time case the Milestone 4 tests exercise.
    /// </summary>
    [Test]
    public async Task LlmGateway_ConcurrencyBudget_CapsRequests_WhenManyAiPlayersDecideSimultaneously()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        const int maxConcurrent = 3;
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled, true);
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEndpoint, "http://127.0.0.1:1/v1/chat/completions");
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmModel, "test-model");
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmTimeoutSeconds, 3f);
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmMaxConcurrentRequests, maxConcurrent);

        var aiPlayers = new List<EntityUid>();
        var gateway = server.System<LlmGatewaySystem>();

        await server.WaitPost(() =>
        {
            var aiPlayerSystem = server.System<AIPlayerSystem>();
            for (var i = 0; i < AiPlayerCount; i++)
                aiPlayers.Add(aiPlayerSystem.SpawnAiPlayer(Passenger, station)!.Value);

            var accepted = aiPlayers.Count(uid => gateway.TryRequestDecision(uid));
            Assert.That(accepted, Is.EqualTo(maxConcurrent),
                $"Expected exactly the configured budget ({maxConcurrent}) of {AiPlayerCount} simultaneous requests to be accepted.");
        });

        // The accepted requests are real background Tasks against a refusing socket, so wall-clock time needs
        // to actually pass (bounded past the configured timeout) rather than just simulated ticks.
        for (var i = 0; i < 60 && aiPlayers.Any(uid => gateway.HasPendingRequest(uid)); i++)
        {
            await Task.Delay(100);
            await pair.RunTicksSync(1);
        }

        await server.WaitAssertion(() =>
        {
            foreach (var uid in aiPlayers)
            {
                Assert.That(gateway.HasPendingRequest(uid), Is.False,
                    $"{uid}'s LLM request should have failed and completed instead of hanging.");
                Assert.That(server.EntMan.EntityExists(uid), Is.True);
            }
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled, false);
            foreach (var uid in aiPlayers)
            {
                if (server.EntMan.EntityExists(uid))
                    server.EntMan.DeleteEntity(uid);
            }
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Milestone 8's distance-based LOD (spec section 25) must keep classifying correctly across a full
    /// population, not just the single near/far AI player each AiLodSystemTests case uses: a group of 10
    /// standing on the real player gets Full, a group of 10 far away gets Background.
    /// </summary>
    [Test]
    public async Task Population_LodClassification_SplitsCorrectly_AcrossFullAndBackgroundTiers()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var nearGroup = new List<EntityUid>();
        var farGroup = new List<EntityUid>();

        await server.WaitPost(() =>
        {
            var aiPlayerSystem = server.System<AIPlayerSystem>();
            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            var realPlayerCoords = server.EntMan.GetComponent<TransformComponent>(
                server.PlayerMan.Sessions.First().AttachedEntity!.Value).Coordinates;

            for (var i = 0; i < AiPlayerCount / 2; i++)
            {
                var uid = aiPlayerSystem.SpawnAiPlayer(Passenger, station)!.Value;
                xformSystem.SetCoordinates(uid, realPlayerCoords);
                server.EntMan.GetComponent<AiLodComponent>(uid).ReassessAccumulator = 0f;
                nearGroup.Add(uid);
            }

            for (var i = 0; i < AiPlayerCount / 2; i++)
            {
                var uid = aiPlayerSystem.SpawnAiPlayer(Passenger, station)!.Value;
                xformSystem.SetCoordinates(uid, realPlayerCoords.Offset(new Vector2(1000, 1000)));
                server.EntMan.GetComponent<AiLodComponent>(uid).ReassessAccumulator = 0f;
                farGroup.Add(uid);
            }
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            foreach (var uid in nearGroup)
            {
                var lod = server.EntMan.GetComponent<AiLodComponent>(uid);
                Assert.That(lod.Level, Is.EqualTo(AiLevelOfDetail.Full), $"{uid} near the real player should be Full.");
            }

            foreach (var uid in farGroup)
            {
                var lod = server.EntMan.GetComponent<AiLodComponent>(uid);
                Assert.That(lod.Level, Is.EqualTo(AiLevelOfDetail.Background), $"{uid} far from the real player should be Background.");
            }
        });

        await server.WaitPost(() =>
        {
            foreach (var uid in nearGroup.Concat(farGroup))
            {
                if (server.EntMan.EntityExists(uid))
                    server.EntMan.DeleteEntity(uid);
            }
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
