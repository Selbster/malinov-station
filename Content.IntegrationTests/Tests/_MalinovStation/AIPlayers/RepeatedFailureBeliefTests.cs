#nullable enable
using System.Linq;
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
/// AI Players 0.4 Milestone 12: repeated failure of the exact same action+reason combination becomes a Belief
/// (spec's "Kitchen inaccessible -&gt; Kitchen may not be a reliable food source" example), not just another
/// one-off outcome memory each time. Extends the existing ActionFailed -&gt; outcome-memory-plus-fast-reflection
/// pipeline (already covered by <see cref="CognitiveActionLoopTests"/>) rather than replacing it - every
/// ActionFailed call still writes its own outcome memory and forces fast reflection exactly as before.
/// </summary>
[TestFixture]
public sealed class RepeatedFailureBeliefTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "RepeatedFailureBeliefTestMap";

    [TestPrototypes]
    private static readonly string RepeatedFailureBeliefTestMap = @$"
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

    [Test]
    public async Task ActionFailed_SameActionAndReasonThreeTimesInARow_WritesABelief()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var trace = server.System<AiTraceSystem>();
            for (var i = 0; i < AiTraceSystem.ConsecutiveFailuresBeforeBelief - 1; i++)
                trace.ActionFailed(aiPlayer, "SearchArea", "Kitchen inaccessible");

            var beliefsBefore = server.EntMan.GetComponent<BeliefComponent>(aiPlayer).Beliefs;
            Assert.That(beliefsBefore.Any(b => b.Source == "repeated-failure"), Is.False,
                "Test setup: shouldn't have crossed the threshold yet.");

            trace.ActionFailed(aiPlayer, "SearchArea", "Kitchen inaccessible");

            var beliefs = server.EntMan.GetComponent<BeliefComponent>(aiPlayer).Beliefs;
            var repeated = beliefs.FirstOrDefault(b => b.Source == "repeated-failure");
            Assert.That(repeated, Is.Not.Null, "Crossing the threshold should write a repeated-failure belief.");
            Assert.That(repeated!.Subject, Is.EqualTo("SearchArea"));

            // Every individual ActionFailed call should still have written its own outcome memory too - this
            // is additive, not a replacement for the existing feedback loop.
            var memories = server.EntMan.GetComponent<MemoryComponent>(aiPlayer).Memories;
            Assert.That(memories.Count(m => m.Source == "outcome"), Is.EqualTo(AiTraceSystem.ConsecutiveFailuresBeforeBelief));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task ActionFailed_DifferentReasonsEachTime_NeverCrossesTheThreshold()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var trace = server.System<AiTraceSystem>();
            for (var i = 0; i < AiTraceSystem.ConsecutiveFailuresBeforeBelief + 2; i++)
                trace.ActionFailed(aiPlayer, "SearchArea", $"reason number {i}");

            var beliefs = server.EntMan.GetComponent<BeliefComponent>(aiPlayer).Beliefs;
            Assert.That(beliefs.Any(b => b.Source == "repeated-failure"), Is.False,
                "Different failure reasons each time shouldn't accumulate toward the same repeated-failure belief.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
