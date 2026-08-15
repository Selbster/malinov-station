#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared._MalinovStation.AIPlayers;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// Milestone 4 acceptance checks: the LLM gateway validates/clamps decisions, respects its enabled switch,
/// and - critically - never breaks an AI player when the LLM is unreachable (spec section 30).
/// </summary>
[TestFixture]
public sealed class LlmGatewaySystemTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "AIPlayerLlmTestMap";

    [TestPrototypes]
    private static readonly string AIPlayerLlmTestMap = @$"
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
    /// With the LLM disabled (the default), the gateway must never actually kick off a request.
    /// </summary>
    [Test]
    public async Task TryRequestDecision_LlmDisabledByDefault_ReturnsFalse()
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

            Assert.That(server.CfgMan.GetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled), Is.False);

            var gateway = server.System<LlmGatewaySystem>();
            Assert.That(gateway.TryRequestDecision(aiPlayer!.Value), Is.False);
            Assert.That(gateway.HasPendingRequest(aiPlayer.Value), Is.False);
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// A structurally valid decision with a whitelisted intent is applied to GoalComponent as an override.
    /// </summary>
    [Test]
    public async Task TryApplyDecision_ValidIntent_SetsGoalOverride()
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

            var gateway = server.System<LlmGatewaySystem>();
            var applied = gateway.TryApplyDecision(uid, new LlmDecision(AIGoals.Rest, 1.5f, "the character is exhausted"));
            Assert.That(applied, Is.True);

            var goal = server.EntMan.GetComponent<GoalComponent>(uid);
            Assert.Multiple(() =>
            {
                Assert.That(goal.CurrentGoal, Is.EqualTo(AIGoals.Rest));
                // Priority is clamped even though we passed an out-of-range value.
                Assert.That(goal.CurrentPriority, Is.EqualTo(1f));
                Assert.That(goal.Reason, Does.StartWith("llm:"));
                Assert.That(goal.IsLlmOverride, Is.True);
                Assert.That(goal.LlmOverrideExpiresAt, Is.GreaterThan(TimeSpan.Zero));
            });
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// An intent outside AIGoals.All must never reach GoalComponent - the whitelist is the enforcement point
    /// for "LLM actions must always be validated" (spec section 19).
    /// </summary>
    [Test]
    public async Task TryApplyDecision_UnknownIntent_IsRejected()
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

            var goalBefore = server.EntMan.GetComponent<GoalComponent>(uid).CurrentGoal;

            var gateway = server.System<LlmGatewaySystem>();
            var applied = gateway.TryApplyDecision(uid, new LlmDecision("HackTheMainframe", 0.99f, "bogus intent"));
            Assert.That(applied, Is.False);

            var goal = server.EntMan.GetComponent<GoalComponent>(uid);
            Assert.Multiple(() =>
            {
                Assert.That(goal.CurrentGoal, Is.EqualTo(goalBefore));
                Assert.That(goal.IsLlmOverride, Is.False);
            });
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// If the LLM is enabled but unreachable, the request must fail cleanly (not hang, not throw out of the
    /// system, not corrupt AI player state) so the AI player keeps running on the HTN/Goal System alone.
    /// </summary>
    [Test]
    public async Task TryRequestDecision_UnreachableEndpoint_FailsGracefully()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled, true);
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEndpoint, "http://127.0.0.1:1/v1/chat/completions");
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmModel, "test-model");
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmTimeoutSeconds, 3f);

        EntityUid? aiPlayer = null;

        await server.WaitPost(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station);
            Assert.That(aiPlayer, Is.Not.Null);

            var gateway = server.System<LlmGatewaySystem>();
            Assert.That(gateway.TryRequestDecision(aiPlayer!.Value), Is.True);
        });

        // The HTTP request runs on a real background Task against a real (refusing) socket, so waiting on
        // simulated ticks alone isn't enough - advance real wall-clock time too, bounded well past the
        // configured 3s timeout, so the pair is never handed back to the pool with work still in flight.
        var gatewaySystem = server.System<LlmGatewaySystem>();
        for (var i = 0; i < 60 && gatewaySystem.HasPendingRequest(aiPlayer!.Value); i++)
        {
            await Task.Delay(100);
            await pair.RunTicksSync(1);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(gatewaySystem.HasPendingRequest(aiPlayer!.Value), Is.False,
                "The LLM request should have failed and completed instead of hanging.");

            // The AI player must still be intact and un-overridden - the goal system stays in control.
            Assert.That(server.EntMan.EntityExists(aiPlayer.Value), Is.True);
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer.Value);
            Assert.That(goal.IsLlmOverride, Is.False);
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled, false);
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
