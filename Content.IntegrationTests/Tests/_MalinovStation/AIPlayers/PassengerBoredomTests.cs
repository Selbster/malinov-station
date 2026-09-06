#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
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

    /// <summary>
    /// AI Players 0.6.1: <see cref="LlmGatewaySystem.TryApplyCognitiveDecision"/> and
    /// <see cref="LlmGatewaySystem.HandleCompletedIntentRequest"/> used to unconditionally re-stamp
    /// <see cref="IntentComponent.ChosenAt"/> on every successful cognitive reflection, even one that just
    /// reconfirmed the exact same ongoing intent - since that timestamp is what <see cref="NeedsSystem"/>
    /// reads as "how long has the AI been doing the same thing", boredom's clock kept getting wiped before it
    /// could ever meaningfully compound across reflection cycles. This proves the fix directly: reconfirming
    /// the same intent name leaves the timestamp untouched, a genuinely different one still resets it.
    /// </summary>
    [Test]
    public async Task ReconfirmedIntent_DoesNotResetChosenAt_ButAGenuinelyNewIntentDoes()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
        });

        var gateway = server.System<LlmGatewaySystem>();
        var timing = server.ResolveDependency<IGameTiming>();

        var firstDecision = new LlmCognitiveDecision(
            "boredom", "заняться_своими_делами", 0.4f, 0.6f, "Просто продолжаю заниматься своими делами.",
            ContinueActivityAction.ActionName, new Dictionary<string, string>());

        TimeSpan chosenAtAfterFirst = default;
        await server.WaitPost(() =>
        {
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, firstDecision), Is.True);
            chosenAtAfterFirst = server.EntMan.GetComponent<IntentComponent>(aiPlayer).ChosenAt;
        });

        await pair.RunTicksSync(30);

        // Same intention text again - a reconfirmation, not a new decision.
        await server.WaitPost(() => Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, firstDecision), Is.True));

        await server.WaitAssertion(() =>
        {
            var chosenAtAfterReconfirm = server.EntMan.GetComponent<IntentComponent>(aiPlayer).ChosenAt;
            Assert.That(chosenAtAfterReconfirm, Is.EqualTo(chosenAtAfterFirst),
                "Reconfirming the same intent name should not reset ChosenAt - boredom's freshness signal must survive it.");
        });

        await pair.RunTicksSync(30);

        var secondDecision = new LlmCognitiveDecision(
            "boredom", "исследовать_станцию", 0.6f, 0.7f, "Хочу посмотреть, что там дальше.",
            ContinueActivityAction.ActionName, new Dictionary<string, string>());

        await server.WaitPost(() => Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, secondDecision), Is.True));

        await server.WaitAssertion(() =>
        {
            var chosenAtAfterNewIntent = server.EntMan.GetComponent<IntentComponent>(aiPlayer).ChosenAt;
            Assert.That(chosenAtAfterNewIntent, Is.GreaterThan(chosenAtAfterFirst),
                "A genuinely different intent name should still reset ChosenAt.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// End-to-end version of the above: with the reconfirmation-doesn't-reset fix in place, boredom should
    /// actually keep climbing across two full same-intent reflection cycles instead of collapsing back toward
    /// 0 every ~<see cref="CognitiveModeComponent.ReflectionCooldown"/> seconds the way it did before.
    /// </summary>
    [Test]
    public async Task Boredom_KeepsGrowingAcrossRepeatedSameIntentReflections()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var decision = new LlmCognitiveDecision(
            "boredom", "заняться_своими_делами", 0.4f, 0.6f, "Просто продолжаю заниматься своими делами.",
            ContinueActivityAction.ActionName, new Dictionary<string, string>());

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            server.EntMan.GetComponent<PersonalityComponent>(aiPlayer).Curiosity = 1f;
            // Pre-set to the same name the mid-test decision below reconfirms, so that later call is a genuine
            // reconfirmation (no name change) rather than the first-ever intent this AI has held.
            server.EntMan.GetComponent<IntentComponent>(aiPlayer).Name = decision.Intention;
        });
        MakeIntentAndAreaStale(pair, aiPlayer);

        var gateway = server.System<LlmGatewaySystem>();

        var before = server.EntMan.GetComponent<NeedsComponent>(aiPlayer).Boredom;
        await pair.RunTicksSync(150);

        // Reconfirm the same intent partway through - simulating a routine reflection that changed nothing.
        await server.WaitPost(() => Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True));
        await pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            var after = server.EntMan.GetComponent<NeedsComponent>(aiPlayer).Boredom;
            Assert.That(after, Is.GreaterThan(before),
                "Boredom should keep growing across a reconfirmed reflection, not collapse back toward 0 each cycle.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// AI Players 0.6.3: the case the 0.6.1 reconfirmation fix could not cover. A real model rarely repeats an
    /// intent verbatim - it re-words the same ongoing activity every reflection - so the name-changed check
    /// stamped a fresh timestamp anyway, roughly every reflection cycle, and the drain (fifty times the gain)
    /// erased everything boredom had accumulated. Live dumps showed the result: скука=0.00 on every passenger.
    ///
    /// An AI that has not left the corridor it is standing in is bored, regardless of how eloquently it keeps
    /// describing what it is doing there. Deliberately started from a non-zero value so a regression shows up
    /// as an actual decline rather than being hidden by the clamp at 0.
    /// </summary>
    [Test]
    public async Task Boredom_SurvivesReflectionsThatRewordTheIntent()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            server.EntMan.GetComponent<PersonalityComponent>(aiPlayer).Curiosity = 1f;
            server.EntMan.GetComponent<NeedsComponent>(aiPlayer).Boredom = 0.5f;
        });
        MakeIntentAndAreaStale(pair, aiPlayer);

        var gateway = server.System<LlmGatewaySystem>();
        var before = server.EntMan.GetComponent<NeedsComponent>(aiPlayer).Boredom;

        // Three reflections, each phrasing the very same ongoing activity differently - which is what a live
        // model actually produces, and what used to reset the clock every single time.
        foreach (var wording in new[] { "осматриваюсь_вокруг", "разглядываю_коридор", "продолжаю_осмотр" })
        {
            await pair.RunTicksSync(150);

            var decision = new LlmCognitiveDecision(
                "boredom", wording, 0.4f, 0.6f, "Всё ещё стою здесь.",
                ContinueActivityAction.ActionName, new Dictionary<string, string>());

            await server.WaitPost(() => Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True));
        }

        await pair.RunTicksSync(150);

        await server.WaitAssertion(() =>
        {
            var after = server.EntMan.GetComponent<NeedsComponent>(aiPlayer).Boredom;
            Assert.That(after, Is.GreaterThan(before),
                "Re-wording the same activity is not doing something new, so it must not relieve boredom.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
