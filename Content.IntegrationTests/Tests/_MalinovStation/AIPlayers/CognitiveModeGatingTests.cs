#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 2.0 Milestone 1's core safety guarantee (spec section 28: "must not change vanilla/legacy
/// behaviour"): a legacy AI player (spawned without <c>cognitiveMode: true</c>) must never receive any of the
/// new cognitive-only components, and every new side effect gated on <see cref="CognitiveModeComponent"/>
/// must be a genuine no-op for it - not "does nothing useful" but "provably does not run at all."
/// </summary>
[TestFixture]
public sealed class CognitiveModeGatingTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "CognitiveModeGatingTestMap";

    [TestPrototypes]
    private static readonly string CognitiveModeGatingTestMap = @$"
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

    /// <summary>
    /// A plain (non-cognitive) AI player - exactly what every existing AI player spawn produced before this
    /// milestone - never gets any of the new cognitive-only components.
    /// </summary>
    [Test]
    public async Task LegacyAiPlayer_NeverReceivesAnyCognitiveComponents()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;

        await server.WaitAssertion(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station);
            Assert.That(aiPlayer, Is.Not.Null);
            var uid = aiPlayer!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<CognitiveModeComponent>(uid), Is.False);
                Assert.That(server.EntMan.HasComponent<EmotionComponent>(uid), Is.False);
                Assert.That(server.EntMan.HasComponent<BeliefComponent>(uid), Is.False);
                Assert.That(server.EntMan.HasComponent<DesireComponent>(uid), Is.False);
                Assert.That(server.EntMan.HasComponent<IntentComponent>(uid), Is.False);
            });
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// AiTraceSystem's feedback-loop memory writes (spec section 11's "observe result") are hard-gated on
    /// CognitiveModeComponent - a legacy AI player's memory count must be provably unaffected by the exact
    /// same trace event a cognitive AI player would turn into an outcome memory. GoalFailed is public and
    /// exercised directly here (rather than reproducing the full no-welder-repair scenario
    /// GoalIntentUnificationTests uses to trigger it organically) since it shares the identical
    /// HasComp&lt;CognitiveModeComponent&gt; gate ActionFailed/PlanInterrupted also use internally.
    /// </summary>
    [Test]
    public async Task LegacyAiPlayer_TraceEventsNeverWriteMemory()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;
        var memoriesBefore = 0;

        await server.WaitAssertion(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station);
            Assert.That(aiPlayer, Is.Not.Null);
            var uid = aiPlayer!.Value;

            Assert.That(server.EntMan.HasComponent<CognitiveModeComponent>(uid), Is.False,
                "Test setup: this AI player must be legacy (non-cognitive).");

            memoriesBefore = server.EntMan.GetComponent<MemoryComponent>(uid).Memories.Count;

            server.System<AiTraceSystem>().GoalFailed(uid, "RepairMachine", "NoProgress");
        });

        await server.WaitAssertion(() =>
        {
            var memoriesAfter = server.EntMan.GetComponent<MemoryComponent>(aiPlayer!.Value).Memories.Count;
            Assert.That(memoriesAfter, Is.EqualTo(memoriesBefore),
                "A legacy AI player's memory should be completely unaffected by a trace event that would create an outcome memory for a cognitive one.");
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// A cognitive-mode AI player is the mirror case: it does receive every new component, and the same
    /// GoalFailed trace event does create an outcome memory for it - proving the gate actually lets the
    /// intended behaviour through, not just that it blocks the legacy case.
    /// </summary>
    [Test]
    public async Task CognitiveAiPlayer_ReceivesComponentsAndTraceEventsWriteMemory()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;
        var memoriesBefore = 0;

        await server.WaitAssertion(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(Passenger, station, cognitiveMode: true);
            Assert.That(aiPlayer, Is.Not.Null);
            var uid = aiPlayer!.Value;

            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<CognitiveModeComponent>(uid), Is.True);
                Assert.That(server.EntMan.HasComponent<EmotionComponent>(uid), Is.True);
                Assert.That(server.EntMan.HasComponent<BeliefComponent>(uid), Is.True);
                Assert.That(server.EntMan.HasComponent<DesireComponent>(uid), Is.True);
                Assert.That(server.EntMan.HasComponent<IntentComponent>(uid), Is.True);
            });

            memoriesBefore = server.EntMan.GetComponent<MemoryComponent>(uid).Memories.Count;

            server.System<AiTraceSystem>().GoalFailed(uid, "RepairMachine", "NoProgress");
        });

        await server.WaitAssertion(() =>
        {
            var memoriesAfter = server.EntMan.GetComponent<MemoryComponent>(aiPlayer!.Value).Memories.Count;
            Assert.That(memoriesAfter, Is.EqualTo(memoriesBefore + 1),
                "A cognitive AI player should have gained exactly one outcome memory from the GoalFailed trace event.");
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
