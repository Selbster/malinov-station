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
/// AI movement-quality acceptance checks (live playtest follow-up): AiDoorApproachSystem replaces vanilla
/// context-steering's force-blended door approach - the documented cause of AI players "dancing" - with a
/// deterministic walk-up-and-click for the final few tiles, reusing vanilla's own door/access/interaction
/// primitives. Mirrors GoalIntentUnificationTests' "AI actually walks somewhere over real ticks" style
/// (WaitForCondition, generous real tick budgets) - same known-environment-timing caveat as every other
/// real-tick test in this suite.
/// </summary>
[TestFixture]
public sealed class AiDoorApproachTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<JobPrototype> StationEngineer = "StationEngineer";

    private const string Map = "AiDoorApproachTestMap";
    private const string LockedDoor = "TestAiDoorApproachLockedDoor";

    [TestPrototypes]
    private static readonly string AiDoorApproachTestMap = @$"
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
            {StationEngineer}: [ -1, -1 ]

# Real vanilla airlocks (AirlockCommand etc.) wire access through an inserted electronics board
# (ContainerAccessProvider: board, inherited from the base Airlock prototype), not a direct AccessReaderComponent
# check on the door itself - and a Paused-but-boardless door bypasses access entirely (see
# AccessReaderSystem.IsAllowed's own ""Door electronics is kind of a mess"" comment). Explicitly nulling
# containerAccessProvider here reverts to the simple, direct AccessLists check this test actually wants -
# same hand-authored-test-prototype convention LandmarkNavigationTests/AiInteractionTests already use.
- type: entity
  id: {LockedDoor}
  parent: Airlock
  components:
  - type: AccessReader
    containerAccessProvider: null
    access:
      - [ Engineering ]
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

    private static async Task WaitForCondition(TestPair pair, Func<bool> condition, int maxIterations = 100, int ticksPerIteration = 5)
    {
        for (var i = 0; i < maxIterations && !condition(); i++)
            await pair.RunTicksSync(ticksPerIteration);
    }

    /// <summary>Spawns an AI a short distance from a door, with two short flanking wall segments so the door
    /// is the only gap between the AI and a destination directly beyond it - mirrors
    /// LandmarkNavigationTests_BeaconBehindWall's own wall-spawning style, just to force a real approach
    /// rather than proving anything about the wall itself.</summary>
    private async Task<(EntityUid AiPlayer, EntityUid Door)> SpawnAiFacingDoor(TestPair pair, EntityUid station, ProtoId<JobPrototype> job, string doorProto)
    {
        var server = pair.Server;
        EntityUid aiPlayer = default;
        EntityUid door = default;
        EntityCoordinates destination = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(job, station)!.Value;
            AiMovementTestMap.PaintNorthCorridor(pair, aiPlayer);
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;

            door = server.EntMan.SpawnEntity(doorProto, coords.Offset(new Vector2(0, 3)));
            server.EntMan.SpawnEntity("WallSolid", coords.Offset(new Vector2(-1, 3)));
            server.EntMan.SpawnEntity("WallSolid", coords.Offset(new Vector2(1, 3)));

            // Real airlocks need APC power to actually open - this bare test map has no power network, so
            // bypass that requirement directly rather than standing up a whole power grid just for this test.
            if (server.EntMan.TryGetComponent<ApcPowerReceiverComponent>(door, out var receiver))
                receiver.NeedsPower = false;

            destination = coords.Offset(new Vector2(0, 6));
        });

        await pair.RunTicksSync(15);
        await server.WaitPost(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            Assert.That(actions.TryDoAction(aiPlayer, "MoveTo", new MoveToActionParams(destination), out var reason), Is.True, reason);
        });

        return (aiPlayer, door);
    }

    [Test]
    public async Task UnlockedDoor_OpensWithoutExcessiveDancing()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, door) = await SpawnAiFacingDoor(pair, station, Passenger, "Airlock");

        // Generous but bounded budget - if this system were fighting vanilla steering (the dance this
        // replaces), reaching Open reliably within this window would not be a safe assumption.
        await WaitForCondition(pair, () => server.EntMan.GetComponent<DoorComponent>(door).State == DoorState.Open, maxIterations: 60, ticksPerIteration: 5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Open),
                "An unlocked door directly in the AI's path should open without needing an excessive number of ticks.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(door);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task LockedDoorWithoutAccess_BacksOffCleanly()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        // A Passenger has no Engineering access.
        var (aiPlayer, door) = await SpawnAiFacingDoor(pair, station, Passenger, LockedDoor);

        await server.WaitAssertion(() =>
        {
            var accessReader = server.System<Content.Shared.Access.Systems.AccessReaderSystem>();
            Assert.That(accessReader.IsAllowed(aiPlayer, door), Is.False,
                "Test setup sanity check: a Passenger should not genuinely be allowed through an Engineering-locked door.");
        });

        await WaitForCondition(pair, () => server.EntMan.GetComponent<DoorApproachComponent>(aiPlayer).DeniedDoors.ContainsKey(door), maxIterations: 60, ticksPerIteration: 5);

        await server.WaitAssertion(() =>
        {
            var approach = server.EntMan.GetComponent<DoorApproachComponent>(aiPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(approach.DeniedDoors, Does.ContainKey(door),
                    "A door the AI has no access for should be recorded as denied.");
                Assert.That(server.EntMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Closed),
                    "An access-locked door should never actually open just because the AI walked up to it.");
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(door);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task LockedDoorWithMatchingAccess_Opens()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        // A StationEngineer's default loadout includes Engineering access.
        var (aiPlayer, door) = await SpawnAiFacingDoor(pair, station, StationEngineer, LockedDoor);

        await WaitForCondition(pair, () => server.EntMan.GetComponent<DoorComponent>(door).State == DoorState.Open, maxIterations: 60, ticksPerIteration: 5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Open),
                "An AI with genuinely matching access should have the door actually open, via a real AccessReaderSystem check.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(door);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task LegacyAiPlayer_AlsoGetsDoorApproachComponent()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value; // legacy, no cognitiveMode
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<DoorApproachComponent>(aiPlayer), Is.True,
                "Door approach is a baseline movement-quality fix, not an LLM-driven behaviour - every AI player should get it.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
