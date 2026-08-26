#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using System.Numerics;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.6.2, spec sections 3/4/27/28: <c>ExploreStation</c> has to be a real, selectable action, not
/// another intent string. These tests cover the wiring the cognitive pipeline depends on - it is registered,
/// it carries a Russian description, the resolver accepts it, it is offered to the model when it makes sense
/// and withheld when it does not - plus the related fix that stopped <c>MoveTo</c> being advertised as an
/// option the model could never actually execute.
/// </summary>
[TestFixture]
public sealed class PassengerExploreActionTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "PassengerExploreActionTestMap";

    [TestPrototypes]
    private static readonly string PassengerExploreActionTestMap = @$"
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
    public async Task ExploreStation_IsRegistered_WithRussianDescription_AndResolvesWithoutParameters()
    {
        var pair = Pair;
        var server = pair.Server;
        await StartRoundAndGetStation(pair);

        await server.WaitAssertion(() =>
        {
            var action = server.System<AiActionRegistrySystem>().AllActions
                .FirstOrDefault(a => a.Name == ExploreStationAction.ActionName);

            Assert.That(action, Is.Not.Null, "ExploreStation must be registered in the action registry (spec section 3).");

            Assert.Multiple(() =>
            {
                Assert.That(action!.Category, Is.EqualTo(AiActionCategories.Movement));
                Assert.That(action.IsExtended, Is.True, "Exploration hands off to multi-tick HTN movement.");
                Assert.That(action.IsLlmSelectable, Is.True, "The whole point is that the model can choose it.");
                Assert.That(action.Description, Does.Contain("сследовать"),
                    "The description shown to the model must be Russian (spec section 29).");
            });

            // The model is never asked for a destination, so an empty parameter dictionary must resolve.
            var decision = new LlmCognitiveDecision(
                "Скука", "исследовать станцию", 0.8f, 0.9f, "Мне скучно.",
                ExploreStationAction.ActionName, new Dictionary<string, string>());

            Assert.That(ActionProposalResolver.TryResolve(decision, out var proposal, out var failReason), Is.True, failReason);
            Assert.That(proposal!.Parameters, Is.TypeOf<ExploreStationActionParams>());
        });

        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The bare test map carries no NavMapBeacons, so <c>SeedKnownBeacons</c> gives a passenger spawned here
    /// nothing at all - unlike a real station. One remembered place is therefore established explicitly, so
    /// these tests exercise "knows somewhere" rather than accidentally testing an empty map.
    /// </summary>
    private static void RememberOnePlace(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        var origin = server.EntMan.GetComponent<TransformComponent>(uid).Coordinates;
        server.System<MemorySystem>().AddMemory(uid,
            content: "Ты уже знаешь дорогу к «Бар».", importance: 0.25f, source: "landmark",
            location: origin.Offset(new Vector2(-6, 0)), subject: "Бар");
    }

    [Test]
    public async Task ExploreStation_IsOfferedToTheModel_WhenThePassengerKnowsPlaces()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            RememberOnePlace(pair, aiPlayer);
        });

        await server.WaitAssertion(() =>
        {
            var registry = server.System<AiActionRegistrySystem>();
            var selectable = registry.GetLlmSelectableActions(aiPlayer).Select(a => a.Name).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(selectable, Does.Contain(ExploreStationAction.ActionName),
                    "A passenger that knows somewhere it has never been must be offered exploration (spec section 27).");
                Assert.That(registry.GetEligibleCategories(aiPlayer), Does.Contain(AiActionCategories.Movement));
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Spec section 28: an action the passenger could not actually carry out must never reach the Action
    /// Selection prompt. An incapacitated character cannot go anywhere.
    /// </summary>
    [Test]
    public async Task ExploreStation_IsWithheld_WhenThePassengerIsIncapacitated()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            RememberOnePlace(pair, aiPlayer);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<AiActionRegistrySystem>().GetLlmSelectableActions(aiPlayer).Select(a => a.Name),
                Does.Contain(ExploreStationAction.ActionName), "Test setup: it should start out eligible.");
        });

        await server.WaitPost(() =>
        {
            var mobState = server.System<MobStateSystem>();
            mobState.ChangeMobState(aiPlayer, Content.Shared.Mobs.MobState.Critical);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<AiActionRegistrySystem>().GetLlmSelectableActions(aiPlayer).Select(a => a.Name),
                Does.Not.Contain(ExploreStationAction.ActionName),
                "An incapacitated passenger must not be offered exploration at all (spec section 28).");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The companion fix: <c>MoveTo</c> needs raw coordinates, has no resolver case, and was therefore
    /// rejected every single time the model picked it - while still being printed as a choice and still
    /// keeping the Movement category alive. It must stay usable programmatically but invisible to the LLM.
    /// </summary>
    [Test]
    public async Task MoveTo_StaysEligibleForCode_ButIsNeverOfferedToTheModel()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
        });

        await server.WaitAssertion(() =>
        {
            var registry = server.System<AiActionRegistrySystem>();

            Assert.Multiple(() =>
            {
                Assert.That(registry.GetEligibleActions(aiPlayer).Select(a => a.Name), Does.Contain("MoveTo"),
                    "MoveTo remains a perfectly good action for code to invoke.");
                Assert.That(registry.GetLlmSelectableActions(aiPlayer).Select(a => a.Name), Does.Not.Contain("MoveTo"),
                    "But the model must never be offered an action whose parameters it cannot produce.");
            });

            // And it is genuinely unusable through the LLM path, which is why hiding it is correct.
            var decision = new LlmCognitiveDecision(
                "Скука", "куда-то пойти", 0.5f, 0.5f, "Просто так.",
                "MoveTo", new Dictionary<string, string>());

            Assert.That(ActionProposalResolver.TryResolve(decision, out _, out _), Is.False);
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
