#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// Milestone 7 acceptance checks: being attacked raises Safety, records a memory, and damages the
/// relationship with the attacker (relationships finally have a reason to move negative); seeing another
/// character incapacitated nearby drives the HelpInjured goal.
/// </summary>
[TestFixture]
public sealed class DangerSystemTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<DamageTypePrototype> Blunt = "Blunt";

    private const string Map = "AIPlayerDangerTestMap";

    [TestPrototypes]
    private static readonly string AIPlayerDangerTestMap = @$"
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
    /// Attacking an AI player should raise Safety, record a "danger" memory naming the attacker, damage the
    /// relationship toward them, set an active threat, and force an immediate goal reconsideration (clearing
    /// any stale LLM override) rather than waiting for the routine cooldown.
    /// </summary>
    [Test]
    public async Task OnDamaged_RaisesSafety_RecordsMemory_AndDamagesRelationship()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid victim = default;
        EntityUid attacker = default;

        await server.WaitPost(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            victim = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;
            attacker = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;

            var goal = server.EntMan.GetComponent<GoalComponent>(victim);
            goal.IsLlmOverride = true;
            goal.LlmOverrideExpiresAt = server.Timing.CurTime + TimeSpan.FromMinutes(5);
            goal.ReconsiderAccumulator = 999f;

            var damageable = server.System<DamageableSystem>();
            var damageType = server.ProtoMan.Index(Blunt);
            var damage = new DamageSpecifier(damageType, FixedPoint2.New(10));
            damageable.TryChangeDamage(victim, damage, origin: attacker);
        });

        await server.WaitAssertion(() =>
        {
            var needs = server.EntMan.GetComponent<NeedsComponent>(victim);
            Assert.That(needs.Safety, Is.GreaterThan(0f));

            var memory = server.EntMan.GetComponent<MemoryComponent>(victim);
            Assert.That(memory.Memories.Any(m => m.Source == "danger" && m.Participants.Contains(attacker)), Is.True);

            var relationships = server.EntMan.GetComponent<RelationshipComponent>(victim);
            Assert.That(relationships.Relationships.TryGetValue(attacker, out var data), Is.True);
            Assert.Multiple(() =>
            {
                Assert.That(data!.Fear, Is.GreaterThan(0f));
                Assert.That(data.Trust, Is.LessThan(0f));
            });

            var danger = server.EntMan.GetComponent<DangerComponent>(victim);
            Assert.That(danger.ThreatSource, Is.EqualTo(attacker));

            var goal = server.EntMan.GetComponent<GoalComponent>(victim);
            Assert.Multiple(() =>
            {
                Assert.That(goal.IsLlmOverride, Is.False, "Being attacked should clear a stale LLM override.");
                Assert.That(goal.ReconsiderAccumulator, Is.EqualTo(0f), "Should react immediately, not on the routine cooldown.");
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(victim);
            server.EntMan.DeleteEntity(attacker);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Once a threat is active, the Goal System should pick Flee over routine goals.
    /// </summary>
    [Test]
    public async Task GoalSystem_PicksFlee_WhenThreatened()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid victim = default;
        EntityUid attacker = default;

        await server.WaitPost(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            victim = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;
            attacker = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;

            var danger = server.EntMan.GetComponent<DangerComponent>(victim);
            danger.ThreatSource = attacker;
            danger.ThreatExpiresAt = server.Timing.CurTime + TimeSpan.FromSeconds(15);

            var goal = server.EntMan.GetComponent<GoalComponent>(victim);
            goal.ReconsiderAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var goal = server.EntMan.GetComponent<GoalComponent>(victim);
            Assert.That(goal.CurrentGoal, Is.EqualTo(AIGoals.Flee));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(victim);
            server.EntMan.DeleteEntity(attacker);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Seeing another character incapacitated nearby should be detected by DangerSystem's perception scan
    /// and drive the HelpInjured goal.
    /// </summary>
    [Test]
    public async Task DangerSystem_DetectsIncapacitatedCharacter_AndGoalSystemPicksHelpInjured()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid helper = default;
        EntityUid injured = default;

        await server.WaitPost(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            helper = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;
            injured = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;

            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            var helperCoords = server.EntMan.GetComponent<TransformComponent>(helper).Coordinates;
            xformSystem.SetCoordinates(injured, helperCoords);

            // Force the state directly rather than relying on exact damage/threshold numbers - this test is
            // about Danger's detection of an incapacitated character, not about combat balance.
            var mobState = server.System<MobStateSystem>();
            mobState.ChangeMobState(injured, MobState.Critical);

            server.EntMan.GetComponent<PerceptionComponent>(helper).PerceiveAccumulator = 0f;
        });

        // Let Perception populate LastObservation first - system update order within a single tick isn't
        // guaranteed, so forcing Perception's and Danger's accumulators to 0 in the same tick would race.
        await pair.RunTicksSync(3);

        await server.WaitPost(() =>
        {
            server.EntMan.GetComponent<DangerComponent>(helper).ScanAccumulator = 0f;
        });

        await pair.RunTicksSync(3);

        await server.WaitPost(() =>
        {
            server.EntMan.GetComponent<GoalComponent>(helper).ReconsiderAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var danger = server.EntMan.GetComponent<DangerComponent>(helper);
            Assert.That(danger.NearbyInjured, Is.EqualTo(injured));

            var goal = server.EntMan.GetComponent<GoalComponent>(helper);
            Assert.That(goal.CurrentGoal, Is.EqualTo(AIGoals.HelpInjured));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(helper);
            server.EntMan.DeleteEntity(injured);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
