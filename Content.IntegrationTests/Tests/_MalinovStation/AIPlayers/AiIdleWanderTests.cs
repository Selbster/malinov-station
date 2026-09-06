#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
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
/// AI movement-quality acceptance checks (live playtest follow-up): AIPlayerIdleCompound biases idle
/// wandering toward remembered places (PickRememberedOrAccessibleOperator) instead of a uniformly random
/// accessible point, so idle behaviour looks purposeful.
///
/// AI Players 0.6.3 narrowed that contract: a remembered place is used when the AI can actually get to it,
/// and skipped when it cannot. Wandering toward somewhere unreachable was how passengers ended up walking
/// into departmental airlocks over and over. So these fixtures now lay real floor - on the bare map every
/// destination is unreachable, which used to leave idle wander producing nothing at all. Deliberately doesn't require cognitive mode -
/// MemoryComponent is universal, only LandmarkPerceptionSystem (which populates it passively) is
/// cognitive-only, so these tests seed memory directly, matching LandmarkNavigationTests' own convention.
/// </summary>
[TestFixture]
public sealed class AiIdleWanderTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string TargetCoordinatesKey = "TargetCoordinates";

    private const string Map = "AiIdleWanderTestMap";

    [TestPrototypes]
    private static readonly string AiIdleWanderTestMap = @$"
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

    private static async Task WaitForCondition(TestPair pair, Func<bool> condition, int maxIterations = 100, int ticksPerIteration = 5)
    {
        for (var i = 0; i < maxIterations && !condition(); i++)
            await pair.RunTicksSync(ticksPerIteration);
    }

    /// <summary>Spawns an AI player with no urgent needs (Rest/Socialize suppressed) so the root compound
    /// naturally falls through to AIPlayerIdleCompound.</summary>
    /// <summary>The bare test map carries no floor at all, so there is physically nowhere to walk and no
    /// destination can ever be produced. Lays a square of plating around the passenger.</summary>
    private static void PaintFloorAround(TestPair pair, EntityUid uid, int radius)
    {
        var server = pair.Server;
        var xform = server.EntMan.GetComponent<TransformComponent>(uid);

        Assert.That(xform.GridUid, Is.Not.Null, "Test setup: the passenger should have spawned on a grid.");
        var gridUid = xform.GridUid!.Value;
        var grid = server.EntMan.GetComponent<MapGridComponent>(gridUid);

        var mapSys = server.System<SharedMapSystem>();
        var tile = new Tile(server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
        var origin = mapSys.CoordinatesToTile(gridUid, grid, xform.Coordinates);

        for (var x = -radius; x <= radius; x++)
        {
            for (var y = -radius; y <= radius; y++)
                mapSys.SetTile(gridUid, grid, new Vector2i(origin.X + x, origin.Y + y), tile);
        }
    }

    private async Task<EntityUid> SpawnIdleAiPlayer(TestPair pair, EntityUid station)
    {
        var server = pair.Server;
        EntityUid aiPlayer = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value;
            var needs = server.EntMan.GetComponent<NeedsComponent>(aiPlayer);
            needs.Fatigue = 0f;
            needs.SocialNeed = 0f;
            server.EntMan.GetComponent<GoalComponent>(aiPlayer).ReconsiderAccumulator = 0f;

            PaintFloorAround(pair, aiPlayer, radius: 12);
        });

        // Deliberately no ticks here. Idle wander commits to a destination once it picks one (AI Players
        // 0.6.3), so letting the passenger start pottering before a test has seeded its memory would mean
        // testing against a decision made in ignorance of the very thing being seeded.
        return aiPlayer;
    }

    [Test]
    public async Task IdleWander_WithRememberedLocation_TargetsIt()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var aiPlayer = await SpawnIdleAiPlayer(pair, station);
        EntityCoordinates remembered = default;

        await server.WaitPost(() =>
        {
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            remembered = coords.Offset(new Vector2(5, 5));
            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Break Room\".", importance: 0.5f, source: "landmark",
                location: remembered, subject: "Break Room");
        });

        await WaitForCondition(pair, () =>
            server.EntMan.GetComponent<HTNComponent>(aiPlayer).Blackboard
                .TryGetValue<EntityCoordinates>(TargetCoordinatesKey, out _, server.EntMan));

        await server.WaitAssertion(() =>
        {
            var htn = server.EntMan.GetComponent<HTNComponent>(aiPlayer);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(TargetCoordinatesKey, out var target, server.EntMan), Is.True,
                "Idle wander should have picked a target within the test's wait budget.");
            Assert.That(target, Is.EqualTo(remembered),
                "With a known place remembered and nothing else competing, idle wander should target it rather than a random point.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task IdleWander_WithNoMemory_StillPicksAValidTarget()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        // No memory seeded - legacy AI players never populate location memory at all (LandmarkPerceptionSystem
        // is cognitive-only), so this is also the legacy-AI-unaffected proof.
        var aiPlayer = await SpawnIdleAiPlayer(pair, station);

        await WaitForCondition(pair, () =>
            server.EntMan.GetComponent<HTNComponent>(aiPlayer).Blackboard
                .TryGetValue<EntityCoordinates>(TargetCoordinatesKey, out _, server.EntMan));

        await server.WaitAssertion(() =>
        {
            var htn = server.EntMan.GetComponent<HTNComponent>(aiPlayer);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(TargetCoordinatesKey, out _, server.EntMan), Is.True,
                "With nothing remembered, idle wander must still fall back to picking a real accessible point, not fail outright.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
