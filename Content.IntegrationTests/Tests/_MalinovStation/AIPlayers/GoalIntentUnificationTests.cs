#nullable enable
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Prototypes;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.Atmos.EntitySystems;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Shared.Atmos;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// Milestone 1 acceptance checks: proves the goal/intent pipeline is actually unified end to end, not just at
/// the GoalComponent level. Two real, previously-undetected bugs surfaced while writing these: Flee and
/// HelpInjured never actually executed at all (PickFleeDestinationOperator/PickInjuredTargetOperator were
/// missing a Plan() override, so MoveToOperator's own Plan() silently rejected the branch during every
/// planning pass - the same bug class Milestone 11 found for repair), and an LLM goal decision had no effect
/// on real HTN-driven behaviour outside Socialize. Both are fixed in the systems these tests exercise.
/// </summary>
[TestFixture]
public sealed class GoalIntentUnificationTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<JobPrototype> StationEngineer = "StationEngineer";
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";

    private const string Map = "AIPlayerGoalIntentTestMap";
    private const string TestMachine = "TestGoalIntentRepairableMachine";

    [TestPrototypes]
    private static readonly string AIPlayerGoalIntentTestMap = @$"
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

    private static async Task WaitForCondition(TestPair pair, Func<bool> condition, int maxIterations = 100, int ticksPerIteration = 5)
    {
        for (var i = 0; i < maxIterations && !condition(); i++)
            await pair.RunTicksSync(ticksPerIteration);
    }

    /// <summary>
    /// Spawns an AI player with the given job, co-located with a freshly-damaged test machine. Mirrors
    /// ProfessionalGoalsTests' helper of the same shape/purpose.
    /// </summary>
    private async Task<(EntityUid AiPlayer, EntityUid Machine)> SpawnAiWithDamagedMachine(TestPair pair, EntityUid station, ProtoId<JobPrototype> job)
    {
        var server = pair.Server;
        EntityUid aiPlayer = default;
        EntityUid machine = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(job, station)!.Value;

            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            machine = server.EntMan.SpawnEntity(TestMachine, coords);

            var proto = server.ResolveDependency<IPrototypeManager>();
            var damage = new DamageSpecifier(proto.Index(BluntDamageType), FixedPoint2.New(20));
            server.System<DamageableSystem>().TryChangeDamage(machine, damage, ignoreResistances: true);

            server.EntMan.GetComponent<RepairOpportunityComponent>(aiPlayer).ScanAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitPost(() =>
        {
            server.EntMan.GetComponent<GoalComponent>(aiPlayer).ReconsiderAccumulator = 0f;
        });

        return (aiPlayer, machine);
    }

    private async Task GiveWelder(TestPair pair, EntityUid aiPlayer)
    {
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            var hands = server.System<HandsSystem>();
            var welder = server.EntMan.SpawnEntity("Welder", server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates);
            Assert.That(hands.TryPickup(aiPlayer, welder), Is.True, "Test setup: AI player should be able to pick up the welder.");
        });
    }

    /// <summary>
    /// Empties (and deletes) the contents of the AI's belt, same as ProfessionalGoalsTests' helper - needed
    /// because StationEngineer's real starting belt comes pre-filled with a welder.
    /// </summary>
    private async Task StripBeltContents(TestPair pair, EntityUid aiPlayer)
    {
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            var inventory = server.System<InventorySystem>();
            if (!inventory.TryGetSlotEntity(aiPlayer, "belt", out var belt) ||
                !server.EntMan.TryGetComponent<StorageComponent>(belt.Value, out var storage))
            {
                return;
            }

            var container = server.System<SharedContainerSystem>();
            foreach (var stored in storage.Container.ContainedEntities.ToList())
            {
                container.Remove(stored, storage.Container);
                server.EntMan.DeleteEntity(stored);
            }
        });
    }

    private float GetTotalDamage(TestPair pair, EntityUid machine)
    {
        var server = pair.Server;
        var damageable = server.EntMan.GetComponent<Content.Shared.Damage.Components.DamageableComponent>(machine);
        return (float)server.System<DamageableSystem>().GetTotalDamage((machine, damageable));
    }

    /// <summary>
    /// Regression test for a real bug this milestone found: PickFleeDestinationOperator only implemented
    /// Update(), not Plan(), so MoveToOperator's own Plan() (which unconditionally requires TargetCoordinates
    /// during the planning pass) silently rejected FleeCompound on every single planning attempt - the AI
    /// never even tried to flee. The prior test (DangerSystemTests.GoalSystem_PicksFlee_WhenThreatened) only
    /// checked GoalComponent and would never have caught this.
    /// Deliberately does not assert the AI physically covers the full configured flee distance: whether 8
    /// unobstructed units exist away from a given attacker depends on this test map's specific layout, which
    /// this regression isn't about (spec Milestone 1's real, 30-minute gameplay test is what validates actual
    /// in-round flee movement on a real station). What this proves instead: HTN ends up in a well-defined
    /// state (a real plan, not stuck/crashed) - before the fix, MoveToOperator.Plan() would unconditionally
    /// reject FleeCompound, and since Flee also gates Rest/HelpInjured/RepairMachine via CurrentGoalPrecondition,
    /// a threatened AI could end up with no eligible branch at all.
    /// </summary>
    [Test]
    public async Task Flee_FromAttacker_PicksFleeGoalAndProducesAValidPlan()
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

            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            var victimCoords = server.EntMan.GetComponent<TransformComponent>(victim).Coordinates;
            xformSystem.SetCoordinates(attacker, victimCoords);

            var damageable = server.System<DamageableSystem>();
            var damageType = server.ProtoMan.Index(BluntDamageType);
            var damage = new DamageSpecifier(damageType, FixedPoint2.New(10));
            damageable.TryChangeDamage(victim, damage, origin: attacker);
        });

        // OnDamaged already forces an immediate goal reconsideration; give HTN planning real time to settle.
        await WaitForCondition(pair, () => server.EntMan.GetComponent<GoalComponent>(victim).CurrentGoal == AIGoals.Flee);

        // HTNPlanJob runs asynchronously (a time-sliced job queue) - Plan can be transiently null for a few
        // ticks right after CurrentGoal changes invalidates the old plan and a new planning pass is queued but
        // hasn't completed yet. Give it a little more time before checking the settled result.
        await WaitForCondition(pair, () => server.EntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(victim).Plan != null, maxIterations: 20);

        await server.WaitAssertion(() =>
        {
            var goal = server.EntMan.GetComponent<GoalComponent>(victim);
            Assert.That(goal.CurrentGoal, Is.EqualTo(AIGoals.Flee), "Being attacked should make Flee the current intent.");

            var htn = server.EntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(victim);
            Assert.That(htn.Plan, Is.Not.Null,
                "HTN should have a real plan (Flee itself, or a graceful fallback if no reachable flee point " +
                "exists on this test map) - before this milestone's fix, MoveToOperator.Plan() unconditionally " +
                "rejected FleeCompound, and since Flee gates every other branch via CurrentGoalPrecondition " +
                "while it's the active goal, a threatened AI could end up with no valid plan at all.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(victim);
            server.EntMan.DeleteEntity(attacker);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Same bug class, same fix, for HelpInjured: PickInjuredTargetOperator was also missing Plan(). Proven
    /// via DangerComponent.ComfortedAt, which only gets populated once ComfortInjuredOperator actually runs at
    /// the end of the full PickInjuredTarget -> MoveTo -> ComfortInjured chain.
    /// </summary>
    [Test]
    public async Task HelpInjured_ActuallyWalksToAndComfortsInjuredParty()
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

            server.System<MobStateSystem>().ChangeMobState(injured, MobState.Critical);
            server.EntMan.GetComponent<PerceptionComponent>(helper).PerceiveAccumulator = 0f;
        });

        // Perception first, then Danger's scan, then Goal's reconsider - same ordering care as the Milestone 7
        // Perception/Danger race fix (resetting them in the same tick isn't guaranteed to be ordered).
        await pair.RunTicksSync(3);
        await server.WaitPost(() => server.EntMan.GetComponent<DangerComponent>(helper).ScanAccumulator = 0f);
        await pair.RunTicksSync(3);
        await server.WaitPost(() => server.EntMan.GetComponent<GoalComponent>(helper).ReconsiderAccumulator = 0f);

        await WaitForCondition(pair, () => server.EntMan.GetComponent<DangerComponent>(helper).ComfortedAt.ContainsKey(injured));

        await server.WaitAssertion(() =>
        {
            var danger = server.EntMan.GetComponent<DangerComponent>(helper);
            Assert.That(danger.ComfortedAt.ContainsKey(injured), Is.True,
                "Helper should have actually walked to and comforted the injured character, not just changed GoalComponent.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(helper);
            server.EntMan.DeleteEntity(injured);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Before this milestone, GoalComponent.CurrentGoal was observability-only for everything except
    /// Socialize - HTN branches independently re-derived their own gate and never consulted it, so an LLM
    /// decision to abandon a task had no effect on real behaviour. CurrentGoalPrecondition (added this
    /// milestone) is what makes this test pass: forcing Rest via the LLM gateway now actually blocks
    /// RepairMachineCompound even though the AI is fully eligible (job, tool in hand, damaged machine visible).
    /// </summary>
    [Test]
    public async Task LlmOverride_ForcingRest_ActuallyBlocksEligibleRepair()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnAiWithDamagedMachine(pair, station, StationEngineer);
        await GiveWelder(pair, aiPlayer);

        await server.WaitPost(() =>
        {
            // Would naturally prefer RepairMachine if left alone - the override below is what should stop it.
            var personality = server.EntMan.GetComponent<PersonalityComponent>(aiPlayer);
            personality.Professionalism = 1f;
            personality.Laziness = 0f;

            var gateway = server.System<LlmGatewaySystem>();
            var applied = gateway.TryApplyDecision(aiPlayer, new LlmDecision(AIGoals.Rest, 0.9f, "test: forced rest"));
            Assert.That(applied, Is.True);

            // Hold the override well past this test's observation window, prevent GoalSystem's own routine
            // reconsider from ever re-running while it's held (matching SocialSystemTests' established
            // "ReconsiderAccumulator = 999f" pattern - SpawnAiWithDamagedMachine already has a reconsider
            // queued up from its own setup, and racing that against this override is exactly what makes the
            // outcome timing-dependent instead of deterministic), and make sure no unrelated danger signal can
            // clear it out from under this test - that interruption behaviour is Emergency_*'s job to prove.
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            goal.LlmOverrideExpiresAt = server.Timing.CurTime + TimeSpan.FromMinutes(5);
            goal.ReconsiderAccumulator = 999f;
            var danger = server.EntMan.GetComponent<DangerComponent>(aiPlayer);
            danger.ThreatSource = null;
            danger.FireHazardLocation = null;
        });

        var damageBefore = GetTotalDamage(pair, machine);
        Assert.That(damageBefore, Is.GreaterThan(0f), "Test setup: machine should start damaged.");

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetTotalDamage(pair, machine), Is.EqualTo(damageBefore),
                "An LLM override forcing Rest should actually prevent the eligible, tool-in-hand repair from happening.");
            Assert.That(server.EntMan.GetComponent<GoalComponent>(aiPlayer).CurrentGoal, Is.EqualTo(AIGoals.Rest));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Spec Milestone 1 section 10: personality must have an observable effect on which goal wins the same
    /// situation. Both engineers face an identical eligible-repair + fatigued-enough-to-rest situation;
    /// only their Professionalism/Laziness differ.
    /// </summary>
    [Test]
    public async Task Personality_ProfessionalismAndLaziness_ChangeWhichGoalWinsInTheSameSituation()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (engineerA, machineA) = await SpawnAiWithDamagedMachine(pair, station, StationEngineer);
        await GiveWelder(pair, engineerA);
        var (engineerB, machineB) = await SpawnAiWithDamagedMachine(pair, station, StationEngineer);
        await GiveWelder(pair, engineerB);

        await server.WaitPost(() =>
        {
            var a = server.EntMan.GetComponent<PersonalityComponent>(engineerA);
            a.Professionalism = 1f;
            a.Laziness = 0f;

            var b = server.EntMan.GetComponent<PersonalityComponent>(engineerB);
            b.Professionalism = 0f;
            b.Laziness = 1f;

            // Same situation for both: fatigued enough that Rest is also eligible (FatiguePrecondition's 0.75
            // threshold), on top of the already-eligible repair opportunity.
            server.EntMan.GetComponent<NeedsComponent>(engineerA).Fatigue = 0.8f;
            server.EntMan.GetComponent<NeedsComponent>(engineerB).Fatigue = 0.8f;

            server.EntMan.GetComponent<GoalComponent>(engineerA).ReconsiderAccumulator = 0f;
            server.EntMan.GetComponent<GoalComponent>(engineerB).ReconsiderAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<GoalComponent>(engineerA).CurrentGoal, Is.EqualTo(ProfessionalGoals.RepairMachine),
                "High-professionalism, low-laziness engineer should prioritize work over rest.");
            Assert.That(server.EntMan.GetComponent<GoalComponent>(engineerB).CurrentGoal, Is.EqualTo(AIGoals.Rest),
                "Low-professionalism, high-laziness engineer should prioritize rest over the same work opportunity.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(engineerA);
            server.EntMan.DeleteEntity(machineA);
            server.EntMan.DeleteEntity(engineerB);
            server.EntMan.DeleteEntity(machineB);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Spec Milestone 1 sections 8/12: the full emergency lifecycle. An engineer mid-repair notices a fire,
    /// interrupts repair work for it, then - once the fire is put out - resumes and completes the interrupted
    /// repair. The fire hazard itself is injected directly into DangerComponent rather than ignited via real
    /// atmos gas (this test map has no GridAtmosphereComponent to ignite in the first place - confirmed via
    /// direct inspection, not assumed), the same way DangerSystemTests.GoalSystem_PicksFlee_WhenThreatened
    /// injects DangerComponent.ThreatSource directly rather than dealing real damage: this test is about the
    /// AI's reaction to DangerComponent state, not about re-proving atmos combustion mechanics. Real end-to-end
    /// fire detection is what spec Milestone 1's real, 30-minute gameplay test on an actual station validates.
    /// </summary>
    [Test]
    public async Task Emergency_FireInterruptsRepairWork_ThenAiResumesAndFinishesOnceFireIsOut()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnAiWithDamagedMachine(pair, station, StationEngineer);
        await GiveWelder(pair, aiPlayer);

        await server.WaitPost(() =>
        {
            // Pin personality so Flee (min priority 0.95 at these values) deterministically beats RepairMachine
            // (0.6 at these values) regardless of the AI's random roll.
            var personality = server.EntMan.GetComponent<PersonalityComponent>(aiPlayer);
            personality.Courage = 0f;
            personality.RiskTolerance = 0f;
            personality.Professionalism = 0.5f;
            personality.Laziness = 0.5f;

            // Force a fresh reconsider now that personality is pinned, rather than racing whatever reconsider
            // SpawnAiWithDamagedMachine's own setup already had queued up (WaitPost calls alone don't
            // guarantee GoalSystem.Update() has actually run in between - see LlmOverride_* for the same fix).
            server.EntMan.GetComponent<GoalComponent>(aiPlayer).ReconsiderAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<GoalComponent>(aiPlayer).CurrentGoal, Is.EqualTo(ProfessionalGoals.RepairMachine),
                "Test setup: AI should have settled on the repair job before the emergency starts.");
        });

        await server.WaitPost(() =>
        {
            var xform = server.EntMan.GetComponent<TransformComponent>(aiPlayer);
            var danger = server.EntMan.GetComponent<DangerComponent>(aiPlayer);
            danger.FireHazardLocation = xform.Coordinates;
            danger.ScanAccumulator = 999f; // Don't let the next routine scan immediately clear the injected hazard.

            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            goal.IsLlmOverride = false;
            goal.ReconsiderAccumulator = 0f;
        });

        await WaitForCondition(pair, () => server.EntMan.GetComponent<GoalComponent>(aiPlayer).CurrentGoal == AIGoals.Flee);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<GoalComponent>(aiPlayer).CurrentGoal, Is.EqualTo(AIGoals.Flee),
                "AI should interrupt repair work to flee the fire.");
        });

        // Not asserting the AI physically covers a specific distance here: whether unobstructed space exists
        // away from wherever the fire happened to be depends on this test map's layout (see
        // Flee_FromAttacker_PicksFleeGoalAndProducesAValidPlan's doc comment for the same reasoning) - what
        // matters for this scenario is that it interrupted repair and HTN settled into a real plan reacting to
        // the fire, not that it necessarily found room to run. HTNPlanJob plans asynchronously, so give it a
        // moment to settle past the "CurrentGoal just changed" instant before checking.
        await WaitForCondition(pair, () => server.EntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(aiPlayer).Plan != null, maxIterations: 20);

        await server.WaitAssertion(() =>
        {
            var htn = server.EntMan.GetComponent<Content.Server.NPC.HTN.HTNComponent>(aiPlayer);
            Assert.That(htn.Plan, Is.Not.Null, "AI should have a real plan reacting to the fire, not be stuck unable to plan at all.");
        });

        await server.WaitPost(() =>
        {
            var danger = server.EntMan.GetComponent<DangerComponent>(aiPlayer);
            danger.FireHazardLocation = null;
            danger.ScanAccumulator = 0f;
            server.EntMan.GetComponent<GoalComponent>(aiPlayer).ReconsiderAccumulator = 0f;
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<GoalComponent>(aiPlayer).CurrentGoal, Is.EqualTo(ProfessionalGoals.RepairMachine),
                "Once the fire is out, the AI should return to the interrupted repair goal.");
        });

        await WaitForCondition(pair, () => GetTotalDamage(pair, machine) <= 0f);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetTotalDamage(pair, machine), Is.LessThanOrEqualTo(0f),
                "AI should resume and complete the repair after the emergency ends.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Spec Milestone 1 section 19: an AI should never silently loop forever. Simulates a goal that's been
    /// active for longer than GoalSystem.StuckWarningThreshold (rather than waiting out ~20 real simulated
    /// seconds of replanning) and checks the warning actually fires.
    /// </summary>
    [Test]
    public async Task GoalSystem_WarnsWhenAGoalNeverResolves()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnAiWithDamagedMachine(pair, station, StationEngineer);
        await StripBeltContents(pair, aiPlayer); // No welder anywhere - repair can never actually happen.

        // Give GoalSystem.Update() actual tick time to process the reconsider request SpawnAiWithDamagedMachine
        // queued up - unlike the other tests here, nothing else (GiveWelder, a WaitForCondition poll) happens to
        // advance a tick before this assertion, so it must be done explicitly.
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            Assert.That(goal.CurrentGoal, Is.EqualTo(ProfessionalGoals.RepairMachine),
                "Test setup: GoalSystem picks RepairMachine as intent regardless of tool availability - only HTN's HasToolQualityPrecondition checks that.");
            Assert.That(goal.LastStuckWarningAt, Is.EqualTo(TimeSpan.Zero));
        });

        await server.WaitPost(() =>
        {
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            goal.CurrentGoalSince = server.Timing.CurTime - GoalSystem.StuckWarningThreshold - TimeSpan.FromSeconds(1);
            goal.ReconsiderAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<GoalComponent>(aiPlayer).LastStuckWarningAt, Is.GreaterThan(TimeSpan.Zero),
                "GoalSystem should have logged (and recorded) a stuck-goal warning once the threshold passed.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
