#nullable enable
using System.Linq;
using System.Threading;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Pathfinding;
using Content.Server.Power.Components;
using Content.Shared.CCVar;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.GameTicking;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Power;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.6.3: the ordered door judgement (<see cref="AiDoorApproachSystem.JudgeDoor"/>).
///
/// Live play produced the behaviour this file exists to prevent - passengers repeatedly walking into supply
/// airlocks they had no access to, and into shutters they physically cannot open at all. The fix is not "try
/// and see": every door is classified before a step is taken toward it, in a fixed order, and each test below
/// pins one rung of that order. The ordering tests matter as much as the individual verdicts - the whole point
/// of asking "is it welded" before "has it lost power" is that a welded door must never be mistaken for a
/// prying opportunity.
///
/// These call the classifier directly rather than watching an AI walk about, so nothing here depends on
/// pathfinding or steering timing.
/// </summary>
[TestFixture]
public sealed class PassengerDoorJudgementTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "PassengerDoorJudgementTestMap";
    private const string Shutter = "TestAiJudgementShutter";
    private const string CargoDoor = "TestAiJudgementCargoDoor";
    private const string PublicDoor = "TestAiJudgementPublicDoor";

    /// <summary>Half-width of the plating laid for routing tests. The blocking wall must span all of it.</summary>
    private const int FloorRadius = 6;

    [TestPrototypes]
    private static readonly string Prototypes = @$"
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

# What a shutter, window shutter or blast door really is: BaseShutter sets clickOpen false, so no character
# opens one by hand. Derived from Airlock so the test isolates that single property rather than also depending
# on signal wiring.
- type: entity
  id: {Shutter}
  parent: Airlock
  components:
  - type: Door
    clickOpen: false

# Access checked on the door itself rather than through inserted electronics - same convention the other AI
# door tests use, and for the same reason.
- type: entity
  id: {CargoDoor}
  parent: Airlock
  components:
  - type: AccessReader
    containerAccessProvider: null
    access:
      - [ Cargo ]

