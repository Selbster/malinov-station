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
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.6, spec section 4: boredom is a deterministic *signal* (NeedsSystem.BoredomDelta), not a
/// "boredom &gt; X -&gt; random room" rule - what it does with that signal (GoalSystem.ComputeCandidates
/// surfacing a "Restlessness" desire) is covered by <see cref="PassengerExplorationTests"/>. This file proves
/// the signal itself: it grows while the current intent/area is stale, resets while either is fresh, is scaled
/// by <see cref="PersonalityComponent.Curiosity"/>, and never grows at all for a legacy (non-cognitive) AI
/// player, mirroring <see cref="CognitiveModeGatingTests"/>' precedent for proving a side effect is provably
/// inert rather than merely unused.
/// </summary>
[TestFixture]
public sealed class PassengerBoredomTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string Map = "PassengerBoredomTestMap";

    [TestPrototypes]
    private static readonly string PassengerBoredomTestMap = @$"
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

    /// <summary>Backdates IntentComponent.ChosenAt/LandmarkPerceptionComponent.AreaEnteredAt past
    /// NeedsComponent.BoredomGraceSeconds, so boredom growth is no longer gated on "just changed" freshness -
    /// isolates NeedsSystem's own growth formula from LandmarkPerceptionSystem's real scan cadence.</summary>
    private void MakeIntentAndAreaStale(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        var timing = server.ResolveDependency<IGameTiming>();
        var stale = timing.CurTime - TimeSpan.FromSeconds(60);

        server.EntMan.GetComponent<IntentComponent>(uid).ChosenAt = stale;
        server.EntMan.GetComponent<LandmarkPerceptionComponent>(uid).AreaEnteredAt = stale;
    }

    [Test]
    public async Task Boredom_GrowsWhileIntentAndAreaStaySame_ScaledByCuriosity()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            server.EntMan.GetComponent<PersonalityComponent>(aiPlayer).Curiosity = 1f;
        });
        MakeIntentAndAreaStale(pair, aiPlayer);

        var before = server.EntMan.GetComponent<NeedsComponent>(aiPlayer).Boredom;
        await pair.RunTicksSync(300);

        await server.WaitAssertion(() =>
        {
            var after = server.EntMan.GetComponent<NeedsComponent>(aiPlayer).Boredom;
            Assert.That(after, Is.GreaterThan(before),
                "Boredom should grow while the current intent and area have both been unchanged well past the grace period.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task Boredom_ResetsWhenAreaChanges_EvenWithHighExistingBoredom()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            server.EntMan.GetComponent<NeedsComponent>(aiPlayer).Boredom = 0.8f;

            // A fresh area transition, same shape LandmarkPerceptionSystem.UpdateCurrentArea itself writes on a
            // genuine arrival - isolates NeedsSystem's reaction to "something just changed" from the scan itself.
            var timing = server.ResolveDependency<IGameTiming>();
            var landmark = server.EntMan.GetComponent<LandmarkPerceptionComponent>(aiPlayer);
            landmark.CurrentAreaLabel = "Somewhere New";
            landmark.AreaEnteredAt = timing.CurTime;
        });

        await pair.RunTicksSync(120);

        await server.WaitAssertion(() =>
        {
            var boredom = server.EntMan.GetComponent<NeedsComponent>(aiPlayer).Boredom;
            Assert.That(boredom, Is.LessThan(0.8f),
                "Boredom should drain back down once the current area is fresh again, regardless of how high it had climbed.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task Boredom_NeverGrowsForLegacyAiPlayer()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value; // legacy, no cognitiveMode
        });

        await pair.RunTicksSync(300);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.HasComponent<IntentComponent>(aiPlayer), Is.False,
                    "Test setup: a legacy AI player should never receive IntentComponent.");
                Assert.That(server.EntMan.HasComponent<LandmarkPerceptionComponent>(aiPlayer), Is.False,
                    "Test setup: a legacy AI player should never receive LandmarkPerceptionComponent.");
                Assert.That(server.EntMan.GetComponent<NeedsComponent>(aiPlayer).Boredom, Is.EqualTo(0f),
                    "Boredom should stay inert for a legacy AI player, with no explicit CognitiveModeComponent check needed.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
