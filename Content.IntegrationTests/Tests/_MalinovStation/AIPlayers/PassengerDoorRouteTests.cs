#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.Components;
using Content.Server.Power.Components;
using Content.Shared.CCVar;
using Content.Shared.Doors.Components;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.6.1, spec sections 13-15/27: door interaction must be *route-aware*.
/// <see cref="AiDoorApproachSystem"/>'s candidate search used to be pure proximity - "nearest closed door
/// within <see cref="DoorApproachComponent.ApproachRadius"/>" - so a travelling AI would walk off its own
/// route to open a door belonging to some unrelated room it merely happened to pass. It now cross-references
/// the AI's real computed <see cref="NPCSteeringComponent.CurrentPath"/>, the same route data vanilla
/// steering itself follows, so only the door actually blocking the way is ever engaged.
///
/// The first test is spec section 27's exact fixture (a door on the route, an unrelated door beside it) and
/// inspects real door interaction rather than action selection. The second proves the underlying invariant
/// deterministically, with no dependence on pathfinding timing: no route means no door engagement at all.
/// </summary>
[TestFixture]
public sealed class PassengerDoorRouteTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "PassengerDoorRouteTestMap";

    [TestPrototypes]
    private static readonly string PassengerDoorRouteTestMap = @$"
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

    /// <summary>Real airlocks need APC power to actually open - this bare test map has no power network, so
    /// bypass that requirement directly rather than standing up a whole power grid, exactly as
    /// <see cref="AiDoorApproachTests"/> already does.</summary>
    private static void UnpowerGate(TestPair pair, EntityUid door)
    {
        if (pair.Server.EntMan.TryGetComponent<ApcPowerReceiverComponent>(door, out var receiver))
            receiver.NeedsPower = false;
    }

    /// <summary>
    /// Spec section 27's fixture: the AI travels north through door A (the only gap in a wall) toward a
    /// destination beyond it, while unrelated door B sits well inside
    /// <see cref="DoorApproachComponent.ApproachRadius"/> to the east, opening onto nothing the AI wants.
    /// </summary>
    [Test]
    public async Task TravellingAi_EngagesTheDoorOnItsRoute_AndIgnoresAnUnrelatedNeighbouringDoor()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid doorOnRoute = default;
        EntityUid unrelatedDoor = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;

            // The route: a wall with a single door-shaped gap directly between the AI and its destination.
            doorOnRoute = server.EntMan.SpawnEntity("Airlock", coords.Offset(new Vector2(0, 3)));
            server.EntMan.SpawnEntity("WallSolid", coords.Offset(new Vector2(-1, 3)));
            server.EntMan.SpawnEntity("WallSolid", coords.Offset(new Vector2(1, 3)));

            // Unrelated: close enough that the old proximity-only scan would have locked onto it, but on no
            // path the AI is following.
            unrelatedDoor = server.EntMan.SpawnEntity("Airlock", coords.Offset(new Vector2(2, 0)));

            UnpowerGate(pair, doorOnRoute);
            UnpowerGate(pair, unrelatedDoor);

            var destination = coords.Offset(new Vector2(0, 6));
            var actions = server.System<AiActionRegistrySystem>();
            Assert.That(actions.TryDoAction(aiPlayer, "MoveTo", new MoveToActionParams(destination), out var reason), Is.True, reason);
        });

        // Watch the whole travel window, recording whether the unrelated door was ever engaged at any point -
        // a single end-state assertion could miss a transient approach that was later abandoned.
        var everEngagedUnrelatedDoor = false;
        var unrelatedDoorEverOpened = false;

        for (var i = 0; i < 60; i++)
        {
            await pair.RunTicksSync(5);

            if (server.EntMan.GetComponent<DoorApproachComponent>(aiPlayer).ActiveDoor == unrelatedDoor)
                everEngagedUnrelatedDoor = true;

            if (server.EntMan.GetComponent<DoorComponent>(unrelatedDoor).State != DoorState.Closed)
                unrelatedDoorEverOpened = true;

            if (server.EntMan.GetComponent<DoorComponent>(doorOnRoute).State == DoorState.Open)
                break;
        }

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.GetComponent<DoorComponent>(doorOnRoute).State, Is.EqualTo(DoorState.Open),
                    "The door genuinely blocking the AI's route should still be opened - route-awareness must not break ordinary travel.");
                Assert.That(everEngagedUnrelatedDoor, Is.False,
                    "A door that is not part of the current route should never be approached (spec section 14: no 'nearest closed door' behaviour).");
                Assert.That(unrelatedDoorEverOpened, Is.False,
                    "An unrelated neighbouring door should never be opened by a travelling AI.");
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(doorOnRoute);
            server.EntMan.DeleteEntity(unrelatedDoor);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The invariant behind the fix, proven without depending on real pathfinding timing: an AI that is
    /// registered as steering but has no route (empty <see cref="NPCSteeringComponent.CurrentPath"/> - it is
    /// already at its target) must not engage a closed door sitting right beside it. Before this milestone the
    /// proximity-only scan would have locked onto it regardless.
    /// </summary>
    [Test]
    public async Task SteeringWithNoRoute_NeverEngagesANeighbouringDoor()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid door = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;

            door = server.EntMan.SpawnEntity("Airlock", coords.Offset(new Vector2(1, 0)));
            UnpowerGate(pair, door);

            // Registered as steering, but already at its own target - so there is no route, and no poly of any
            // path can possibly implicate the door beside it.
            var steering = server.EntMan.EnsureComponent<NPCSteeringComponent>(aiPlayer);
            steering.Coordinates = coords;
            steering.CurrentPath.Clear();

            server.EntMan.GetComponent<DoorApproachComponent>(aiPlayer).ScanAccumulator = 0f;
        });

        var everEngaged = false;
        for (var i = 0; i < 20; i++)
        {
            await pair.RunTicksSync(5);

            if (server.EntMan.GetComponent<DoorApproachComponent>(aiPlayer).ActiveDoor == door)
                everEngaged = true;
        }

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(everEngaged, Is.False,
                    "With no route, a door sitting right beside the AI has nothing to do with anything it is doing - it must be left alone.");
                Assert.That(server.EntMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Closed),
                    "An unrelated door should never be opened by an AI that is not routing through it.");
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(door);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