- type: entity
  id: {PublicDoor}
  parent: Airlock
  components:
  - type: AccessReader
    containerAccessProvider: null
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

    /// <summary>Spawns a passenger plus a door of <paramref name="proto"/> beside it, both ready to judge.</summary>
    private async Task<(EntityUid Ai, EntityUid Door)> SpawnAiAndDoor(TestPair pair, EntityUid station, string proto)
    {
        var server = pair.Server;
        EntityUid ai = default;
        EntityUid door = default;

        await server.WaitPost(() =>
        {
            ai = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(ai).Coordinates;
            door = server.EntMan.SpawnEntity(proto, coords.Offset(new Vector2(2, 0)));
        });

        await pair.RunTicksSync(5);
        return (ai, door);
    }

    /// <summary>
    /// Sets both halves of a door's power story: the receiver's own reading, and the cached flag the airlock
    /// keeps, which is only ever written in response to a PowerChangedEvent - so the event is what raises it.
    ///
    /// Must be called in the same tick the judgement is made. A bare test map has no power network at all, so
    /// the receiver recomputes itself back to unpowered the moment any tick runs; there is nothing here to
    /// keep a door alive between calls.
    /// </summary>
    private static void SetPower(Robust.UnitTesting.RobustIntegrationTest.ServerIntegrationInstance server, EntityUid door, bool powered)
    {
        if (server.EntMan.TryGetComponent<ApcPowerReceiverComponent>(door, out var receiver))
            receiver.Powered = powered;

        var ev = new PowerChangedEvent(powered, powered ? 1f : 0f);
        server.EntMan.EventBus.RaiseLocalEvent(door, ref ev);
    }

    /// <summary>The bare test map has no floor at all, so there is genuinely nowhere to walk and every path
    /// request comes back NoPath regardless of doors. Lays a square of plating around the passenger - the
    /// physical precondition for any of these routing questions to mean anything.</summary>
    private static void PaintFloorAround(TestPair pair, EntityUid uid, int radius)
    {
        var server = pair.Server;
        var xform = server.EntMan.GetComponent<TransformComponent>(uid);

        Assert.That(xform.GridUid, Is.Not.Null, "Test setup: the passenger should have spawned on a grid.");
        var gridUid = xform.GridUid!.Value;
        var grid = server.EntMan.GetComponent<MapGridComponent>(gridUid);
        grid.CanSplit = false;

        var mapSys = server.System<SharedMapSystem>();
        var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        var origin = mapSys.CoordinatesToTile(gridUid, grid, xform.Coordinates);

        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
                mapSys.SetTile(gridUid, grid, new Vector2i(origin.X + x, origin.Y + y), tile);
        }
    }

    private async Task Cleanup(TestPair pair, params EntityUid[] entities)
    {
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            foreach (var entity in entities)
            {
                if (!server.EntMan.Deleted(entity))
                    server.EntMan.DeleteEntity(entity);
            }
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// AI Players 0.6.3: the check that was missing between deciding where to go and setting off.
    ///
    /// Vanilla's pathfinder is deliberately optimistic about access doors - a per-tile flag cannot encode who
    /// a door opens for, so planning assumes it opens and the real check happens at the door. That works only
    /// if whoever commits to the route performs that check, and nobody did: the AI walked the whole way,
    /// found out at the door, abandoned, replanned, and often set off the same way again.
    ///
    /// Both directions are asserted from one fixture, because a filter that rejected everything would pass
    /// the interesting half on its own.
    /// </summary>
    [Test]
    public async Task ARouteThroughADoorThisPassengerCannotOpen_IsRecognisedFromThePathAlone()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid ai = default;
        EntityUid lockedDoor = default;
        EntityCoordinates beyondLocked = default;
        EntityCoordinates beyondPublic = default;

        await server.WaitPost(() =>
        {
            ai = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            PaintFloorAround(pair, ai, radius: FloorRadius);

            var coords = server.EntMan.GetComponent<TransformComponent>(ai).Coordinates;

            // A wall long enough that there is no way round it inside the painted floor, whose single gap is
            // a door this passenger has no access to - and a destination on the far side of it.
            lockedDoor = server.EntMan.SpawnEntity(CargoDoor, coords.Offset(new Vector2(0, 3)));

            // Spans the full width of the painted floor. A wall narrower than the floor is not a wall - the
            // route simply goes round the end of it, which is how the first version of this fixture quietly
            // tested nothing at all.
            for (var x = -FloorRadius; x <= FloorRadius; x++)
            {
                if (x != 0)
                    server.EntMan.SpawnEntity("WallSolid", coords.Offset(new Vector2(x, 3)));
            }

            beyondLocked = coords.Offset(new Vector2(0, 5));

            // Somewhere on this side of the wall, reached without passing through anything at all.
            beyondPublic = coords.Offset(new Vector2(4, 0));

            if (server.EntMan.TryGetComponent<ApcPowerReceiverComponent>(lockedDoor, out var receiver))
                receiver.NeedsPower = false;
        });

        await pair.RunTicksSync(60);

        // Paths are produced by a job queue, so they are started here and collected after ticks have run.
        Task<PathResultEvent>? throughDoor = null;
        Task<PathResultEvent>? openFloor = null;

        await server.WaitPost(() =>
        {
            var pathfinding = server.System<PathfindingSystem>();
            var flags = pathfinding.GetFlags(ai);
            var from = server.EntMan.GetComponent<TransformComponent>(ai).Coordinates;

            throughDoor = pathfinding.GetPathSafe(ai, from, beyondLocked, 1f, CancellationToken.None, flags);
            openFloor = pathfinding.GetPathSafe(ai, from, beyondPublic, 1f, CancellationToken.None, flags);
        });

        await pair.RunTicksSync(120);

        // The jobs have had their ticks by now, so awaiting here returns straight away rather than blocking
        // the server loop that would have to produce the answer.
        var throughResult = await throughDoor!;
        var openResult = await openFloor!;

        await server.WaitAssertion(() =>
        {
            var doors = server.System<AiDoorApproachSystem>();

            var blockedFound = doors.TryFindImpassableDoorOnRoute(ai, throughResult.Path, out var found, out var reason);
            var openFound = doors.TryFindImpassableDoorOnRoute(ai, openResult.Path, out _, out _);

            // Two mechanisms can rule this journey out and either is a correct answer, so the assertion is
            // on the guarantee rather than on which one delivered it: vanilla's own denial memory can drop
            // the door out of the graph so no route comes back at all, or a route comes back and this
            // system's filter rejects it. What must never happen is a usable route through a door this
            // passenger cannot open - that is the state that had bots grinding against departmental
            // airlocks.
            var journeyRuledOut = throughResult.Result != PathResult.Path || blockedFound;

            Assert.Multiple(() =>
            {
                var anchored = server.EntMan.GetComponent<TransformComponent>(lockedDoor).Anchored;
                var doorPolys = throughResult.Path.Count(poly =>
                    (poly.Data.Flags & Content.Shared.NPC.PathfindingBreadcrumbFlag.Door) != 0x0);

                Assert.That(journeyRuledOut, Is.True,
                    $"The only way through is a cargo door, and a passenger does not open one. " +
                    $"[anchored={anchored} pathLen={throughResult.Path.Count} doorPolys={doorPolys}]");

                if (blockedFound)
                {
                    Assert.That(found, Is.EqualTo(lockedDoor));
                    Assert.That(reason, Does.Contain("доступ"),
                        "The reason travels into the trace and the AI's memory.");
                }

                Assert.That(openResult.Result, Is.EqualTo(PathResult.Path), "Open floor should be walkable.");
                Assert.That(openFound, Is.False,
                    "A walk across open floor must not be rejected - a filter that refuses everything is no filter.");
            });
        });

        await Cleanup(pair, ai, lockedDoor);
    }

    /// <summary>
    /// AI Players 0.6.3: the routing half of the shutter problem, held in place from our own side rather than
    /// by editing vanilla.
    ///
    /// PathFlags.Interact is read in exactly two vanilla places, both about doors carrying no AccessReader:
    /// the pathfinder prices such a door cheaply, and steering then walks up and clicks it once per tick. The
    /// only station doors in that set are shutters and blast-door panels, which open for nobody - so the flag
    /// bought a route into a wall and an NPC grinding against it. Everything an AI actually needs to walk
    /// through carries an AccessReader and travels the AccessInteract path instead.
    ///
    /// If someone ever switches NavInteract back on, this is the test that should stop them.
    /// </summary>
    [Test]
    public async Task AnAiPlayer_DoesNotAskThePathfinderToRouteItThroughLeverOperatedDoors()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid ai = default;
        await server.WaitPost(() =>
            ai = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value);
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var flags = server.System<PathfindingSystem>().GetFlags(ai);

            Assert.Multiple(() =>
            {
                Assert.That(flags.HasFlag(PathFlags.Interact), Is.False,
                    "Interact only ever bought a cheap route into shutters, which open for nobody.");
                Assert.That(flags.HasFlag(PathFlags.AccessInteract), Is.True,
                    "Airlocks and firelocks all carry an AccessReader, so this is the flag that real travel needs.");
            });
        });

        await Cleanup(pair, ai);
    }

    /// <summary>
    /// A windoor is the glass hatch set into a service counter - the thing a parcel gets passed through at a
    /// reception desk. It is a real, clickable, access-gated door, so every later rung would happily wave it
    /// through; routing a character into one means routing them into the counter it is bolted to.
    ///
    /// Deliberately uses the shipped Windoor prototype rather than a synthetic stand-in, because the point is
    /// that real map content is classified correctly. It is told apart by what it physically collides with:
    /// an airlock carries FullTileMask and stops people, a windoor carries TabletopMachineMask and does not.
    /// </summary>
    [Test]
    public async Task ARealWindoor_IsACounterHatch_NotAWayThrough()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (ai, door) = await SpawnAiAndDoor(pair, station, "Windoor");

        await server.WaitAssertion(() =>
        {
            SetPower(server, door, powered: true);

            var doorComp = server.EntMan.GetComponent<DoorComponent>(door);
            var judgement = server.System<AiDoorApproachSystem>().JudgeDoor(ai, door, doorComp, includeMomentaryState: false);

            Assert.That(judgement.Passability, Is.EqualTo(DoorPassability.Blocked),
                "A powered, unlocked windoor still is not a doorway - it is a hatch on a counter.");
        });

        await Cleanup(pair, ai, door);
    }

    /// <summary>
    /// Rung 1, and the reason it is first: nothing below it could change the answer. This is the shutter case
    /// straight from live play - the AI kept trying, because a shutter carries no AccessReaderComponent at
    /// all and so answers "yes, you have access" to anyone who asks that narrower question.
    /// </summary>
    [Test]
    public async Task AShutterIsAWall_BecauseNobodyAtAllOpensItByHand()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (ai, door) = await SpawnAiAndDoor(pair, station, Shutter);

        await server.WaitAssertion(() =>
        {
            var doorComp = server.EntMan.GetComponent<DoorComponent>(door);
            var judgement = server.System<AiDoorApproachSystem>().JudgeDoor(ai, door, doorComp, includeMomentaryState: false);

            Assert.That(judgement.Passability, Is.EqualTo(DoorPassability.Blocked),
                "A shutter opens on a lever, so it is a wall to a passenger no matter what else is true of it.");
        });

        await Cleanup(pair, ai, door);
    }

    /// <summary>
    /// Rung 2 above rung 3, which is the whole reason the order is fixed. The door here is welded *and*
    /// unpowered *and* the AI is holding a crowbar - every ingredient of a prying opportunity except that
    /// vanilla cancels prying on a welded door. Getting this order wrong would have the AI stand there
    /// levering at something that can never give.
    /// </summary>
    [Test]
    public async Task AWeldedDoorIsAWall_EvenUnpoweredWithACrowbarInHand()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (ai, door) = await SpawnAiAndDoor(pair, station, PublicDoor);

        EntityUid crowbar = default;
        await server.WaitPost(() =>
        {
            SetPower(server, door, powered: false);
            server.System<SharedDoorSystem>().SetState(door, DoorState.Welded);

            var coords = server.EntMan.GetComponent<TransformComponent>(ai).Coordinates;
            crowbar = server.EntMan.SpawnEntity("Crowbar", coords);
            server.System<SharedHandsSystem>().TryPickupAnyHand(ai, crowbar);
        });

        await server.WaitAssertion(() =>
        {
            var doorComp = server.EntMan.GetComponent<DoorComponent>(door);
            var judgement = server.System<AiDoorApproachSystem>().JudgeDoor(ai, door, doorComp, includeMomentaryState: false);

            Assert.That(judgement.Passability, Is.EqualTo(DoorPassability.Blocked),
                "Welded is asked before power precisely so a welded door is never taken for a prying job.");
        });

        await Cleanup(pair, ai, door, crowbar);
    }

    /// <summary>
    /// Rung 3, both ways. A dead airlock is the one case a crowbar solves - vanilla cancels prying on a
    /// *powered* airlock and permits it on an unpowered one - so carrying the tool is the entire difference
    /// between a way through and a wall.
    /// </summary>
    [Test]
    public async Task AnUnpoweredDoor_IsAWayThroughWithACrowbar_AndAWallWithout()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (ai, door) = await SpawnAiAndDoor(pair, station, PublicDoor);

        var system = server.System<AiDoorApproachSystem>();

        await server.WaitPost(() => SetPower(server, door, powered: false));

        DoorJudgement empty = default;
        await server.WaitAssertion(() =>
        {
            var doorComp = server.EntMan.GetComponent<DoorComponent>(door);
            empty = system.JudgeDoor(ai, door, doorComp, includeMomentaryState: true);
        });

        EntityUid crowbar = default;
        await server.WaitPost(() =>
        {
            var coords = server.EntMan.GetComponent<TransformComponent>(ai).Coordinates;
            crowbar = server.EntMan.SpawnEntity("Crowbar", coords);
            server.System<SharedHandsSystem>().TryPickupAnyHand(ai, crowbar);
        });

        await server.WaitAssertion(() =>
        {
            var doorComp = server.EntMan.GetComponent<DoorComponent>(door);
            var armed = system.JudgeDoor(ai, door, doorComp, includeMomentaryState: true);

            Assert.Multiple(() =>
            {
                Assert.That(empty.Passability, Is.EqualTo(DoorPassability.Blocked),
                    "Empty-handed, a dead airlock is simply a wall.");
                Assert.That(armed.Passability, Is.EqualTo(DoorPassability.Pry),
                    "With a crowbar it is a way through, and the AI should know that before walking over.");
                Assert.That(armed.Tool, Is.EqualTo(crowbar),
                    "The judgement must name the tool it counted on, or the approach cannot act on it.");
            });
        });

        await Cleanup(pair, ai, door, crowbar);
    }

    /// <summary>
    /// Rung 4 - the case the whole milestone started from: a passenger repeatedly walking into supply doors.
    /// The verdict also has to say what the door wanted, because that sentence is what reaches the decision
    /// trace and the AI's own memory; "нельзя" alone explains nothing to anyone reading it afterwards.
    /// </summary>
    [Test]
    public async Task ADoorDemandingAccessThePassengerLacks_IsAWall_AndSaysWhatItWanted()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (ai, door) = await SpawnAiAndDoor(pair, station, CargoDoor);

        await server.WaitAssertion(() =>
        {
            SetPower(server, door, powered: true);

            var doorComp = server.EntMan.GetComponent<DoorComponent>(door);
            var judgement = server.System<AiDoorApproachSystem>().JudgeDoor(ai, door, doorComp, includeMomentaryState: false);

            Assert.Multiple(() =>
            {
                Assert.That(judgement.Passability, Is.EqualTo(DoorPassability.Blocked),
                    "A passenger has no cargo access, and must know that without walking into the door first.");
                Assert.That(judgement.Reason, Does.Contain("доступ"),
                    "The reason is what ends up in the trace and in memory - it has to name the problem.");
            });
        });

        await Cleanup(pair, ai, door);
    }

    /// <summary>
    /// The other side of every rule above: an ordinary working door with nothing to prove is simply opened.
    /// Without this, a classifier that returned Blocked for everything would pass the whole rest of the file.
    /// </summary>
    [Test]
    public async Task AnOrdinaryWorkingDoor_IsSimplyOpened()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (ai, door) = await SpawnAiAndDoor(pair, station, PublicDoor);

        await server.WaitAssertion(() =>
        {
            SetPower(server, door, powered: true);

            var doorComp = server.EntMan.GetComponent<DoorComponent>(door);
            var judgement = server.System<AiDoorApproachSystem>().JudgeDoor(ai, door, doorComp, includeMomentaryState: false);

            Assert.That(judgement.Passability, Is.EqualTo(DoorPassability.Open),
                "A powered, unwelded, unlocked door is the ordinary case and must stay walkable.");
        });

        await Cleanup(pair, ai, door);
    }

    /// <summary>
    /// The cached-power trap, kept nailed down. <see cref="AirlockComponent.Powered"/> starts false and is
    /// only ever written when a PowerChangedEvent arrives, so a door that has not yet been told about its own
    /// power reads as dead. Trusting that flag alone would have the AI write off working doors as "обесточена,
    /// а вскрыть нечем" - and a door written off is not approached again for ninety seconds.
    /// </summary>
    [Test]
    public async Task ADoorWhoseReceiverStillHasPower_IsNotTakenForADeadOne()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (ai, door) = await SpawnAiAndDoor(pair, station, PublicDoor);

        await server.WaitPost(() =>
        {
            // Exactly the stale-cache shape: the airlock's own flag says dead, the receiver says otherwise.
            var dead = new PowerChangedEvent(false, 0f);
            server.EntMan.EventBus.RaiseLocalEvent(door, ref dead);
            if (server.EntMan.TryGetComponent<ApcPowerReceiverComponent>(door, out var receiver))
                receiver.Powered = true;
        });

        await server.WaitAssertion(() =>
        {
            var doorComp = server.EntMan.GetComponent<DoorComponent>(door);
            // Close-range, because that is the only place power is judged at all now.
            var judgement = server.System<AiDoorApproachSystem>().JudgeDoor(ai, door, doorComp, includeMomentaryState: true);

            Assert.That(judgement.Passability, Is.Not.EqualTo(DoorPassability.Pry),
                "The receiver is the physical fact; the airlock flag is a cache that can simply be behind, and " +
                "a door with power must never be taken for a prying job.");
        });

        await Cleanup(pair, ai, door);
    }
}
