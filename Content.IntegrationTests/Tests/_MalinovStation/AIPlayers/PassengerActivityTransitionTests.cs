#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Prototypes;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.6, spec section 17: "not every event should completely erase the previous intention" - an
/// interrupted activity should be resumable, not merely forgotten. Extends
/// <see cref="CognitiveRuntimeIntegrationTests.EmergencyInterruptionLoop"/> (which already proves a fresh
/// decision lands after interruption) with the genuinely new link this milestone adds: the interrupted
/// <see cref="IntentComponent"/> now actually reaches the next Cognitive prompt (<see cref="PromptBuilder"/>'s
/// new "Недавно ты решил(а)" line - previously computed but never rendered, see the plan's audit finding #4),
/// so the LLM can choose to genuinely resume it instead of only ever being handed a blank slate.
/// </summary>
[TestFixture]
public sealed class PassengerActivityTransitionTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";
    private const string Map = "PassengerActivityTransitionTestMap";

    [TestPrototypes]
    private static readonly string PassengerActivityTransitionTestMap = @$"
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

    /// <summary>Applies a GoToKnownLocation("Room A") decision and an attack from <paramref name="attacker"/>,
    /// then waits out AiBusyStateSystem's ~1s scan cycle - the exact interruption setup
    /// EmergencyInterruptionLoop already established, factored out since both tests here build on it.</summary>
    private async Task<EntityCoordinates> SetUpInterruptedTravel(TestPair pair, EntityUid aiPlayer, EntityUid attacker)
    {
        var server = pair.Server;
        EntityCoordinates roomA = default;

        await server.WaitAssertion(() =>
        {
            var spawnCoords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            roomA = spawnCoords.Offset(new Vector2(5, 0));

            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Room A\".", importance: 0.25f, source: "landmark",
                location: roomA, subject: "Room A");

            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "boredom", "исследовать_a", 0.6f, 0.7f, "Мне скучно, посмотрю комнату A.",
                GoToKnownLocationAction.ActionName, new Dictionary<string, string> { ["location"] = "Room A" });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.EqualTo(GoToKnownLocationAction.ActionName));

            server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator = 999f;

            var damageable = server.System<DamageableSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            var damage = new DamageSpecifier(proto.Index(BluntDamageType), FixedPoint2.New(10));
            damageable.TryChangeDamage(aiPlayer, damage, origin: attacker);
        });

        await pair.RunTicksSync(40);
        return roomA;
    }

    [Test]
    public async Task InterruptedIntent_RemainsVisibleInNextPrompt_BeforeAnyNewDecisionIsApplied()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid attacker = default;
        await server.WaitPost(() =>
        {
            var players = server.System<AIPlayerSystem>();
            aiPlayer = players.SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            attacker = players.SpawnAiPlayer(Passenger, station)!.Value;
        });

        await SetUpInterruptedTravel(pair, aiPlayer, attacker);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.Null,
                "Test setup: the attack should have interrupted the travel commitment.");

            // The interrupted intent is still on IntentComponent - nothing here has applied a new decision yet.
            var intent = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.That(intent.Name, Is.EqualTo("исследовать_a"));

            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(aiPlayer);
            Assert.That(state, Is.Not.Null);

            var prompt = PromptBuilder.BuildCognitiveUserPrompt(state!);
            Assert.That(prompt, Does.Contain("исследовать_a"),
                "The interrupted intent should reach the next Cognitive prompt's actual text, so the LLM can " +
                "genuinely choose to resume it rather than only ever seeing a blank slate.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(attacker);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task AfterInterruption_FreshDecisionCanEitherResumeTheOldActivityOrReplaceIt()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid attacker = default;
        await server.WaitPost(() =>
        {
            var players = server.System<AIPlayerSystem>();
            aiPlayer = players.SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            attacker = players.SpawnAiPlayer(Passenger, station)!.Value;
        });

        var roomA = await SetUpInterruptedTravel(pair, aiPlayer, attacker);

        // Resume path: after the interruption, choosing to go back to Room A must work exactly like any other
        // travel decision - nothing should treat the interrupted place as unreachable or special.
        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var resume = new LlmCognitiveDecision(
                "boredom", "вернуться_в_a", 0.5f, 0.6f, "Опасность миновала, вернусь досмотреть комнату A.",
                GoToKnownLocationAction.ActionName, new Dictionary<string, string> { ["location"] = "Room A" });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, resume), Is.True);

            var htn = server.EntMan.GetComponent<HTNComponent>(aiPlayer);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out var stored, server.EntMan), Is.True);
            Assert.That(stored, Is.EqualTo(roomA));
        });

        // Replace path: equally, choosing something entirely different (e.g. actually fleeing) must also work -
        // resuming is a real option, never the only one.
        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var replace = new LlmCognitiveDecision(
                "safety", "бежать", 0.9f, 0.9f, "На меня напали, лучше уйти в безопасное место.",
                PursueGoalAction.ActionName, new Dictionary<string, string> { ["goal"] = AIGoals.Flee });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, replace), Is.True);

            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            Assert.That(goal.CurrentGoal, Is.EqualTo(AIGoals.Flee));

            var intent = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.That(intent.Name, Is.EqualTo("бежать"),
                "The new decision should fully replace the old intent, proving replacement works just as well as resumption.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(attacker);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
