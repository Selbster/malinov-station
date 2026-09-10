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
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Search architectural-preparation acceptance checks (spec section 15's first slice): an AI actively looks
/// for something nearby matching a keyword, right now, and - unlike Interaction/Inventory's passive candidate
/// lists - remembers a genuine find as a "search-result" location memory that Navigation's own
/// MemorySystem.FindKnownLocation/GetKnownLocationNames pick up for free. Mirrors
/// <see cref="AiInteractionTests"/>/<see cref="AiInventoryTests"/>'s structure/helpers, but there's no passive
/// scan to force - every test invokes the action directly.
/// </summary>
[TestFixture]
public sealed class AiSearchTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string TestItem = "TestSearchItem";
    private const string ItemName = "test food item";
    private const string TestSwitch = "TestSearchSwitch";
    private const string SwitchName = "test search switch";
    private const string TestSwitchWithUi = "TestSearchSwitchWithUi";

    private const string Map = "AiSearchTestMap";

    [TestPrototypes]
    private static readonly string AiSearchTestMap = @$"
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

- type: entity
  id: {TestSwitch}
  name: {SwitchName}
  components:
  - type: SignalSwitch

- type: entity
  id: {TestSwitchWithUi}
  name: test search switch with ui
  components:
  - type: SignalSwitch
  - type: ActivatableUI
    key: enum.VendingMachineUiKey.Key
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

    private int SearchResultMemoryCount(TestPair pair, EntityUid uid) =>
        pair.Server.EntMan.GetComponent<MemoryComponent>(uid).Memories.Count(m => m.Source == "search-result");

    [Test]
    public async Task SearchAreaAction_MatchingItemWithinRadiusAndLos_SucceedsAndWritesSearchResultMemory()
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
            item = server.EntMan.SpawnEntity(TestItem, coords.Offset(new Vector2(5, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 20f);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, SearchAreaAction.ActionName, new SearchAreaActionParams(ItemName), out var reason);
            Assert.That(ok, Is.True, reason);

            var memory = server.EntMan.GetComponent<MemoryComponent>(aiPlayer);
            var result = memory.Memories.SingleOrDefault(m => m.Source == "search-result");
            Assert.That(result, Is.Not.Null);
            Assert.Multiple(() =>
            {
                Assert.That(result!.Subject, Is.EqualTo(ItemName));
                Assert.That(result.Location, Is.Not.Null);
                Assert.That(result.Participants, Does.Contain(item));
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task SearchAreaAction_MatchingInteractableWithinRadiusAndLos_SucceedsAndWritesSearchResultMemory()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid uiSwitch = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            uiSwitch = server.EntMan.SpawnEntity(TestSwitch, coords.Offset(new Vector2(5, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 20f);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, SearchAreaAction.ActionName, new SearchAreaActionParams("switch"), out var reason);
            Assert.That(ok, Is.True, reason);
            Assert.That(SearchResultMemoryCount(pair, aiPlayer), Is.EqualTo(1),
                "SearchArea should cover interactables, not just items.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(uiSwitch);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task SearchAreaAction_PresentButNameDoesNotMatchKeyword_FailsAndWritesNoMemory()
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
            item = server.EntMan.SpawnEntity(TestItem, coords.Offset(new Vector2(5, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 20f);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, SearchAreaAction.ActionName, new SearchAreaActionParams("nonexistent keyword"), out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("не нашёл"));
            Assert.That(SearchResultMemoryCount(pair, aiPlayer), Is.EqualTo(0));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task SearchAreaAction_NothingNearby_FailsCleanly()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 25f);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, SearchAreaAction.ActionName, new SearchAreaActionParams("anything"), out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("не нашёл"));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task SearchAreaAction_MatchOutsideSearchRadius_NotFound()
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
            // Well beyond SearchAreaAction's 20f radius.
            item = server.EntMan.SpawnEntity(TestItem, coords.Offset(new Vector2(50, 0)));
        });

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, SearchAreaAction.ActionName, new SearchAreaActionParams(ItemName), out var reason);

            Assert.That(ok, Is.False);
            Assert.That(SearchResultMemoryCount(pair, aiPlayer), Is.EqualTo(0));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task SearchAreaAction_MatchBehindWall_NotFound()
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
        });

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, SearchAreaAction.ActionName, new SearchAreaActionParams(ItemName), out var reason);

            Assert.That(ok, Is.False, "A match behind a wall should not be found, even within radius.");
            Assert.That(SearchResultMemoryCount(pair, aiPlayer), Is.EqualTo(0));
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
    public async Task SearchAreaAction_MatchingItemAlreadyHeld_IsExcluded()
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
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 20f);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, SearchAreaAction.ActionName, new SearchAreaActionParams(ItemName), out var reason);

            Assert.That(ok, Is.False, "An item the AI already holds shouldn't count as a fresh search find.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task SearchAreaAction_MatchingSwitchWithActivatableUi_IsExcluded()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid uiThing = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            uiThing = server.EntMan.SpawnEntity(TestSwitchWithUi, coords.Offset(new Vector2(2, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 20f);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, SearchAreaAction.ActionName, new SearchAreaActionParams("switch"), out var reason);

            Assert.That(ok, Is.False, "An object with ActivatableUIComponent should never count as a search find, even if it's also a SignalSwitch.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(uiThing);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The loop-closing test: a successful search's memory must actually be readable by
    /// MemorySystem.FindKnownLocation/GetKnownLocationNames - the same rails GoToKnownLocationAction uses -
    /// not just sit in memory unused.
    /// </summary>
    [Test]
    public async Task SearchAreaAction_SuccessfulFind_IsResolvableByFindKnownLocation()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid item = default;
        EntityCoordinates itemCoords = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            item = server.EntMan.SpawnEntity(TestItem, coords.Offset(new Vector2(5, 0)));
            itemCoords = server.EntMan.GetComponent<TransformComponent>(item).Coordinates;
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 20f);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            Assert.That(actions.TryDoAction(aiPlayer, SearchAreaAction.ActionName, new SearchAreaActionParams(ItemName), out var reason), Is.True, reason);

            var memorySystem = server.System<MemorySystem>();
            var resolved = memorySystem.FindKnownLocation(aiPlayer, "food");
            Assert.That(resolved, Is.EqualTo(itemCoords));

            var names = memorySystem.GetKnownLocationNames(aiPlayer);
            Assert.That(names, Does.Contain(ItemName));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// End-to-end wiring proof: a hand-built cognitive decision proposing SearchArea for a real nearby match
    /// actually writes the memory, mirroring the previous two slices' TryApplyCognitiveDecision_... style (no
    /// real LLM).
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_SearchArea_ForANearbyMatch_WritesSearchResultMemory()
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
            item = server.EntMan.SpawnEntity(TestItem, coords.Offset(new Vector2(5, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 20f);

        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "hunger", "find_food", 0.7f, 0.8f, "I don't see food yet, let me look around",
                SearchAreaAction.ActionName, new Dictionary<string, string> { ["keyword"] = ItemName });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);

            var intent = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.That(intent.Name, Is.EqualTo("find_food"));
            Assert.That(SearchResultMemoryCount(pair, aiPlayer), Is.EqualTo(1));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
