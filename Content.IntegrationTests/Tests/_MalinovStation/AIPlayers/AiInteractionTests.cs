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
using Content.Server.DeviceLinking.Components;
using Content.Server.GameTicking;
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
/// AI Interaction architectural-preparation acceptance checks (spec section 16's first slice): an AI notices a
/// real single-step, no-UI interactable object it can actually perceive, and can choose to use it via the
/// "UseInteractable" action - with clean feedback if it guesses a name it doesn't actually know, and without
/// ever offering a UI-flow object (vending machine, computer) as a candidate. Mirrors
/// <see cref="LandmarkNavigationTests"/>'s structure/helpers closely, but deliberately NOT memory-backed - see
/// <see cref="InteractionOpportunityComponent"/>'s own doc comment for why.
/// </summary>
[TestFixture]
public sealed class AiInteractionTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string TestSwitch = "TestInteractionSwitch";
    private const string TestSwitchWithUi = "TestInteractionSwitchWithUi";
    private const string SwitchName = "test switch";

    private const string Map = "AiInteractionTestMap";

    [TestPrototypes]
    private static readonly string AiInteractionTestMap = @$"
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
  id: {TestSwitch}
  name: {SwitchName}
  components:
  - type: SignalSwitch

# Simulates a hypothetical interactable that is ALSO a UI-flow object (e.g. a machine with a manual override
# switch) - the only way to actually exercise the ActivatableUIComponent exclusion filter, since v1's scan is
# already scoped to SignalSwitchComponent holders only (see InteractionOpportunitySystem's own doc comment).
- type: entity
  id: {TestSwitchWithUi}
  name: test switch with ui
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

    /// <summary>Forces InteractionOpportunitySystem to scan on the very next tick, then gives it a couple of
    /// ticks to actually run - mirrors LandmarkNavigationTests.ForceScan.</summary>
    private async Task ForceScan(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        await server.WaitPost(() => server.EntMan.GetComponent<InteractionOpportunityComponent>(uid).ScanAccumulator = 0f);
        await pair.RunTicksSync(2);
    }

    /// <summary>Mirrors LandmarkNavigationTests.ClearOtherMobsNearby - removes incidental ambient mobs whose
    /// own collision fixture would otherwise count as an LOS obstruction.</summary>
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
    public async Task InteractionOpportunitySystem_SwitchWithinRangeAndLos_AddsToCandidates()
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
            uiSwitch = server.EntMan.SpawnEntity(TestSwitch, coords.Offset(new Vector2(2, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            var opportunity = server.EntMan.GetComponent<InteractionOpportunityComponent>(aiPlayer);
            Assert.That(opportunity.NearbyInteractables, Does.Contain(uiSwitch));

            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(aiPlayer);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.NearbyInteractables, Does.Contain(SwitchName));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(uiSwitch);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task InteractionOpportunitySystem_SwitchOutsideScanRadius_NotACandidate()
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
            var scanRadius = server.EntMan.GetComponent<InteractionOpportunityComponent>(aiPlayer).ScanRadius;
            uiSwitch = server.EntMan.SpawnEntity(TestSwitch, coords.Offset(new Vector2(scanRadius + 20f, 0)));
        });

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<InteractionOpportunityComponent>(aiPlayer).NearbyInteractables, Is.Empty,
                "A switch well outside scan radius should never become a candidate.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(uiSwitch);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task InteractionOpportunitySystem_SwitchBehindWall_NotACandidate()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid uiSwitch = default;
        EntityUid wall = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;

            wall = server.EntMan.SpawnEntity("WallSolid", coords.Offset(new Vector2(1, 0)));
            uiSwitch = server.EntMan.SpawnEntity(TestSwitch, coords.Offset(new Vector2(2, 0)));

            var interaction = server.System<SharedInteractionSystem>();
            Assert.That(interaction.InRangeUnobstructed(aiPlayer, uiSwitch, 10f, CollisionGroup.Opaque), Is.False,
                "Test setup: the wall should actually block line of sight between the AI and the switch.");
        });

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<InteractionOpportunityComponent>(aiPlayer).NearbyInteractables, Is.Empty,
                "A switch behind a wall should never become a candidate, even within radius.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(uiSwitch);
            server.EntMan.DeleteEntity(wall);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task InteractionOpportunitySystem_ObjectWithActivatableUi_IsExcludedEvenIfOtherwiseEligible()
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
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        await ForceScan(pair, aiPlayer);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<InteractionOpportunityComponent>(aiPlayer).NearbyInteractables,
                Does.Not.Contain(uiThing),
                "An object with ActivatableUIComponent should never be offered as a candidate, even if it's also a SignalSwitch.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(uiThing);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task UseInteractableAction_KnownSwitch_SucceedsAndFlipsSwitchState()
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
            uiSwitch = server.EntMan.SpawnEntity(TestSwitch, coords.Offset(new Vector2(1, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        await ForceScan(pair, aiPlayer);

        var stateBefore = server.EntMan.GetComponent<SignalSwitchComponent>(uiSwitch).State;

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, UseInteractableAction.ActionName, new UseInteractableActionParams(SwitchName), out var reason);
            Assert.That(ok, Is.True, reason);
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<SignalSwitchComponent>(uiSwitch).State, Is.Not.EqualTo(stateBefore),
                "Using a known nearby switch should actually flip its real vanilla state, not just report success.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(uiSwitch);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task UseInteractableAction_UnknownName_IsRejectedAndSwitchUntouched()
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
            uiSwitch = server.EntMan.SpawnEntity(TestSwitch, coords.Offset(new Vector2(1, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        await ForceScan(pair, aiPlayer);

        var stateBefore = server.EntMan.GetComponent<SignalSwitchComponent>(uiSwitch).State;

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, UseInteractableAction.ActionName, new UseInteractableActionParams("Nonexistent Thing"), out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("Поблизости нет ничего"));
            Assert.That(server.EntMan.GetComponent<SignalSwitchComponent>(uiSwitch).State, Is.EqualTo(stateBefore));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(uiSwitch);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task UseInteractableAction_CandidateNoLongerInRange_FailsCleanlyOnFreshCheck()
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
            uiSwitch = server.EntMan.SpawnEntity(TestSwitch, coords.Offset(new Vector2(1, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        // Scan while genuinely in range, so NearbyInteractables legitimately contains it...
        await ForceScan(pair, aiPlayer);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<InteractionOpportunityComponent>(aiPlayer).NearbyInteractables,
                Does.Contain(uiSwitch), "Test setup: the switch should be a candidate before it moves away.");
        });

        var stateBefore = server.EntMan.GetComponent<SignalSwitchComponent>(uiSwitch).State;

        // ...then walk it far away without letting another scan run - the cached candidate list is now stale.
        await server.WaitPost(() =>
        {
            var aiCoords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            server.EntMan.GetComponent<TransformComponent>(uiSwitch).Coordinates = aiCoords.Offset(new Vector2(500, 500));
        });

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(aiPlayer, UseInteractableAction.ActionName, new UseInteractableActionParams(SwitchName), out var reason);

            Assert.That(ok, Is.False, "CanDo should re-verify proximity fresh, not trust the stale scan.");
            Assert.That(reason, Does.Contain("недостаточно близко"));
            Assert.That(server.EntMan.GetComponent<SignalSwitchComponent>(uiSwitch).State, Is.EqualTo(stateBefore));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(uiSwitch);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Legacy-gating proof mirroring LandmarkNavigationTests.LegacyAiPlayer_NeverGetsLandmarkPerceptionOrMemories:
    /// a plain AI player never gets InteractionOpportunityComponent at all, so the scan provably never runs
    /// for it, not just "produces nothing useful".
    /// </summary>
    [Test]
    public async Task LegacyAiPlayer_NeverGetsInteractionOpportunityOrCandidates()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid uiSwitch = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value; // legacy, no cognitiveMode
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            uiSwitch = server.EntMan.SpawnEntity(TestSwitch, coords.Offset(new Vector2(1, 0)));
        });

        await pair.RunTicksSync(20);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<InteractionOpportunityComponent>(aiPlayer), Is.False,
                "A legacy AI player should never receive InteractionOpportunityComponent.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(uiSwitch);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// End-to-end wiring proof: a hand-built cognitive decision proposing UseInteractable for a real nearby
    /// switch actually flips it, mirroring LandmarkNavigationTests'
    /// TryApplyCognitiveDecision_GoToKnownLocation_ForARememberedPlace_SetsForcedDestination (no real LLM).
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_UseInteractable_ForANearbySwitch_FlipsItsState()
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
            uiSwitch = server.EntMan.SpawnEntity(TestSwitch, coords.Offset(new Vector2(1, 0)));
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);

        await ForceScan(pair, aiPlayer);

        var stateBefore = server.EntMan.GetComponent<SignalSwitchComponent>(uiSwitch).State;

        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "curiosity", "flip_the_switch", 0.6f, 0.8f, "I wonder what this does",
                UseInteractableAction.ActionName, new Dictionary<string, string> { ["target"] = SwitchName });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);

            var intent = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.That(intent.Name, Is.EqualTo("flip_the_switch"));
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<SignalSwitchComponent>(uiSwitch).State, Is.Not.EqualTo(stateBefore));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(uiSwitch);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
