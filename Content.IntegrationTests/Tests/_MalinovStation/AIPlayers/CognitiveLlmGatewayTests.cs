#nullable enable
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
/// AI Players 2.0 Milestone 1: mirrors <see cref="LlmGatewaySystemTests"/>'s guaranteed contract for the new
/// cognitive decision pipeline - it must be exactly as safe as the legacy one (disabled by default, validated,
/// fails gracefully), since <see cref="LlmGatewaySystem.TryApplyCognitiveDecision"/> delegates its actual
/// whitelist/clamp logic to the same <see cref="LlmGatewaySystem.TryApplyDecision"/> the legacy tests already
/// cover. Also proves the bundled bug fix: the system prompt's allowed-intents list used to silently omit
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
    /// A structurally valid cognitive decision is applied to both GoalComponent (via the shared
    /// TryApplyDecision whitelist/clamp logic) and the cognitive-only IntentComponent overlay.
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_ValidIntention_SetsGoalOverrideAndIntent()
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
            var decision = new LlmCognitiveDecision("fatigue", AIGoals.Rest, 0.8f, 0.9f, "I'm exhausted and should rest");
            Assert.That(gateway.TryApplyCognitiveDecision(uid, decision), Is.True);

            var goal = server.EntMan.GetComponent<GoalComponent>(uid);
            var intent = server.EntMan.GetComponent<IntentComponent>(uid);
            Assert.Multiple(() =>
            {
                Assert.That(goal.CurrentGoal, Is.EqualTo(AIGoals.Rest));
                Assert.That(goal.IsLlmOverride, Is.True);
                Assert.That(intent.Name, Is.EqualTo(AIGoals.Rest));
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
    /// Same whitelist enforcement as the legacy path - proves the delegation to TryApplyDecision actually
    /// happens rather than the cognitive path having its own, possibly-drifted, validation.
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_UnknownIntention_IsRejected()
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
            var decision = new LlmCognitiveDecision("curiosity", "HackTheMainframe", 0.9f, 0.9f, "bogus intent");
            Assert.That(gateway.TryApplyCognitiveDecision(uid, decision), Is.False);

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
