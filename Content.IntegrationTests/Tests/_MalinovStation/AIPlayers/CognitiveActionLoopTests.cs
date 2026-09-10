#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Prototypes;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.Damage.Systems;
using Content.Server.Hands.Systems;
using Content.Shared.CCVar;
using Content.Shared._MalinovStation.AIPlayers;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.3 vertical-slice acceptance checks: Independent Intent + ActionProposal actually close the
/// loop end to end (LLM decision -&gt; ActionProposal -&gt; AiActionRegistrySystem -&gt; a real GoalComponent/HTN
/// effect -&gt; feedback), not just at the type level. Mirrors <see cref="GoalIntentUnificationTests"/>'
/// causal-proof style (assert real repair damage doesn't move, not just that a field got set) and
/// <see cref="CognitiveModeGatingTests"/>' gating conventions, but exercises the new PursueGoal/ContinueActivity
/// actions via <see cref="LlmGatewaySystem.TryApplyCognitiveDecision"/> instead of the legacy
/// <see cref="LlmGatewaySystem.TryApplyDecision"/>.
/// </summary>
[TestFixture]
public sealed class CognitiveActionLoopTests : GameTest
{
    private static readonly ProtoId<JobPrototype> StationEngineer = "StationEngineer";
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";

    private const string Map = "CognitiveActionLoopTestMap";
    private const string TestMachine = "CognitiveActionLoopTestRepairableMachine";

