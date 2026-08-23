#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Perception;
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
/// AI Players 0.4 Milestones 1-2: action categories exist on every registered action and match
/// <see cref="AiActionCategories"/>'s catalog, and <see cref="AiActionRegistrySystem.GetEligibleActions"/>/
/// <see cref="AiActionRegistrySystem.GetEligibleCategories"/> correctly pre-filter to only what's actually
/// currently possible - the deterministic gate the Cognitive/Action-Selection LLM roles now sit behind, so an
/// AI is never offered (and doesn't have to discover post-hoc via a CanDo rejection) something it plainly
/// cannot attempt right now. Mirrors <see cref="AiInventoryTests"/>'s map/setup conventions.
/// </summary>
[TestFixture]
public sealed class ActionEligibilityTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string TestItem = "TestEligibilityItem";
    private const string ItemName = "test eligibility item";

    private const string Map = "ActionEligibilityTestMap";

    [TestPrototypes]
    private static readonly string ActionEligibilityTestMap = @$"
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
        // Without this, characters spawned via SpawnAiPlayer can land on the arrivals shuttle grid rather than
        // the station grid directly, and ArrivalsSystem's own transfer-to-station sweep then relocates them
        // out from under a manually-assigned TransformComponent.Coordinates mid-test, non-deterministically
        // breaking any test that positions two characters relative to each other.
        server.CfgMan.SetCVar(CCVars.ArrivalsShuttles, false);
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

    /// <summary>Mirrors AiInventoryTests.ClearOtherMobsNearby - removes incidental ambient mobs (including the
    /// connected test client's own spawned character) whose own collision fixture would otherwise count as an
    /// LOS obstruction, or which could otherwise just crowd the same default spawn area.</summary>
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
    public async Task AllRegisteredActions_HaveACategoryFromTheCatalog()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var registry = server.System<AiActionRegistrySystem>();
            foreach (var action in registry.AllActions)
            {
                Assert.That(AiActionCategories.All, Does.Contain(action.Category),
                    $"{action.Name} has category \"{action.Category}\", which isn't in AiActionCategories.All.");
            }
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task ContinueActivityAction_IsAlwaysEligible()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var registry = server.System<AiActionRegistrySystem>();
            var eligible = registry.GetEligibleActions(aiPlayer);
            Assert.That(eligible.Select(a => a.Name), Does.Contain(ContinueActivityAction.ActionName));

            var categories = registry.GetEligibleCategories(aiPlayer);
            Assert.That(categories, Does.Contain(AiActionCategories.General));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task PickUpItemAction_IneligibleWithNoNearbyItems_EligibleOnceOneIsScanned()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid item = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
        });

        await server.WaitAssertion(() =>
        {
            var registry = server.System<AiActionRegistrySystem>();
            var eligible = registry.GetEligibleActions(aiPlayer).Select(a => a.Name).ToList();
            Assert.That(eligible, Does.Not.Contain(PickUpItemAction.ActionName),
                "PickUpItem should not be eligible when there is nothing nearby to pick up.");

            // Work is still an eligible category here - SearchAreaAction (also Work) has no candidate list to
            // check ahead of time (see its own IsEligible doc comment) and so is always eligible, keeping
            // Work non-empty regardless of PickUpItem specifically. The category-level assertion belongs in
            // the PickUpItemAction_HandsFull test below instead, isolating just PickUpItem's own contribution.
        });

        await server.WaitPost(() =>
        {
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            item = server.EntMan.SpawnEntity(TestItem, coords.Offset(new Vector2(1, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            var registry = server.System<AiActionRegistrySystem>();
            var eligible = registry.GetEligibleActions(aiPlayer).Select(a => a.Name).ToList();
            Assert.That(eligible, Does.Contain(PickUpItemAction.ActionName),
                "PickUpItem should become eligible once a nearby item is actually scanned as a candidate.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task PickUpItemAction_HandsFull_IsIneligibleEvenWithANearbyItem()
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
                hands.TryPickup(aiPlayer, filler, hand);
            }
        });

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            var registry = server.System<AiActionRegistrySystem>();
            var eligible = registry.GetEligibleActions(aiPlayer).Select(a => a.Name).ToList();
            Assert.That(eligible, Does.Not.Contain(PickUpItemAction.ActionName),
                "PickUpItem should not be eligible when both hands are already full, even with a visible candidate.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(item);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task GoToKnownLocationAction_IneligibleWithNoMemory_EligibleOnceALocationIsRemembered()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var registry = server.System<AiActionRegistrySystem>();
            var eligible = registry.GetEligibleActions(aiPlayer).Select(a => a.Name).ToList();
            Assert.That(eligible, Does.Not.Contain(GoToKnownLocationAction.ActionName),
                "GoToKnownLocation should not be eligible before the AI remembers anywhere (this test map has no beacons to pre-seed).");

            var destination = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates.Offset(new Vector2(5, 5));
            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Kitchen\".", importance: 0.25f, source: "landmark",
                location: destination, subject: "Kitchen");

            eligible = registry.GetEligibleActions(aiPlayer).Select(a => a.Name).ToList();
            Assert.That(eligible, Does.Contain(GoToKnownLocationAction.ActionName),
                "GoToKnownLocation should become eligible once the AI remembers at least one place.");
            Assert.That(registry.GetEligibleCategories(aiPlayer), Does.Contain(AiActionCategories.Movement));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task TalkToAction_IneligibleAlone_EligibleWithAFreeVisiblePartner()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid partner = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var registry = server.System<AiActionRegistrySystem>();
            var eligible = registry.GetEligibleActions(aiPlayer).Select(a => a.Name).ToList();
            Assert.That(eligible, Does.Not.Contain(TalkToAction.ActionName),
                "TalkTo should not be eligible with nobody visible.");
        });

        await server.WaitAssertion(() =>
        {
            partner = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            // What TalkToAction.IsEligible actually consumes is PerceptionComponent.LastObservation - a real
            // two-character spawn+scan race here would also be re-proving PerceptionSystem's own scanning
            // (already covered by AiSocialTests et al) and is vulnerable to vanilla spawn-point placement
            // (arrivals shuttle vs station grid) moving either character between spawn and scan. Constructing
            // the observation directly keeps this test scoped to what it's actually about: does IsEligible
            // correctly read a partner perception already reports as visible.
            var perception = server.EntMan.GetComponent<PerceptionComponent>(aiPlayer);
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            perception.LastObservation = new WorldObservation(coords, new List<EntityUid> { partner }, server.Timing.CurTime);

            var partnerConversation = server.EntMan.GetComponent<ConversationComponent>(partner);
            Assert.That(partnerConversation.State, Is.EqualTo(ConversationState.None));
            Assert.That(partnerConversation.Partner, Is.Null);

            var registry = server.System<AiActionRegistrySystem>();
            var eligible = registry.GetEligibleActions(aiPlayer).Select(a => a.Name).ToList();
            Assert.That(eligible, Does.Contain(TalkToAction.ActionName),
                "TalkTo should become eligible once a free, visible conversation partner is actually perceived.");
            Assert.That(registry.GetEligibleCategories(aiPlayer), Does.Contain(AiActionCategories.Social));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(partner);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>Eligibility is computed by the registry off plain component/memory state - it isn't itself
    /// cognitive-gated, so a legacy AI player (no opportunity components) correctly sees a narrower eligible
    /// set than a cognitive one, without anything here needing to special-case legacy AI explicitly.</summary>
    [Test]
    public async Task LegacyAiPlayer_NeverGetsOpportunityComponents_SoOnlyAmbientActionsAreEligible()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value; // legacy, no cognitiveMode

            var registry = server.System<AiActionRegistrySystem>();
            var eligible = registry.GetEligibleActions(aiPlayer).Select(a => a.Name).ToList();

            Assert.That(eligible, Does.Contain(ContinueActivityAction.ActionName));
            Assert.That(eligible, Does.Contain(SearchAreaAction.ActionName), "SearchArea only checks incapacitation, so it's eligible for legacy AI too.");
            Assert.That(eligible, Does.Not.Contain(PickUpItemAction.ActionName), "Legacy AI never gets ItemOpportunityComponent.");
            Assert.That(eligible, Does.Not.Contain(UseInteractableAction.ActionName), "Legacy AI never gets InteractionOpportunityComponent.");
            Assert.That(eligible, Does.Not.Contain(GoToKnownLocationAction.ActionName), "Legacy AI never accumulates landmark memory.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
