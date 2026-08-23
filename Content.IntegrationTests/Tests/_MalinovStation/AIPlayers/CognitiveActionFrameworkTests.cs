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
using Content.Server.Hands.Systems;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Nutrition.Prototypes;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.4 Milestone 13: one complete hierarchical scenario, end to end through the real pipeline
/// pieces this milestone actually built - hunger rising -&gt; eligibility reflects a real nearby food item
/// -&gt; the hierarchical decision (hand-built here the same way every other cognitive-decision test in this
/// suite already does, since there's no fake LLM client in this tree - see
/// <see cref="CognitiveLlmGatewayTests"/>) picks Work -&gt; PickUpItem -&gt; the food actually ends up in hand.
/// Stops at "acquired," not "eaten": there's no cognitive EatAction in the registry yet (vanilla eating is a
/// distinct mechanic <see cref="NeedRecoveryTests"/> already proves the legacy/HTN path resolves on its own) -
/// this test is honestly scoped to what the action catalog this milestone extended actually supports.
///
/// The second scenario the spec asks for (work/interruption: busy -&gt; danger -&gt; interruption clears busy
/// state and forces fast reflection -&gt; new decision) is <see cref="AiBusyStateTests.AiBusyStateSystem_AttackWhileBusy_ForcesFastReflectionWithoutWaitingForRoutineCooldown"/> -
/// not duplicated here.
/// </summary>
[TestFixture]
public sealed class CognitiveActionFrameworkTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly SatiationValue StarvingThreshold = "Starving";
    private const string TestFoodItem = "TestHierarchicalFoodItem";
    private const string FoodName = "test sandwich";

    private const string Map = "CognitiveActionFrameworkTestMap";

    [TestPrototypes]
    private static readonly string CognitiveActionFrameworkTestMap = @$"
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
  id: {TestFoodItem}
  parent: BaseItem
  name: {FoodName}
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

    private async Task ForceScan(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        await server.WaitPost(() => server.EntMan.GetComponent<ItemOpportunityComponent>(uid).ScanAccumulator = 0f);
        await pair.RunTicksSync(2);
    }

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
    public async Task HungerScenario_RisingHungerToEligibleFoodPickup_ResolvesThroughTheHierarchicalPipeline()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid food = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            food = server.EntMan.SpawnEntity(TestFoodItem, coords.Offset(new Vector2(1, 0)));

            // Hunger becomes urgent - same real mechanism NeedRecoveryTests uses, feeding CognitiveState's
            // own IsHungry flag (see ContextBuilderSystem.BuildCognitiveState).
            var satiation = server.EntMan.GetComponent<SatiationComponent>(aiPlayer);
            server.System<SatiationSystem>().SetValue((aiPlayer, satiation), SatiationSystem.Hunger, StarvingThreshold);
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);
        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(aiPlayer);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.Needs.IsHungry, Is.True, "Test setup: hunger should now be urgent.");
            Assert.That(state.NearbyItems, Does.Contain(FoodName), "Test setup: the food should be a live candidate.");

            // Eligibility reflects a real, currently-possible way to act on hunger.
            var registry = server.System<AiActionRegistrySystem>();
            var eligibleInWork = registry.GetEligibleActions(aiPlayer).Where(a => a.Category == AiActionCategories.Work).ToList();
            Assert.That(eligibleInWork.Select(a => a.Name), Does.Contain(PickUpItemAction.ActionName));

            // Hand-built the same way every other cognitive-decision test in this suite already does (no fake
            // LLM client exists in this tree) - but shaped exactly like the real hierarchical pipeline
            // synthesizes it: Stage 1's own fields carried through, Stage 2's action/parameters/reason applied
            // on top (see LlmGatewaySystem.HandleCompletedActionSelectionRequest).
            var intent = new LlmIntentDecision("hunger", "find_food", 0.7f, 0.8f, "I'm starving", AiActionCategories.Work);
            var selection = new LlmActionSelectionDecision(PickUpItemAction.ActionName, new Dictionary<string, string> { ["target"] = FoodName }, "grab the food");
            var synthesized = new LlmCognitiveDecision(intent.Desire, intent.Intention, intent.Priority, intent.Confidence, selection.Reason, selection.Action, selection.ActionParameters);

            var gateway = server.System<LlmGatewaySystem>();
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, synthesized), Is.True);

            var intentComp = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.That(intentComp.Name, Is.EqualTo("find_food"));
            Assert.That(intentComp.DesireServed, Is.EqualTo("hunger"));
        });

        await server.WaitAssertion(() =>
        {
            var hands = server.System<HandsSystem>();
            var inHand = hands.EnumerateHands(aiPlayer).Any(hand => hands.GetHeldItem(aiPlayer, hand) == food);
            Assert.That(inHand, Is.True, "The food should actually end up in the AI's hand, not just report success.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(food);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