    [TestPrototypes]
    private static readonly string CognitiveActionLoopTestMap = @$"
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
        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled, false);
        var ticker = server.System<GameTicker>();

        ticker.ToggleReadyAll(true);
        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        var station = server.EntMan.EntityQuery<StationDataComponent>().Select(x => x.Owner).FirstOrDefault();
        Assert.That(server.EntMan.EntityExists(station), "Expected a station to exist after round start.");
        return station;
    }

    /// <summary>Spawns a cognitive-mode engineer, co-located with a freshly-damaged test machine - the same
    /// setup GoalIntentUnificationTests.SpawnAiWithDamagedMachine uses, but in cognitive mode.</summary>
    private async Task<(EntityUid AiPlayer, EntityUid Machine)> SpawnCognitiveAiWithDamagedMachine(TestPair pair, EntityUid station)
    {
        var server = pair.Server;
        EntityUid aiPlayer = default;
        EntityUid machine = default;

        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(StationEngineer, station, cognitiveMode: true)!.Value;

            // This fixture tests goal arbitration, not survival in the empty map's atmosphere.
            server.System<GodmodeSystem>().EnableGodmode(aiPlayer);

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

    /// <summary>Empties (and deletes) the contents of the AI's belt - needed because StationEngineer's real
    /// starting belt comes pre-filled with a welder.</summary>
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
    /// Primary causal proof: a cognitive decision's "PursueGoal" action proposal actually reaches
    /// GoalComponent/HTN, exactly like GoalIntentUnificationTests.LlmOverride_ForcingRest_
    /// ActuallyBlocksEligibleRepair proves for the legacy path - except reached via
    /// TryApplyCognitiveDecision/AiActionRegistrySystem, and with IntentComponent deliberately left free-form
    /// and divergent from the goal name the action actually triggered. Not hunger/thirst: FoodCompound isn't
    /// gated on CurrentGoalPrecondition at all (observability-only, per GoalComponent's own doc comment), so
    /// PursueGoal(SatisfyHunger) would prove nothing causally - Rest is the correct proof.
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_PursueGoalRest_SetsFreeFormIntentAndCausallyBlocksEligibleRepair()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnCognitiveAiWithDamagedMachine(pair, station);
        await GiveWelder(pair, aiPlayer);

        await server.WaitPost(() =>
        {
            // Would naturally prefer RepairMachine if left alone - the PursueGoal(Rest) action is what should
            // stop it.
            var personality = server.EntMan.GetComponent<PersonalityComponent>(aiPlayer);
            personality.Professionalism = 1f;
            personality.Laziness = 0f;

            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "fatigue", "need_a_break", 0.9f, 0.85f, "I should rest instead",
                "PursueGoal", new Dictionary<string, string> { ["goal"] = AIGoals.Rest });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);

            var intent = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.That(intent.Name, Is.EqualTo("need_a_break"),
                "Intent should stay the free-form self-narration, not the goal name PursueGoal actually triggered.");

            // Hold the override well past this test's observation window and prevent unrelated signals from
            // clearing it - same pattern as LlmOverride_ForcingRest_ActuallyBlocksEligibleRepair.
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
                "A cognitive PursueGoal(Rest) action should actually prevent the eligible, tool-in-hand repair from happening.");
            Assert.That(server.EntMan.GetComponent<GoalComponent>(aiPlayer).CurrentGoal, Is.EqualTo(AIGoals.Rest));
            Assert.That(server.EntMan.GetComponent<IntentComponent>(aiPlayer).Name, Is.EqualTo("need_a_break"),
                "Intent should still be the free-form string, independent of GoalComponent, even after HTN has acted on the reflex goal for a while.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// "ContinueActivity" is a true no-op at the GoalComponent level - it must still flow through the registry
    /// and update Intent, proving "keep doing what I'm doing" is a first-class action, not a special case the
    /// LLM has no way to express.
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_ContinueActivity_LeavesGoalUnchangedButUpdatesIntent()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnCognitiveAiWithDamagedMachine(pair, station);

        await server.WaitAssertion(() =>
        {
            var goalBefore = server.EntMan.GetComponent<GoalComponent>(aiPlayer).CurrentGoal;

            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "curiosity", "keep_working", 0.5f, 0.7f, "nothing has changed",
                "ContinueActivity", new Dictionary<string, string>());
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);

            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            var intent = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(goal.CurrentGoal, Is.EqualTo(goalBefore), "ContinueActivity should not touch GoalComponent at all.");
                Assert.That(goal.IsLlmOverride, Is.False, "ContinueActivity is a true no-op on the reflex layer - it shouldn't set an override either.");
                Assert.That(intent.Name, Is.EqualTo("keep_working"));
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// AI Players 0.3's concrete feedback mechanism: a proposed action that fails validation writes an outcome
    /// memory (via AiTraceSystem.ActionFailed, now public) and forces the next reflection to happen
    /// immediately, instead of waiting out the routine ~45s reflection cooldown.
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_ActionFailure_WritesOutcomeMemoryAndForcesFastReflection()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnCognitiveAiWithDamagedMachine(pair, station);

        await server.WaitAssertion(() =>
        {
            server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator = 30f;
            var memoriesBefore = server.EntMan.GetComponent<MemoryComponent>(aiPlayer).Memories.Count;

            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "curiosity", "do_something_impossible", 0.9f, 0.9f, "bogus goal",
                "PursueGoal", new Dictionary<string, string> { ["goal"] = "NotARealGoal" });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.False);

            var memoriesAfter = server.EntMan.GetComponent<MemoryComponent>(aiPlayer).Memories.Count;
            Assert.Multiple(() =>
            {
                Assert.That(memoriesAfter, Is.EqualTo(memoriesBefore + 1),
                    "A failed action proposal should write exactly one outcome memory.");
                Assert.That(server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator, Is.EqualTo(0f),
                    "A failed action should force the next reflection to happen immediately, not wait out the routine cooldown.");
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// AiTraceSystem.GoalFailed is also called from the internal HTN-plan diffing (a stuck/abandoned goal, not
    /// just a failed action proposal) - extends CognitiveModeGatingTests.
    /// CognitiveAiPlayer_ReceivesComponentsAndTraceEventsWriteMemory with the fast-reflection assertion.
    /// </summary>
    [Test]
    public async Task GoalFailed_ForCognitiveAiPlayer_AlsoForcesFastReflection()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;

        await server.WaitAssertion(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            aiPlayer = aiPlayers.SpawnAiPlayer(StationEngineer, station, cognitiveMode: true);
            Assert.That(aiPlayer, Is.Not.Null);
            var uid = aiPlayer!.Value;

            server.EntMan.GetComponent<CognitiveModeComponent>(uid).ReflectionAccumulator = 30f;

            server.System<AiTraceSystem>().GoalFailed(uid, "RepairMachine", "NoProgress");

            Assert.That(server.EntMan.GetComponent<CognitiveModeComponent>(uid).ReflectionAccumulator, Is.EqualTo(0f));
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Second end-to-end scenario (spec section 24): a cognitive AI commits to RepairMachine via PursueGoal,
    /// but with no welder available the repair can never make progress - GoalSystem.WarnIfStuck (unchanged by
    /// this milestone) eventually abandons it and fires GoalFailed, which now forces a fast re-reflection. A
    /// fresh cognitive decision is then shown to actually take effect, proving the AI can genuinely change its
    /// mind after observing its own action's result - without standing up a real LLM, consistent with how
    /// every existing LLM-dependent test in this codebase calls TryApply* directly. Mirrors
    /// GoalIntentUnificationTests.GoalSystem_AbandonsGoalWithNoMeasurableProgress_AndPicksSomethingElse's exact
    /// two-stuck-check staging (see its own comments for why each step is ordered the way it is).
    /// </summary>
    [Test]
    public async Task PursueGoalRepairMachine_AbandonedByNoProgress_FiresGoalFailedAndAllowsReEvaluation()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnCognitiveAiWithDamagedMachine(pair, station);
        await StripBeltContents(pair, aiPlayer); // No welder anywhere - repair can never actually happen.

        await server.WaitPost(() =>
        {
            var personality = server.EntMan.GetComponent<PersonalityComponent>(aiPlayer);
            personality.Professionalism = 1f;
            personality.Laziness = 0f;

            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "duty", "finish_repair", 0.9f, 0.9f, "the machine needs fixing",
                "PursueGoal", new Dictionary<string, string> { ["goal"] = ProfessionalGoals.RepairMachine });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<GoalComponent>(aiPlayer).CurrentGoal, Is.EqualTo(ProfessionalGoals.RepairMachine));
        });

        // Let the override lapse so GoalSystem's own formula-driven arbitration (which doesn't care whether a
        // welder is available - only HTN's HasToolQualityPrecondition does) naturally re-confirms RepairMachine
        // on its own, the same way GoalSystem_AbandonsGoalWithNoMeasurableProgress_AndPicksSomethingElse's
        // no-override setup does.
        await server.WaitPost(() =>
        {
            server.EntMan.GetComponent<GoalComponent>(aiPlayer).IsLlmOverride = false;
            server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates =
                server.EntMan.GetComponent<TransformComponent>(machine).Coordinates;
            server.EntMan.GetComponent<RepairOpportunityComponent>(aiPlayer).ScanAccumulator = 0f;
            server.EntMan.GetComponent<GoalComponent>(aiPlayer).ReconsiderAccumulator = 0f;
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<GoalComponent>(aiPlayer).CurrentGoal, Is.EqualTo(ProfessionalGoals.RepairMachine),
                "Test setup: goal should have re-settled on RepairMachine via the formula after the override lapsed.");
        });

        // First stuck check: establishes the progress baseline, doesn't abandon yet.
        await server.WaitPost(() =>
        {
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            goal.CurrentGoalSince = server.Timing.CurTime - GoalSystem.StuckWarningThreshold - TimeSpan.FromSeconds(1);
            goal.LastStuckWarningAt = server.Timing.CurTime - GoalSystem.StuckWarningThreshold - TimeSpan.FromSeconds(1);
            server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator = 30f;
            goal.ReconsiderAccumulator = 0f;
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            Assert.That(goal.ProgressBaseline, Is.Not.Null, "First stuck check should have recorded a progress baseline.");
            Assert.That(goal.RecentlyAbandoned, Does.Not.ContainKey(ProfessionalGoals.RepairMachine),
                "Shouldn't abandon on the very first stuck check - only once a second check confirms no movement.");
            Assert.That(server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator, Is.InRange(29f, 30f),
                "Just recording a baseline (not yet abandoning) shouldn't force fast reflection.");
        });

        // Re-settle (idle wandering may have carried it off again) before the second stuck check - same
        // ordering care as the test this mirrors, to avoid a reselect stomping the backdated timestamps.
        await server.WaitPost(() =>
        {
            server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates =
                server.EntMan.GetComponent<TransformComponent>(machine).Coordinates;
            server.EntMan.GetComponent<RepairOpportunityComponent>(aiPlayer).ScanAccumulator = 0f;
            server.EntMan.GetComponent<GoalComponent>(aiPlayer).ReconsiderAccumulator = 0f;
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<GoalComponent>(aiPlayer).CurrentGoal, Is.EqualTo(ProfessionalGoals.RepairMachine),
                "Test setup: goal should still be settled on RepairMachine going into the second stuck check.");
        });

        // Second stuck check, a full threshold later: still zero progress - GoalSystem gives up on it, and
        // GoalFailed should now force a fast re-reflection.
        await server.WaitPost(() =>
        {
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            goal.LastStuckWarningAt = server.Timing.CurTime - GoalSystem.StuckWarningThreshold - TimeSpan.FromSeconds(1);
            goal.ReconsiderAccumulator = 0f;
        });
        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            Assert.That(goal.RecentlyAbandoned, Does.ContainKey(ProfessionalGoals.RepairMachine),
                "GoalSystem should have abandoned RepairMachine after confirming zero progress across two stuck checks.");
            var reflection = server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator;
            Assert.That(reflection, Is.LessThanOrEqualTo(0f).Or.GreaterThan(30f),
                "Fast reflection must be queued or already serviced by the heartbeat, not retain the previous countdown.");
        });

        // A fresh cognitive decision now actually takes effect, proving the AI can genuinely change its mind
        // after observing its own action's result.
        await server.WaitPost(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "fatigue", "give_up_and_rest", 0.8f, 0.8f, "that's not working, I'll rest instead",
                "PursueGoal", new Dictionary<string, string> { ["goal"] = AIGoals.Rest });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);
        });

        await server.WaitAssertion(() =>
        {
            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            var intent = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.That(goal.CurrentGoal, Is.EqualTo(AIGoals.Rest), "The AI's re-evaluated decision should actually take effect.");
            Assert.That(intent.Name, Is.EqualTo("give_up_and_rest"));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
