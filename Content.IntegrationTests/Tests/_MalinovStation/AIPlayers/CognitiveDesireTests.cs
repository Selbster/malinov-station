#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Prototypes;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 2.0 Milestone 1 spec section 31's core claim: hunger/fatigue-vs-work tradeoffs should reflect
/// personality/context, not a fixed formula picking a single winner and discarding everything else it
/// considered. <see cref="DesireComponent"/> materializes the same candidate scoring
/// <see cref="GoalSystem.ComputeCandidates"/> already computes for every AI player - these tests prove that
/// materialization is real (multiple live desires visible at once, not just the winner) and that it responds
/// to personality exactly like the underlying formulas always have.
/// </summary>
[TestFixture]
public sealed class CognitiveDesireTests : GameTest
{
    private static readonly ProtoId<JobPrototype> StationEngineer = "StationEngineer";
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";

    private const string Map = "CognitiveDesireTestMap";
    private const string TestMachine = "CognitiveDesireTestRepairableMachine";

    [TestPrototypes]
    private static readonly string CognitiveDesireTestMap = @$"
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
            {StationEngineer}: [ -1, -1 ]

- type: entity
  id: {TestMachine}
  name: test machine
  components:
  - type: Damageable
    damageModifierSet: Metallic
  - type: Injurable
    damageContainer: Inorganic
  - type: Repairable
    qualityNeeded: Welding
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
    /// Spawns a cognitive-mode engineer, co-located with a freshly-damaged test machine, and fatigued enough
    /// that Rest is also eligible (FatiguePrecondition's 0.75 threshold) - the same dual-eligible-work-vs-rest
    /// situation GoalIntentUnificationTests.Personality_* already exercises for the legacy path.
    /// </summary>
    private async Task<EntityUid> SpawnFatiguedEngineerWithDamagedMachine(TestPair pair, EntityUid station, float professionalism, float laziness)
    {
        var server = pair.Server;
        EntityUid aiPlayer = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(StationEngineer, station, cognitiveMode: true)!.Value;

            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            var machine = server.EntMan.SpawnEntity(TestMachine, coords);

            var damage = new DamageSpecifier(server.ProtoMan.Index(BluntDamageType), FixedPoint2.New(20));
            server.System<DamageableSystem>().TryChangeDamage(machine, damage, ignoreResistances: true);

            server.EntMan.GetComponent<RepairOpportunityComponent>(aiPlayer).ScanAccumulator = 0f;

            var personality = server.EntMan.GetComponent<PersonalityComponent>(aiPlayer);
            personality.Professionalism = professionalism;
            personality.Laziness = laziness;

            server.EntMan.GetComponent<NeedsComponent>(aiPlayer).Fatigue = 0.8f;
        });

        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            server.EntMan.GetComponent<GoalComponent>(aiPlayer).ReconsiderAccumulator = 0f;
        });
        await pair.RunTicksSync(2);

        return aiPlayer;
    }

    /// <summary>
    /// A cognitive AI player facing a work-vs-rest tradeoff should have BOTH RepairMachine and Rest as live,
    /// inspectable desires at once - proving the plural candidate list is materialized, not just the single
    /// winner GoalComponent has always exposed.
    /// </summary>
    [Test]
    public async Task DesireComponent_ContainsBothWorkAndRestCandidates_NotJustTheWinner()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var aiPlayer = await SpawnFatiguedEngineerWithDamagedMachine(pair, station, professionalism: 1f, laziness: 0f);

        await server.WaitAssertion(() =>
        {
            var desire = server.EntMan.GetComponent<DesireComponent>(aiPlayer);
            var names = desire.Current.Select(d => d.Name).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(names, Does.Contain(ProfessionalGoals.RepairMachine),
                    "The engineer should still want to repair the damaged machine.");
                Assert.That(names, Does.Contain(AIGoals.Rest),
                    "The engineer should also want to rest, since Fatigue is above the Rest threshold - both should be visible at once.");
                Assert.That(desire.Current.Count, Is.GreaterThan(1),
                    "A cognitive AI player facing a real tradeoff should have more than one live desire.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Two engineers in the identical situation, differing only in Professionalism/Laziness, must end up with
    /// different chosen Intent - the same personality-dependent divergence GoalIntentUnificationTests.
    /// Personality_ProfessionalismAndLaziness_ChangeWhichGoalWinsInTheSameSituation already proves for
    /// GoalComponent, now also proven for the richer IntentComponent overlay.
    /// </summary>
    [Test]
    public async Task IntentComponent_DivergesByPersonality_InTheSameSituation()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var professional = await SpawnFatiguedEngineerWithDamagedMachine(pair, station, professionalism: 1f, laziness: 0f);
        var lazy = await SpawnFatiguedEngineerWithDamagedMachine(pair, station, professionalism: 0f, laziness: 1f);

        await server.WaitAssertion(() =>
        {
            var professionalIntent = server.EntMan.GetComponent<IntentComponent>(professional);
            var lazyIntent = server.EntMan.GetComponent<IntentComponent>(lazy);

            Assert.Multiple(() =>
            {
                Assert.That(professionalIntent.Name, Is.EqualTo(ProfessionalGoals.RepairMachine),
                    "High-professionalism, low-laziness engineer should intend to work.");
                Assert.That(lazyIntent.Name, Is.EqualTo(AIGoals.Rest),
                    "Low-professionalism, high-laziness engineer should intend to rest, in the exact same situation.");
                Assert.That(professionalIntent.Name, Is.Not.EqualTo(lazyIntent.Name));
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(professional);
            server.EntMan.DeleteEntity(lazy);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
