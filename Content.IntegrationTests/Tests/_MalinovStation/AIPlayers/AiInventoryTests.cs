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
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Inventory architectural-preparation acceptance checks (spec section 15/18's first slice): an AI notices
/// a real loose item it can actually perceive, and can choose to pick it up via the "PickUpItem" action - with
/// clean feedback if it guesses a name it doesn't actually know, if its hands are full, or if the item is no
/// longer close enough. Mirrors <see cref="AiInteractionTests"/>'s structure/helpers closely, but the
/// candidate filter is "not currently in a container" rather than "not a UI-flow object" - see
/// <see cref="ItemOpportunityComponent"/>'s own doc comment.
/// </summary>
[TestFixture]
public sealed class AiInventoryTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string TestItem = "TestInventoryItem";
    private const string ItemName = "test item";

    private const string Map = "AiInventoryTestMap";

    [TestPrototypes]
    private static readonly string AiInventoryTestMap = @$"
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

    /// <summary>Forces ItemOpportunitySystem to scan on the very next tick, then gives it a couple of ticks to
    /// actually run - mirrors AiInteractionTests.ForceScan.</summary>
    private async Task ForceScan(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        await server.WaitPost(() => server.EntMan.GetComponent<ItemOpportunityComponent>(uid).ScanAccumulator = 0f);
        await pair.RunTicksSync(2);
    }

    /// <summary>Mirrors AiInteractionTests.ClearOtherMobsNearby - removes incidental ambient mobs whose own
    /// collision fixture would otherwise count as an LOS obstruction.</summary>
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

    private bool IsHeldByAi(TestPair pair, EntityUid aiPlayer, EntityUid item)
    {
        var hands = pair.Server.System<HandsSystem>();
        return hands.EnumerateHands(aiPlayer).Any(hand => hands.GetHeldItem(aiPlayer, hand) == item);
    }

    [Test]
    public async Task ItemOpportunitySystem_ItemWithinRangeAndLos_AddsToCandidates()
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
            item = server.EntMan.SpawnEntity(TestItem, coords.Offset(new Vector2(2, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            var opportunity = server.EntMan.GetComponent<ItemOpportunityComponent>(aiPlayer);
            Assert.That(opportunity.NearbyItems, Does.Contain(item));

            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(aiPlayer);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NearbyItems, Does.Contain(ItemName));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task ItemOpportunitySystem_ItemOutsideScanRadius_NotACandidate()
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
            var scanRadius = server.EntMan.GetComponent<ItemOpportunityComponent>(aiPlayer).ScanRadius;
            item = server.EntMan.SpawnEntity(TestItem, coords.Offset(new Vector2(scanRadius + 20f, 0)));
        });

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<ItemOpportunityComponent>(aiPlayer).NearbyItems, Is.Empty,
                "An item well outside scan radius should never become a candidate.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task ItemOpportunitySystem_ItemBehindWall_NotACandidate()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid item = default;
        EntityUid wall = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;

            wall = server.EntMan.SpawnEntity("WallSolid", coords.Offset(new Vector2(1, 0)));
            item = server.EntMan.SpawnEntity(TestItem, coords.Offset(new Vector2(2, 0)));

            var interaction = server.System<SharedInteractionSystem>();
            Assert.That(interaction.InRangeUnobstructed(aiPlayer, item, 10f, CollisionGroup.Opaque), Is.False,
                "Test setup: the wall should actually block line of sight between the AI and the item.");
        });

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<ItemOpportunityComponent>(aiPlayer).NearbyItems, Is.Empty,
                "An item behind a wall should never become a candidate, even within radius.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
            server.EntMan.DeleteEntity(wall);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task ItemOpportunitySystem_ItemAlreadyHeld_IsExcludedFromCandidates()
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
            item = server.EntMan.SpawnEntity(TestItem, coords);

            var hands = server.System<HandsSystem>();
            Assert.That(hands.TryPickup(aiPlayer, item), Is.True, "Test setup: AI player should be able to pick up the item.");
        });

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<ItemOpportunityComponent>(aiPlayer).NearbyItems, Does.Not.Contain(item),
                "An item already held in a hand should never be offered as a candidate to pick up again.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task PickUpItemAction_KnownItem_SucceedsAndEndsUpInHand()
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
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, PickUpItemAction.ActionName, new PickUpItemActionParams(ItemName), out var reason);
            Assert.That(ok, Is.True, reason);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(IsHeldByAi(pair, aiPlayer, item), Is.True,
                "Picking up a known nearby item should actually put it in the AI's hand, not just report success.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task PickUpItemAction_UnknownName_IsRejectedAndHandsUntouched()
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
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, PickUpItemAction.ActionName, new PickUpItemActionParams("Nonexistent Thing"), out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("nothing nearby"));
            Assert.That(IsHeldByAi(pair, aiPlayer, item), Is.False);
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task PickUpItemAction_BothHandsFull_FailsCleanlyWithHandsFullReason()
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

            var hands = server.System<HandsSystem>();
            foreach (var hand in hands.EnumerateHands(aiPlayer).ToList())
            {
                var filler = server.EntMan.SpawnEntity(TestItem, coords);
                Assert.That(hands.TryPickup(aiPlayer, filler, hand), Is.True, "Test setup: every hand should start fillable.");
            }

            Assert.That(hands.TryGetEmptyHand(aiPlayer, out _), Is.False, "Test setup: AI's hands should all be full.");
        });

        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);
        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, PickUpItemAction.ActionName, new PickUpItemActionParams(ItemName), out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("hands are full"));
            Assert.That(IsHeldByAi(pair, aiPlayer, item), Is.False);
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task PickUpItemAction_CandidateNoLongerInRange_FailsCleanlyOnFreshCheck()
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
            Assert.That(server.EntMan.GetComponent<ItemOpportunityComponent>(aiPlayer).NearbyItems,
                Does.Contain(item), "Test setup: the item should be a candidate before it moves away.");
        });

        await server.WaitPost(() =>
        {
            var aiCoords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            server.EntMan.GetComponent<TransformComponent>(item).Coordinates = aiCoords.Offset(new Vector2(500, 500));
        });

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, PickUpItemAction.ActionName, new PickUpItemActionParams(ItemName), out var reason);

            Assert.That(ok, Is.False, "CanDo should re-verify proximity fresh, not trust the stale scan.");
            Assert.That(reason, Does.Contain("close enough"));
            Assert.That(IsHeldByAi(pair, aiPlayer, item), Is.False);
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>Legacy-gating proof mirroring AiInteractionTests.LegacyAiPlayer_NeverGetsInteractionOpportunityOrCandidates.</summary>
    [Test]
    public async Task LegacyAiPlayer_NeverGetsItemOpportunityOrCandidates()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid item = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value; // legacy, no cognitiveMode
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            item = server.EntMan.SpawnEntity(TestItem, coords.Offset(new Vector2(1, 0)));
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<ItemOpportunityComponent>(aiPlayer), Is.False,
                "A legacy AI player should never receive ItemOpportunityComponent.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// End-to-end wiring proof: a hand-built cognitive decision proposing PickUpItem for a real nearby item
    /// actually puts it in hand, mirroring AiInteractionTests'
    /// TryApplyCognitiveDecision_UseInteractable_ForANearbySwitch_FlipsItsState (no real LLM).
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_PickUpItem_ForANearbyItem_PutsItInHand()
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
            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "curiosity", "grab_the_item", 0.6f, 0.8f, "that could be useful",
                PickUpItemAction.ActionName, new Dictionary<string, string> { ["target"] = ItemName });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);

            var intent = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.That(intent.Name, Is.EqualTo("grab_the_item"));
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(IsHeldByAi(pair, aiPlayer, item), Is.True);
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
