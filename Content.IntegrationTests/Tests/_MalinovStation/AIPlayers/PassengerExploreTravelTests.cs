#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.6.2, spec sections 21/22: exploration has to produce a real journey, not a label. These tests
/// drive <c>ExploreStation</c> through the ordinary action registry and check what actually happened to the
/// world - which destination the HTN blackboard now holds, whether the passenger physically moved, and whether
/// arriving somewhere changed what it knows.
/// </summary>
[TestFixture]
public sealed class PassengerExploreTravelTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Unfamiliar = "Склад";
    private const string Map = "PassengerExploreTravelTestMap";

    [TestPrototypes]
    private static readonly string PassengerExploreTravelTestMap = @$"
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

    private static bool TryGetForcedDestination(TestPair pair, EntityUid uid, out EntityCoordinates destination)
    {
        var server = pair.Server;
        destination = default;
        return server.EntMan.TryGetComponent<HTNComponent>(uid, out var htn) &&
               htn.Blackboard.TryGetValue(MoveToAction.ForcedDestinationKey, out destination, server.EntMan);
    }

    /// <summary>
    /// Strips the station-wide landmark knowledge <c>SeedKnownBeacons</c> hands out at spawn, so a test can
    /// establish exactly what this passenger does and does not know. Left deliberately explicit rather than
    /// hidden in a helper system: the seeding itself is unchanged this milestone, and these tests are the only
    /// place its effect is deliberately undone.
    /// </summary>
    private static void ForgetEverything(TestPair pair, EntityUid uid)
    {
        pair.Server.EntMan.GetComponent<MemoryComponent>(uid).Memories.Clear();
        pair.Server.EntMan.GetComponent<LocationKnowledgeComponent>(uid).Places.Clear();
    }

    /// <summary>
    /// The bare test map has no floor to speak of, so a frontier probe correctly finds nowhere to stand. Lays
    /// down a real run of plating heading west of the passenger, giving it somewhere it genuinely could walk
    /// to - the physical precondition the frontier mechanism is supposed to detect.
    /// </summary>
    private static void PaintFloorWestOf(TestPair pair, EntityUid uid, int length)
    {
        var server = pair.Server;
        var xform = server.EntMan.GetComponent<TransformComponent>(uid);

        Assert.That(xform.GridUid, Is.Not.Null, "Test setup: the passenger should have spawned on a grid.");
        var gridUid = xform.GridUid!.Value;
        var grid = server.EntMan.GetComponent<MapGridComponent>(gridUid);

        var mapSys = server.System<SharedMapSystem>();
        var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        var origin = mapSys.CoordinatesToTile(gridUid, grid, xform.Coordinates);

        // A three-tile-wide run, starting one tile behind the passenger so it is standing on floor too.
        for (var i = -1; i <= length; i++)
        {
            for (var w = -1; w <= 1; w++)
                mapSys.SetTile(gridUid, grid, new Vector2i(origin.X - i, origin.Y + w), tile);
        }
    }

    /// <summary>Spec section 22: a place the passenger knows the way to but has barely been to is a perfectly
    /// good thing to go and look at, and exploring must be able to use existing known-location machinery.</summary>
    [Test]
    public async Task ExploreStation_SelectsAKnownButUnfamiliarPlace_AndCommitsToTravellingThere()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityCoordinates target = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            ForgetEverything(pair, aiPlayer);

            var origin = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            target = origin.Offset(new Vector2(-8, 0));

            // Exactly one known place, never visited - so there is no ambiguity about what should be chosen.
            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: $"Ты уже знаешь дорогу к «{Unfamiliar}».", importance: 0.25f, source: "landmark",
                location: target, subject: Unfamiliar);
        });

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();

            Assert.That(actions.TryDoAction(aiPlayer, ExploreStationAction.ActionName,
                new ExploreStationActionParams(), out var failReason), Is.True, failReason);

            Assert.That(TryGetForcedDestination(pair, aiPlayer, out var destination), Is.True,
                "Exploring must produce a genuine movement commitment, not just an intent label (spec section 21).");

            Assert.That(destination.TryDistance(server.EntMan, target, out var offBy), Is.True);
            Assert.That(offBy, Is.LessThan(0.5f), "It should be heading for the one place it actually knows.");

            Assert.That(server.EntMan.GetComponent<ExplorationComponent>(aiPlayer).CurrentTargetName,
                Is.EqualTo(Unfamiliar));

            // And the commitment is tracked, so the busy-state machinery does not immediately drop it.
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction,
                Is.EqualTo(ExploreStationAction.ActionName));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The busy-state trap this milestone had to avoid: <see cref="AiBusyStateSystem"/> clears any commitment
    /// whose action name it does not recognise, so a new travelling action that was not taught to it would
    /// look finished on the very next scan.
    /// </summary>
    [Test]
    public async Task ExploreStationCommitment_SurvivesBusyStateScans_UntilArrival()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityCoordinates target = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            ForgetEverything(pair, aiPlayer);

            // Freeze the HTN executor for the observation window. This test is about one thing only: whether
            // the busy-state scanner recognises a commitment named "ExploreStation" at all. Left running, HTN
            // would end the journey for its own perfectly legitimate reasons on this bare test map (no route
            // to build), and a commitment cleared for a good reason is indistinguishable from one cleared by
            // the bug being guarded against.
            var htnComp = server.EntMan.GetComponent<HTNComponent>(aiPlayer);
            server.System<HTNSystem>().SetHTNEnabled((aiPlayer, htnComp), false);

            var origin = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            target = origin.Offset(new Vector2(-8, 0));

            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: $"Ты уже знаешь дорогу к «{Unfamiliar}».", importance: 0.25f, source: "landmark",
                location: target, subject: Unfamiliar);

            server.System<AiActionRegistrySystem>().TryDoAction(aiPlayer, ExploreStationAction.ActionName,
                new ExploreStationActionParams(), out _);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction,
                Is.EqualTo(ExploreStationAction.ActionName),
                "The exploration commitment must still stand after several busy-state scans - an action name " +
                "the scanner does not recognise gets cleared on the very first one.");
        });

        // Now let the destination go, the way arrival itself would, and confirm the commitment resolves
        // rather than lingering forever.
        await server.WaitPost(() =>
        {
            server.EntMan.GetComponent<HTNComponent>(aiPlayer).Blackboard
                .Remove<EntityCoordinates>(MoveToAction.ForcedDestinationKey);
        });

        await pair.RunTicksSync(90);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.Null,
                "Once the destination is gone the AI is no longer busy travelling.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Spec sections 6 case B / 7 / 8 / 21: a passenger that knows no place names at all must still be able to
    /// set off toward somewhere reachable it has no name for, and must physically leave where it started.
    /// </summary>
    [Test]
    public async Task WithNoKnownPlaces_ExploreStation_HeadsForAnUnnamedFrontier_AndThePassengerActuallyMoves()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        Vector2 startPosition = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            ForgetEverything(pair, aiPlayer);
            PaintFloorWestOf(pair, aiPlayer, length: 16);
            startPosition = server.System<SharedTransformSystem>().GetWorldPosition(aiPlayer);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<MemorySystem>().HasAnyKnownDestination(aiPlayer), Is.False,
                "Test setup: this passenger must genuinely know nowhere by name.");

            var controller = server.System<ExplorationControllerSystem>();
            Assert.That(controller.TryGetTarget(aiPlayer, out var target), Is.True,
                "Knowing no place names must not mean having nowhere to explore (spec section 6 case B).");
            Assert.That(target.Kind, Is.EqualTo(ExplorationTargetKind.UnknownFrontier));
            Assert.That(target.PlaceName, Is.Null, "A frontier has no semantic name (spec section 8).");

            Assert.That(server.System<AiActionRegistrySystem>().TryDoAction(aiPlayer,
                ExploreStationAction.ActionName, new ExploreStationActionParams(), out var failReason), Is.True, failReason);

            Assert.That(TryGetForcedDestination(pair, aiPlayer, out _), Is.True);
        });

        // Real ticks, real steering - no teleporting.
        var moved = 0f;
        for (var i = 0; i < 80 && moved < 2f; i++)
        {
            await pair.RunTicksSync(10);
            moved = (server.System<SharedTransformSystem>().GetWorldPosition(aiPlayer) - startPosition).Length();
        }

        await server.WaitAssertion(() =>
        {
            Assert.That(moved, Is.GreaterThan(2f),
                "The passenger has to physically leave where it started - spec section 21 inspects position, not intent.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
