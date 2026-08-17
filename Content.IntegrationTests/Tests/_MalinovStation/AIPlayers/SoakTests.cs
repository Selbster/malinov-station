#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// Spec Stabilization milestone stage 21: a soak-test harness that leaves a population of AI players running
/// their normal unattended loop for a sustained stretch and reports the same per-run numbers spec section 13
/// asks for from live human-observed runs (goals stuck/failed, replans, interruptions, actions failed, LLM
/// calls/failures) - as real <see cref="AiTraceSystem.TraceEventsMetric"/>/<see cref="GoalSystem"/> counter
/// deltas, not log-scraping. Split in two, per spec: a short, deterministic tier that runs in every normal
/// suite/CI pass (fast enough to catch a structural regression on every commit), and a long tier simulating
/// the spec's actual 30 real-time minutes, marked <see cref="ExplicitAttribute"/> so it never blocks CI and is
/// only ever run manually or by a nightly job.
/// </summary>
[TestFixture]
public sealed class SoakTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<JobPrototype> StationEngineer = "StationEngineer";

    private const string Map = "AIPlayerSoakTestMap";

    [TestPrototypes]
    private static readonly string AIPlayerSoakTestMap = @$"
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
        server.CfgMan.SetCVar(Content.Shared.CCVar.CCVars.GameMap, Map);
        var ticker = server.System<GameTicker>();

        ticker.ToggleReadyAll(true);
        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        var station = server.EntMan.EntityQuery<StationDataComponent>().Select(x => x.Owner).FirstOrDefault();
        Assert.That(server.EntMan.EntityExists(station), "Expected a station to exist after round start.");
        return station;
    }

    private readonly record struct SoakSnapshot(
        double Reconsiderations,
        double GoalChanges,
        double GoalStuck,
        double GoalFailed,
        double Replanned,
        double PlanInterrupted,
        double PlanResumed,
        double ActionFailed,
        double LlmRequests,
        double LlmFailures);

    private static SoakSnapshot TakeSnapshot() => new(
        Reconsiderations: GoalSystem.ReconsiderationsMetric.Value,
        GoalChanges: GoalSystem.GoalChangesMetric.Value,
        GoalStuck: AiTraceSystem.TraceEventsMetric.WithLabels("GoalStuck").Value,
        GoalFailed: AiTraceSystem.TraceEventsMetric.WithLabels("GoalFailed").Value,
        Replanned: AiTraceSystem.TraceEventsMetric.WithLabels("Replanned").Value,
        PlanInterrupted: AiTraceSystem.TraceEventsMetric.WithLabels("PlanInterrupted").Value,
        PlanResumed: AiTraceSystem.TraceEventsMetric.WithLabels("PlanResumed").Value,
        ActionFailed: AiTraceSystem.TraceEventsMetric.WithLabels("ActionFailed").Value,
        LlmRequests: LlmGatewaySystem.LlmRequestsMetric.Value,
        LlmFailures: AiTraceSystem.TraceEventsMetric.WithLabels("LlmFailure").Value);

    /// <summary>
    /// Spawns a mixed-job population, runs the normal unattended AI loop for <paramref name="totalTicks"/>,
    /// checking structural health periodically rather than only at the end (a population that goes invalid
    /// for a stretch and happens to recover before the final check should still fail the soak), then reports
    /// the spec section 13/24-style summary and asserts the population is still healthy.
    /// </summary>
    private async Task RunSoak(TestPair pair, int aiPlayerCount, int totalTicks, int checkEveryTicks, string label)
    {
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var aiPlayers = new List<EntityUid>();
        await server.WaitPost(() =>
        {
            var aiPlayerSystem = server.System<AIPlayerSystem>();
            for (var i = 0; i < aiPlayerCount; i++)
            {
                var job = i % 2 == 0 ? Passenger : StationEngineer;
                var uid = aiPlayerSystem.SpawnAiPlayer(job, station);
                Assert.That(uid, Is.Not.Null, $"Failed to spawn AI player #{i}.");
                aiPlayers.Add(uid!.Value);
            }
        });

        var before = TakeSnapshot();
        var elapsedTicks = 0;

        while (elapsedTicks < totalTicks)
        {
            var batch = Math.Min(checkEveryTicks, totalTicks - elapsedTicks);
            await pair.RunTicksSync(batch);
            elapsedTicks += batch;

            await server.WaitAssertion(() =>
            {
                foreach (var uid in aiPlayers)
                {
                    Assert.That(server.EntMan.EntityExists(uid), Is.True,
                        $"[{label}] {uid} should still exist after {elapsedTicks}/{totalTicks} ticks.");

                    var goal = server.EntMan.GetComponent<GoalComponent>(uid);
                    Assert.That(AIGoals.All.Contains(goal.CurrentGoal), Is.True,
                        $"[{label}] {uid} has an invalid goal \"{goal.CurrentGoal}\" after {elapsedTicks} ticks.");

                    var needs = server.EntMan.GetComponent<NeedsComponent>(uid);
                    Assert.That(needs.Fatigue, Is.InRange(0f, 1f), $"[{label}] {uid} Fatigue out of range at {elapsedTicks} ticks.");
                    Assert.That(needs.Stress, Is.InRange(0f, 1f), $"[{label}] {uid} Stress out of range at {elapsedTicks} ticks.");
                    Assert.That(needs.Safety, Is.InRange(0f, 1f), $"[{label}] {uid} Safety out of range at {elapsedTicks} ticks.");
                    Assert.That(needs.SocialNeed, Is.InRange(0f, 1f), $"[{label}] {uid} SocialNeed out of range at {elapsedTicks} ticks.");
                }
            });
        }

        var after = TakeSnapshot();
        var simSeconds = totalTicks / 30.0;

        TestContext.WriteLine($"## AI Players Soak Report ({label})");
        TestContext.WriteLine($"Population: {aiPlayerCount}");
        TestContext.WriteLine($"Duration: {totalTicks} ticks (~{simSeconds:0}s simulated)");
        TestContext.WriteLine($"Goal reconsiderations: {after.Reconsiderations - before.Reconsiderations:0}");
        TestContext.WriteLine($"Goal changes: {after.GoalChanges - before.GoalChanges:0}");
        TestContext.WriteLine($"Goals stuck: {after.GoalStuck - before.GoalStuck:0}");
        TestContext.WriteLine($"Goals failed (abandoned, no progress): {after.GoalFailed - before.GoalFailed:0}");
        TestContext.WriteLine($"Replans (same-goal): {after.Replanned - before.Replanned:0}");
        TestContext.WriteLine($"Plan interruptions: {after.PlanInterrupted - before.PlanInterrupted:0}");
        TestContext.WriteLine($"Plan resumes: {after.PlanResumed - before.PlanResumed:0}");
        TestContext.WriteLine($"Actions failed: {after.ActionFailed - before.ActionFailed:0}");
        TestContext.WriteLine($"LLM requests: {after.LlmRequests - before.LlmRequests:0}");
        TestContext.WriteLine($"LLM failures: {after.LlmFailures - before.LlmFailures:0}");

        await server.WaitPost(() =>
        {
            foreach (var uid in aiPlayers)
            {
                if (server.EntMan.EntityExists(uid))
                    server.EntMan.DeleteEntity(uid);
            }
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The CI-safe tier: fast (a handful of simulated minutes, seconds of wall-clock) and deterministic
    /// enough to run in every normal test pass, so a structural regression (a crash, an invalid goal, a needs
    /// value escaping [0,1]) gets caught on every commit instead of only during a manual/nightly run.
    /// </summary>
    [Test]
    public async Task Soak_ShortDeterministic_MixedPopulation_RemainsHealthyThroughout()
    {
        await RunSoak(Pair, aiPlayerCount: 8, totalTicks: 900, checkEveryTicks: 150, label: "CI/short");
    }

    /// <summary>
    /// The real spec section 21 ask: a full 30 simulated minutes (54000 ticks at the standard 30 tick/s) with
    /// a larger population. Far too slow for a normal test pass (tens of minutes of wall-clock) - this is the
    /// manual/nightly tier, never run automatically by a normal `dotnet test` pass.
    /// </summary>
    [Test]
    [Explicit("Spec Stabilization milestone stage 21: full 30-minute soak, manual/nightly only - far too slow for a normal CI pass.")]
    public async Task Soak_ThirtyMinutes_Nightly_MixedPopulation_RemainsHealthyThroughout()
    {
        await RunSoak(Pair, aiPlayerCount: 15, totalTicks: 54000, checkEveryTicks: 900, label: "nightly/30min");
    }
}
