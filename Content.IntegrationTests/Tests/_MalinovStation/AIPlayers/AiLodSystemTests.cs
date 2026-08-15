#nullable enable
using System.Linq;
using System.Numerics;
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
/// Milestone 8 acceptance checks: AI players far from every connected player get classified as
/// lower-detail tiers, the expensive per-tick systems (Perception/Danger's scan/Goal) slow down
/// accordingly, and Background-tier AI players don't spend LLM budget or start new conversations that
/// nobody could witness.
/// </summary>
[TestFixture]
public sealed class AiLodSystemTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "AIPlayerLodTestMap";

    [TestPrototypes]
    private static readonly string AIPlayerLodTestMap = @$"
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
    /// An AI player standing right next to the real connected player should be classified Full.
    /// </summary>
    [Test]
    public async Task ClassifiesFull_WhenNearRealPlayer()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value;

            var realPlayer = server.PlayerMan.Sessions.First().AttachedEntity!.Value;
            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            xformSystem.SetCoordinates(aiPlayer, server.EntMan.GetComponent<TransformComponent>(realPlayer).Coordinates);

            server.EntMan.GetComponent<AiLodComponent>(aiPlayer).ReassessAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var lod = server.EntMan.GetComponent<AiLodComponent>(aiPlayer);
            Assert.That(lod.Level, Is.EqualTo(AiLevelOfDetail.Full));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// An AI player far from every connected player should be classified Background.
    /// </summary>
    [Test]
    public async Task ClassifiesBackground_WhenFarFromEveryRealPlayer()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value;

            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            var farAway = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates.Offset(new Vector2(1000, 1000));
            xformSystem.SetCoordinates(aiPlayer, farAway);

            server.EntMan.GetComponent<AiLodComponent>(aiPlayer).ReassessAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var lod = server.EntMan.GetComponent<AiLodComponent>(aiPlayer);
            Assert.That(lod.Level, Is.EqualTo(AiLevelOfDetail.Background));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// PerceptionSystem's next accumulator reset should reflect the entity's current LOD multiplier -
    /// a direct check of the throttling itself, independent of AiLodSystem's own distance computation.
    /// </summary>
    [Test]
    public async Task PerceptionCooldown_IsScaledByLodMultiplier_ForBackgroundTier()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value;

            var lod = server.EntMan.GetComponent<AiLodComponent>(aiPlayer);
            lod.Level = AiLevelOfDetail.Background;
            // Freeze LOD's own reassessment so it doesn't overwrite our forced Background level mid-test.
            lod.ReassessAccumulator = 999f;

            server.EntMan.GetComponent<PerceptionComponent>(aiPlayer).PerceiveAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var lod = server.EntMan.GetComponent<AiLodComponent>(aiPlayer);
            var perception = server.EntMan.GetComponent<PerceptionComponent>(aiPlayer);

            Assert.That(perception.PerceiveAccumulator, Is.GreaterThan(perception.PerceiveCooldown),
                "A Background-tier AI player's next perception scan should be scheduled further out than the base cooldown.");
            Assert.That(perception.PerceiveAccumulator, Is.EqualTo(perception.PerceiveCooldown * lod.BackgroundMultiplier).Within(0.5f));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Neither goal decisions nor conversation lines should be requested for a Background-tier AI player,
    /// even with the LLM enabled - that budget is reserved for AI players someone could actually notice.
    /// </summary>
    [Test]
    public async Task LlmGateway_RejectsRequests_ForBackgroundTierAiPlayer()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled, true);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value;
            server.EntMan.GetComponent<AiLodComponent>(aiPlayer).Level = AiLevelOfDetail.Background;

            var gateway = server.System<LlmGatewaySystem>();
            Assert.That(gateway.TryRequestDecision(aiPlayer), Is.False);

            var dummyContext = new DialogueContext("A", "calm", "B", 0f, 0f, 0f, 0f, null, null);
            Assert.That(gateway.TryRequestLine(aiPlayer, dummyContext, (_, _) => { }), Is.False);
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled, false);
            server.EntMan.DeleteEntity(aiPlayer);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// A Background-tier AI player that wants to socialize should not start a new conversation, even with
    /// an otherwise-eligible partner right next to it.
    /// </summary>
    [Test]
    public async Task SocialSystem_DoesNotInitiateConversation_ForBackgroundTierAiPlayer()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid a = default;
        EntityUid b = default;

        await server.WaitPost(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            a = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;
            b = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;

            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            xformSystem.SetCoordinates(b, server.EntMan.GetComponent<TransformComponent>(a).Coordinates);

            server.EntMan.GetComponent<AiLodComponent>(a).Level = AiLevelOfDetail.Background;

            var goal = server.EntMan.GetComponent<GoalComponent>(a);
            goal.CurrentGoal = AIGoals.Socialize;
            goal.ReconsiderAccumulator = 999f;

            server.EntMan.GetComponent<PerceptionComponent>(a).PerceiveAccumulator = 0f;
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var conversation = server.EntMan.GetComponent<ConversationComponent>(a);
            Assert.That(conversation.Partner, Is.Null);
            Assert.That(conversation.State, Is.EqualTo(ConversationState.None));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
