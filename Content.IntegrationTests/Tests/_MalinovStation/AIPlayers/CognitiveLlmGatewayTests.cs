#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Prototypes;
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
/// Mirrors <see cref="LlmGatewaySystemTests"/>'s guaranteed contract for the cognitive decision pipeline - it
/// must be exactly as safe as the legacy one (disabled by default, validated, fails gracefully). AI Players
/// 0.3: <see cref="LlmGatewaySystem.TryApplyCognitiveDecision"/> no longer delegates to
/// <see cref="LlmGatewaySystem.TryApplyDecision"/> at all - Intent (free-form) and the "PursueGoal" action's
/// goal name (whitelisted via <see cref="GoalSystem.IsKnownGoalName"/>, the same whitelist
/// <see cref="LlmGatewaySystem.TryApplyDecision"/> now goes through too via <see cref="GoalSystem.TrySetExternalGoal"/>)
/// are independent writes that can succeed/fail separately - see the tests below for the resulting divergence.
/// Also proves the bundled Milestone 1 bug fix still holds: the system prompt's allowed-goals list includes
/// loaded professional-goal ids.
/// </summary>
[TestFixture]
public sealed class CognitiveLlmGatewayTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<AiProfessionalGoalPrototype> RepairMachineGoal = "RepairMachine";

    private const string Map = "CognitiveLlmGatewayTestMap";

    [TestPrototypes]
    private static readonly string CognitiveLlmGatewayTestMap = @$"
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
    /// With the LLM disabled (the default), the cognitive request path must never actually kick off a
    /// request, exactly like the legacy path - even for an entity spawned in cognitive mode.
    /// </summary>
    [Test]
    public async Task TryRequestCognitiveDecision_LlmDisabledByDefault_ReturnsFalse()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;

        await server.WaitAssertion(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station, cognitiveMode: true);
            Assert.That(aiPlayer, Is.Not.Null);

            Assert.That(server.CfgMan.GetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled), Is.False);
            Assert.That(server.CfgMan.GetCVar(MalinovAiPlayerCVars.AiPlayersCognitiveEnabled), Is.False);

            var gateway = server.System<LlmGatewaySystem>();
            Assert.That(gateway.TryRequestCognitiveDecision(aiPlayer!.Value), Is.False);
            Assert.That(gateway.HasPendingCognitiveRequest(aiPlayer.Value), Is.False);
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// AI Players 0.3: a structurally valid cognitive decision writes IntentComponent's free-form
    /// self-narration directly (no longer forced through the AIGoals vocabulary), while a "PursueGoal" action
    /// proposal separately drives GoalComponent via the shared TrySetExternalGoal write path - the two are now
    /// independent and allowed to diverge, which is the actual point of this milestone.
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_ValidPursueGoal_SetsIndependentIntentAndGoalOverride()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;

        await server.WaitAssertion(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station, cognitiveMode: true);
            Assert.That(aiPlayer, Is.Not.Null);
            var uid = aiPlayer!.Value;

            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "fatigue", "rest_up", 0.8f, 0.9f, "I'm exhausted and should rest",
                "PursueGoal", new Dictionary<string, string> { ["goal"] = AIGoals.Rest });
            Assert.That(gateway.TryApplyCognitiveDecision(uid, decision), Is.True);

            var goal = server.EntMan.GetComponent<GoalComponent>(uid);
            var intent = server.EntMan.GetComponent<IntentComponent>(uid);
            Assert.Multiple(() =>
            {
                Assert.That(goal.CurrentGoal, Is.EqualTo(AIGoals.Rest));
                Assert.That(goal.IsLlmOverride, Is.True);
                Assert.That(intent.Name, Is.EqualTo("rest_up"), "Intent should be the free-form string, not forced onto the goal vocabulary.");
                Assert.That(intent.Priority, Is.EqualTo(0.8f));
                Assert.That(intent.Confidence, Is.EqualTo(0.9f));
                Assert.That(intent.DesireServed, Is.EqualTo("fatigue"));
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
    /// AI Players 0.3: Intention itself is no longer validated at all (it's free-form) - the whitelist now
    /// lives on PursueGoal's "goal" parameter instead. An unknown goal name is rejected by the action's own
    /// CanDo (via GoalSystem.IsKnownGoalName), leaving GoalComponent untouched - but IntentComponent still
    /// gets updated, proving Intent and the action attempt now fail independently of each other.
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_PursueGoalUnknownGoal_IsRejectedButIntentStillUpdates()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;

        await server.WaitAssertion(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station, cognitiveMode: true);
            Assert.That(aiPlayer, Is.Not.Null);
            var uid = aiPlayer!.Value;

            var goalBefore = server.EntMan.GetComponent<GoalComponent>(uid).CurrentGoal;

            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "curiosity", "hack_the_mainframe", 0.9f, 0.9f, "bogus goal",
                "PursueGoal", new Dictionary<string, string> { ["goal"] = "HackTheMainframe" });
            Assert.That(gateway.TryApplyCognitiveDecision(uid, decision), Is.False);

            var goal = server.EntMan.GetComponent<GoalComponent>(uid);
            var intent = server.EntMan.GetComponent<IntentComponent>(uid);
            Assert.Multiple(() =>
            {
                Assert.That(goal.CurrentGoal, Is.EqualTo(goalBefore));
                Assert.That(goal.IsLlmOverride, Is.False);
                Assert.That(intent.Name, Is.EqualTo("hack_the_mainframe"),
                    "IntentComponent should still reflect the AI's self-reported intent even though the concrete action failed.");
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
    /// If the LLM is enabled but unreachable, the cognitive request must fail cleanly, exactly like the
    /// legacy path - never hang, never corrupt state.
    /// </summary>
    [Test]
    public async Task TryRequestCognitiveDecision_UnreachableEndpoint_FailsGracefully()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled, true);
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersCognitiveEnabled, true);
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEndpoint, "http://127.0.0.1:1/v1/chat/completions");
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmModel, "test-model");
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmTimeoutSeconds, 3f);

        EntityUid? aiPlayer = null;

        await server.WaitPost(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station, cognitiveMode: true);
            Assert.That(aiPlayer, Is.Not.Null);

            var gateway = server.System<LlmGatewaySystem>();
            Assert.That(gateway.TryRequestCognitiveDecision(aiPlayer!.Value), Is.True);
        });

        var gatewaySystem = server.System<LlmGatewaySystem>();
        for (var i = 0; i < 60 && gatewaySystem.HasPendingCognitiveRequest(aiPlayer!.Value); i++)
        {
            await Task.Delay(100);
            await pair.RunTicksSync(1);
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(gatewaySystem.HasPendingCognitiveRequest(aiPlayer!.Value), Is.False,
                "The cognitive LLM request should have failed and completed instead of hanging.");

            Assert.That(server.EntMan.EntityExists(aiPlayer.Value), Is.True);
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer.Value);
            Assert.That(goal.IsLlmOverride, Is.False);
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled, false);
            server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersCognitiveEnabled, false);
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Regression for the bundled bug fix: BuildSystemPrompt used to hardcode only AIGoals.All, silently
    /// omitting loaded professional-goal ids even though the gateway's own whitelist accepts them.
    /// </summary>
    [Test]
    public async Task BuildSystemPrompt_IncludesLoadedProfessionalGoalIds()
    {
        var server = Pair.Server;

        await server.WaitAssertion(() =>
        {
            var proto = server.ResolveDependency<IPrototypeManager>();
            Assert.That(proto.HasIndex<AiProfessionalGoalPrototype>(RepairMachineGoal), Is.True,
                "Test setup: the RepairMachine professional goal prototype should be loaded.");

            var allowedIntents = AIGoals.All.Append(RepairMachineGoal.Id).ToList();
            var prompt = PromptBuilder.BuildSystemPrompt(allowedIntents);

            Assert.That(prompt, Does.Contain(RepairMachineGoal.Id),
                "The system prompt's allowed-intents list should include loaded professional goal ids, not just the fixed AIGoals.All vocabulary.");
        });
    }
}
