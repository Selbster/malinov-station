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
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.4 Milestone 3: the hierarchical two-stage decision (Cognitive LLM role picks a category,
/// Action Selection role picks a concrete action only among what's eligible in it). The real async round-trip
/// (both stages, an unreachable endpoint) is already covered end-to-end by
/// <see cref="CognitiveLlmGatewayTests.TryRequestCognitiveDecision_UnreachableEndpoint_FailsGracefully"/> -
/// that test's assertions are implementation-agnostic and pass unmodified against the new two-stage internals.
/// This file instead covers the pieces that are actually new: the eligible-actions-by-category grouping the
/// skip-vs-second-call decision is based on, that the prompts built from it never mention more than what's
/// eligible, and that the shape <see cref="LlmGatewaySystem"/> synthesizes for the skip case is exactly what
/// <see cref="LlmGatewaySystem.TryApplyCognitiveDecision"/> already accepts unchanged.
/// </summary>
[TestFixture]
public sealed class HierarchicalActionSelectionTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string TestItem = "TestHierarchicalItem";
    private const string ItemName = "test hierarchical item";

    private const string Map = "HierarchicalActionSelectionTestMap";

    [TestPrototypes]
    private static readonly string HierarchicalActionSelectionTestMap = @$"
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
  id: {TestItem}
  parent: BaseItem
  name: {ItemName}
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

    /// <summary>Mirrors ActionEligibilityTests.ClearOtherMobsNearby - removes incidental ambient mobs
    /// (including the connected test client's own spawned character) whose own collision fixture would
    /// otherwise count as an LOS obstruction, or which could otherwise just crowd the same default spawn area.</summary>
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
    public async Task EligibleActionsInCategory_General_IsSoleContinueActivity()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var registry = server.System<AiActionRegistrySystem>();
            var eligibleInGeneral = registry.GetEligibleActions(aiPlayer).Where(a => a.Category == AiActionCategories.General).ToList();

            Assert.That(eligibleInGeneral, Has.Count.EqualTo(1));
            Assert.That(eligibleInGeneral[0].Name, Is.EqualTo(ContinueActivityAction.ActionName),
                "This is exactly the condition LlmGatewaySystem.HandleCompletedIntentRequest skips the Action Selection role for.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task EligibleActionsInCategory_WorkWithANearbyItem_HasAGenuineChoice()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid item = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            item = server.EntMan.SpawnEntity(TestItem, coords.Offset(new Vector2(1, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            var registry = server.System<AiActionRegistrySystem>();
            var eligibleInWork = registry.GetEligibleActions(aiPlayer).Where(a => a.Category == AiActionCategories.Work).ToList();

            // Work already has a genuine choice even before the item (PursueGoal + SearchArea are both always
            // eligible for any AI player) - what this proves is specifically that PickUpItem joins that set
            // once its own candidate is actually scanned, same as ActionEligibilityTests already proves for
            // PickUpItem in isolation.
            Assert.That(eligibleInWork.Select(a => a.Name), Does.Contain(PickUpItemAction.ActionName));
            Assert.That(eligibleInWork.Select(a => a.Name), Does.Contain(SearchAreaAction.ActionName));
            Assert.That(eligibleInWork.Count, Is.GreaterThanOrEqualTo(2));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public void BuildIntentSystemPrompt_OffersOnlyTheGivenEligibleCategories()
    {
        var prompt = PromptBuilder.BuildIntentSystemPrompt(new[] { "Rest" }, new[] { AiActionCategories.General });

        Assert.That(prompt, Does.Contain($"Categories you can currently act on: {AiActionCategories.General}."),
            "Should offer exactly the eligible categories passed in, not the full catalog.");
    }

    [Test]
    public void BuildActionSelectionSystemPrompt_ListsOnlyTheGivenEligibleActions()
    {
        IReadOnlyList<IAiAction> eligible = new List<IAiAction>
        {
            new ContinueActivityAction(),
        };

        var prompt = PromptBuilder.BuildActionSelectionSystemPrompt(AiActionCategories.General, eligible);

        Assert.Multiple(() =>
        {
            Assert.That(prompt, Does.Contain($"\"{ContinueActivityAction.ActionName}\""));
            Assert.That(prompt, Does.Not.Contain($"\"{PickUpItemAction.ActionName}\""),
                "An action that wasn't in the eligible list passed in should never appear as something the LLM could pick.");
            Assert.That(prompt, Does.Not.Contain($"\"{TalkToAction.ActionName}\""));
            Assert.That(prompt, Does.Not.Contain($"\"{GoToKnownLocationAction.ActionName}\""));
        });
    }

    /// <summary>
    /// Proves the shape LlmGatewaySystem.HandleCompletedIntentRequest synthesizes for the single-eligible-
    /// action skip case (Action=ContinueActivity, empty parameters, stage-one's own Desire/Intention/Priority/
    /// Confidence/Reason carried straight through) is exactly what TryApplyCognitiveDecision already accepts,
    /// unchanged - Intent gets written and no failure is traced, the same as any other valid decision.
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_SynthesizedSkipCaseDecision_AppliesCleanly()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var intent = new LlmIntentDecision("boredom", "keep_going", 0.2f, 0.5f, "nothing new", AiActionCategories.General);
            var synthesized = new LlmCognitiveDecision(
                intent.Desire, intent.Intention, intent.Priority, intent.Confidence, intent.Reason,
                ContinueActivityAction.ActionName, new Dictionary<string, string>());

            var gateway = server.System<LlmGatewaySystem>();
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, synthesized), Is.True);

            var intentComp = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(intentComp.Name, Is.EqualTo("keep_going"));
                Assert.That(intentComp.DesireServed, Is.EqualTo("boredom"));
                Assert.That(intentComp.Priority, Is.EqualTo(0.2f));
                Assert.That(intentComp.Confidence, Is.EqualTo(0.5f));
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
