#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
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
/// AI Players 0.4 Milestones 5-6: <see cref="AiBusyStateComponent"/> gets populated by
/// <see cref="AiActionRegistrySystem.TryDoAction"/> for an <see cref="IAiAction.IsExtended"/> action, and
/// <see cref="AiBusyStateSystem"/> clears it once the underlying commitment (GoalComponent.IsLlmOverride for
/// PursueGoal, the HTN blackboard's ForcedDestination key for GoToKnownLocation) actually resolves - or, as a
/// backstop, once <see cref="AiBusyStateComponent.MaxBusyDurationSeconds"/> elapses regardless. Interruption
/// (Milestone 6) forces a fast cognitive re-reflection when a real danger event fires while busy, mirroring
/// <see cref="DangerSystemTests"/>'s own attack-simulation pattern.
/// </summary>
[TestFixture]
public sealed class AiBusyStateTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    private const string Map = "AiBusyStateTestMap";

    [TestPrototypes]
    private static readonly string AiBusyStateTestMap = @$"
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
    public async Task TryDoAction_PursueGoal_SetsBusyStateWithReason()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var registry = server.System<AiActionRegistrySystem>();
            var ok = registry.TryDoAction(aiPlayer, PursueGoalAction.ActionName,
                new PursueGoalActionParams("Rest", 0.5f, "tired"), "need to recover", out var reason);
            Assert.That(ok, Is.True, reason);

            var busy = server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(busy.CurrentAction, Is.EqualTo(PursueGoalAction.ActionName));
                Assert.That(busy.Reason, Is.EqualTo("need to recover"));
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task TryDoAction_GoToKnownLocation_SetsBusyState()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var destination = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;

            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Kitchen\".", importance: 0.25f, source: "landmark",
                location: destination, subject: "Kitchen");

            var registry = server.System<AiActionRegistrySystem>();
            var ok = registry.TryDoAction(aiPlayer, GoToKnownLocationAction.ActionName,
                new GoToKnownLocationActionParams("Kitchen"), "grabbing a snack", out var reason);
            Assert.That(ok, Is.True, reason);

            var busy = server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer);
            Assert.That(busy.CurrentAction, Is.EqualTo(GoToKnownLocationAction.ActionName));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task AiBusyStateSystem_PursueGoalBusy_ClearsOnceOverrideNoLongerActive()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var registry = server.System<AiActionRegistrySystem>();
            registry.TryDoAction(aiPlayer, PursueGoalAction.ActionName,
                new PursueGoalActionParams("Rest", 0.5f, "tired"), "resting up", out _);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.EqualTo(PursueGoalAction.ActionName));
            // Simulate the override having naturally expired/been cleared, same as GoalSystem.Reconsider or
            // DangerSystem would do on its own.
            server.EntMan.GetComponent<GoalComponent>(aiPlayer).IsLlmOverride = false;
        });

        // AiBusyStateSystem scans on its own ~1s (LOD-scaled) cadence, not per-tick - give it enough real ticks.
        await pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.Null,
                "Busy state should clear once IsLlmOverride is no longer active.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task AiBusyStateSystem_GoToKnownLocationBusy_ClearsOnceForcedDestinationKeyIsGone()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var destination = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;

            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Kitchen\".", importance: 0.25f, source: "landmark",
                location: destination, subject: "Kitchen");

            server.System<AiActionRegistrySystem>().TryDoAction(aiPlayer, GoToKnownLocationAction.ActionName,
                new GoToKnownLocationActionParams("Kitchen"), "grabbing a snack", out _);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.EqualTo(GoToKnownLocationAction.ActionName));
            // Simulate arrival, same as MoveToOperator's own RemoveKeyOnFinish would do.
            server.EntMan.GetComponent<HTNComponent>(aiPlayer).Blackboard.Remove<EntityCoordinates>(MoveToAction.ForcedDestinationKey);
        });

        await pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.Null,
                "Busy state should clear once the ForcedDestination key is gone (arrival or cancellation).");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task AiBusyStateSystem_HardTimeout_ForceClearsRegardlessOfUnderlyingSignal()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        TimeSpan before = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            server.System<AiActionRegistrySystem>().TryDoAction(aiPlayer, PursueGoalAction.ActionName,
                new PursueGoalActionParams("Rest", 0.5f, "tired"), "resting up", out _);

            // IsLlmOverride is still true here (never cleared) - only the hard timeout should end this.
            server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).MaxBusyDurationSeconds = 0.01f;
            before = server.Timing.CurTime;
        });

        await pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            var elapsed = server.Timing.CurTime - before;
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.Null,
                $"The hard timeout backstop should force-clear busy state even if the natural signal never resolves. elapsed={elapsed.TotalSeconds}s");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task AiBusyStateSystem_AttackWhileBusy_ForcesFastReflectionWithoutWaitingForRoutineCooldown()
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

            server.System<AiActionRegistrySystem>().TryDoAction(aiPlayer, PursueGoalAction.ActionName,
                new PursueGoalActionParams("Rest", 0.5f, "tired"), "resting up", out _);

            // Far from ever firing on its own routine cadence - only the interruption should reset this.
            server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator = 999f;
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.EqualTo(PursueGoalAction.ActionName));

            var damageable = server.System<DamageableSystem>();
            var damageType = server.ProtoMan.Index(Blunt);
            var damage = new DamageSpecifier(damageType, FixedPoint2.New(10));
            damageable.TryChangeDamage(aiPlayer, damage, origin: attacker);
        });

        // AiBusyStateSystem's scan accumulator is shared across all busy entities (not one per entity), so a
        // newly-busy entity may have to wait out whatever's left of the current ~1s cycle before its first
        // check - 40 ticks (~1.3s at normal tick rate) safely covers a full cycle either way. It's also short
        // enough that ReflectionAccumulator could not plausibly decay from 999 anywhere near 0 on its own
        // routine 45s cadence - LlmGatewaySystem.UpdateReflectionTriggers resets it back up to
        // ReflectionCooldown (45s) the instant it also observes a value that low, so checking for exactly 0f
        // here would be racing that reset rather than proving anything; "no longer anywhere near the untouched
        // 999 baseline" is the stable signal.
        await pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator, Is.LessThanOrEqualTo(45f),
                    "Being attacked while busy should force an immediate cognitive re-reflection, not wait out the routine interval.");
                Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.Null,
                    "DangerSystem already clears IsLlmOverride on attack, so busy state should also clear once AiBusyStateSystem notices.");
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(attacker);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
