#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Prototypes;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.5.1: proves the full cognitive runtime loop (Perception -&gt; CognitiveState -&gt; LLM decision
/// -&gt; Intent -&gt; Category -&gt; Eligibility -&gt; Action Selection -&gt; concrete Action -&gt; Execution -&gt;
/// Result/Feedback -&gt; Memory/CognitiveState update -&gt; next decision) as one continuous causal chain for
/// three concrete scenarios (Hunger/Work/Emergency), rather than re-proving individual links already covered
/// elsewhere in this directory (<see cref="CognitiveActionLoopTests"/>, <see cref="AiBusyStateTests"/>,
/// <see cref="HierarchicalActionSelectionTests"/>, <see cref="ActionEligibilityTests"/>,
/// <see cref="LandmarkNavigationTests"/>, <see cref="RussianLocalizationTests"/>). Every decision here is
/// hand-built and applied via <see cref="LlmGatewaySystem.TryApplyCognitiveDecision"/> - the same no-mock
/// convention every existing LLM-dependent test in this tree already uses (a real network round-trip is only
/// ever proven against a live Ollama server, manually).
/// </summary>
[TestFixture]
public sealed class CognitiveRuntimeIntegrationTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<JobPrototype> StationEngineer = "StationEngineer";
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";
    private const string FoodItem = "FoodSnackBoritos";

    private const string Map = "CognitiveRuntimeIntegrationTestMap";
    private const string TestMachine = "CognitiveRuntimeIntegrationTestRepairableMachine";

    [TestPrototypes]
    private static readonly string CognitiveRuntimeIntegrationTestMap = @$"
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

    private static async Task WaitForCondition(TestPair pair, Func<bool> condition, int maxIterations = 150, int ticksPerIteration = 5)
    {
        for (var i = 0; i < maxIterations && !condition(); i++)
            await pair.RunTicksSync(ticksPerIteration);
    }

    /// <summary>Mirrors HierarchicalActionSelectionTests' helper of the same shape - removes incidental
    /// ambient mobs (including the connected test client's own character) so eligibility scans only ever see
    /// what this test itself set up.</summary>
    private async Task ClearOtherMobsNearby(TestPair pair, EntityUid keep, EntityCoordinates center, float radius)
    {
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            var lookup = server.EntMan.System<EntityLookupSystem>();
            var nearby = new HashSet<Entity<Content.Shared.Mobs.Components.MobStateComponent>>();
            lookup.GetEntitiesInRange(center, radius, nearby);
            foreach (var mob in nearby)
            {
                if (mob.Owner != keep)
                    server.EntMan.DeleteEntity(mob.Owner);
            }
        });
    }

    private float GetTotalDamage(TestPair pair, EntityUid machine)
    {
        var server = pair.Server;
        var damageable = server.EntMan.GetComponent<Content.Shared.Damage.Components.DamageableComponent>(machine);
        return (float)server.System<DamageableSystem>().GetTotalDamage((machine, damageable));
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
    /// End-to-end Hunger scenario (spec section 4): no legacy Goal/HTN scaffolding involved anywhere in this
    /// chain - a hungry cognitive AI is handed two hand-built decisions (PickUpItem, then the new
    /// AI Players 0.5.1 EatOrDrink action) and satiation must actually, measurably rise as a result, proving
    /// the cognitive pipeline can resolve hunger entirely on its own without depending on vanilla's own HTN
    /// FoodCompound path (already known, and left [Explicit]-disabled, to not reliably fire during real async
    /// HTN planning - see NeedRecoveryTests.Hunger_ResolvesWhenEdibleFoodIsActuallyReachable's own doc comment).
    /// </summary>
    [Test]
    public async Task HungerDecisionLoop()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        // Pinned to Human, not a random species - a randomly-rolled Vox spawns wearing a breathing mask that
        // blocks ingestion outright, a real, separate, already-documented finding (see NeedRecoveryTests), not
        // something this test is trying to prove.
        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, HumanoidCharacterProfile.RandomWithSpecies("Human"), cognitiveMode: true)!.Value;
        });
        await pair.RunTicksSync(5);

        EntityUid food = default;
        string foodName = string.Empty;
        await server.WaitPost(() =>
        {
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            food = server.EntMan.SpawnEntity(FoodItem, coords.Offset(new Vector2(0.5f, 0f)));
            foodName = server.EntMan.GetComponent<MetaDataComponent>(food).EntityName;

            // Just below the Peckish=100 threshold - "Hunger > threshold" per spec. Deliberately not fully
            // Starving (50): empirically, one snack's nutriment only restores ~13 points total (confirmed by
            // running this test against a Starving start and observing the plateau), nowhere near enough to
            // cross two whole thresholds - a real player wouldn't expect one chip bag to fully refill an empty
            // stomach either. Started close enough to Peckish that the one snack's real restoration crosses it.
            var satiation = server.EntMan.GetComponent<SatiationComponent>(aiPlayer);
            server.System<SatiationSystem>().SetValue((aiPlayer, satiation), SatiationSystem.Hunger, 95f);

            server.EntMan.GetComponent<ItemOpportunityComponent>(aiPlayer).ScanAccumulator = 0f;
        });
        await pair.RunTicksSync(5);

        // Cognitive Decision 1: PickUpItem - Category Work, eligible because ItemOpportunitySystem's scan
        // already found the loose snack.
        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "hungry", "найти_еду", 0.8f, 0.9f, "Я голоден и хочу найти еду.",
                PickUpItemAction.ActionName, new Dictionary<string, string> { ["target"] = foodName });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var hands = server.System<HandsSystem>();
            Assert.That(hands.IsHolding(aiPlayer, food, out _), Is.True,
                "Test setup: the PickUpItem decision should have actually picked up the food.");

            // EatOrDrink should now be eligible - the AI is holding an Edible item.
            var eligible = server.System<AiActionRegistrySystem>().GetEligibleActions(aiPlayer);
            Assert.That(eligible.Select(a => a.Name), Does.Contain(EatOrDrinkAction.ActionName));
        });

        // Cognitive Decision 2: EatOrDrink - resolves the actual hunger, not just picks a label.
        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "hungry", "поесть", 0.85f, 0.9f, "У меня в руке еда, пора поесть.",
                EatOrDrinkAction.ActionName, new Dictionary<string, string>());
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);
        });

        var satiationSystem = server.System<SatiationSystem>();
        await WaitForCondition(pair, () =>
        {
            var satiation = server.EntMan.GetComponent<SatiationComponent>(aiPlayer);
            var value = satiationSystem.GetValueOrNull((aiPlayer, satiation), SatiationSystem.Hunger);
            return value is { } v && v >= 100f;
        }, maxIterations: 300, ticksPerIteration: 10);

        await server.WaitAssertion(() =>
        {
            var satiation = server.EntMan.GetComponent<SatiationComponent>(aiPlayer);
            var value = satiationSystem.GetValueOrNull((aiPlayer, satiation), SatiationSystem.Hunger);
            Assert.That(value, Is.GreaterThan(95f),
                "The AI's own EatOrDrink decision should have actually raised hunger satiation, not just been applied as a label.");

            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(aiPlayer);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.Needs.IsHungry, Is.False,
                $"The next cognitive cycle should see the resolved hunger, not keep reporting the AI as hungry. Final satiation value: {value:0.0}.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(food);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// End-to-end Work scenario (spec section 5): deliberately does NOT preset GoalComponent.CurrentGoal to
    /// RepairMachine beforehand - the cognitive PursueGoal(RepairMachine) decision itself is what sets it, and
    /// real machine damage must actually reach 0 over time as a result (the same causal-proof style
    /// GoalIntentUnificationTests.Emergency_FireInterruptsRepairWork_ThenAiResumesAndFinishesOnceFireIsOut
    /// already uses for the legacy path), proving the cognitive entry point drives real HTN execution to
    /// completion, not just a label change.
    /// </summary>
    [Test]
    public async Task WorkDecisionLoop()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityUid machine = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(StationEngineer, station, cognitiveMode: true)!.Value;

            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            machine = server.EntMan.SpawnEntity(TestMachine, coords);

            var proto = server.ResolveDependency<IPrototypeManager>();
            var damage = new DamageSpecifier(proto.Index(BluntDamageType), FixedPoint2.New(20));
            server.System<DamageableSystem>().TryChangeDamage(machine, damage, ignoreResistances: true);

            server.EntMan.GetComponent<RepairOpportunityComponent>(aiPlayer).ScanAccumulator = 0f;

            var personality = server.EntMan.GetComponent<PersonalityComponent>(aiPlayer);
            personality.Professionalism = 1f;
            personality.Laziness = 0f;
        });
        await GiveWelder(pair, aiPlayer);
        await pair.RunTicksSync(2);

        var damageBefore = GetTotalDamage(pair, machine);
        Assert.That(damageBefore, Is.GreaterThan(0f), "Test setup: machine should start damaged.");

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<GoalComponent>(aiPlayer).CurrentGoal, Is.Not.EqualTo(ProfessionalGoals.RepairMachine),
                "Test setup: nothing should have preset RepairMachine yet - the cognitive decision below is what should set it.");

            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "duty", "почини_машину", 0.9f, 0.9f, "Машина повреждена, я инженер и должен её починить.",
                PursueGoalAction.ActionName, new Dictionary<string, string> { ["goal"] = ProfessionalGoals.RepairMachine });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);

            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            Assert.That(goal.CurrentGoal, Is.EqualTo(ProfessionalGoals.RepairMachine));
            Assert.That(goal.IsLlmOverride, Is.True);
        });

        await WaitForCondition(pair, () => GetTotalDamage(pair, machine) <= 0f, maxIterations: 200);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetTotalDamage(pair, machine), Is.LessThanOrEqualTo(0f),
                "The cognitive PursueGoal(RepairMachine) decision should have driven real HTN execution all the way to a completed repair, not just set a label.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// End-to-end Emergency scenario (spec section 6): extends AiBusyStateTests'
    /// AttackWhileBusy_ForcesFastReflectionWithoutWaitingForRoutineCooldown one link further - after the
    /// already-proven interruption (busy clears, fast reflection forced), a fresh cognitive decision is applied
    /// and must actually take hold. PursueGoal(Flee) specifically (not just any decision) because a successful
    /// apply sets GoalComponent.IsLlmOverride=true, which only the cognitive layer's own TrySetExternalGoal
    /// write path ever does - DangerSystem's own reflex response to the same attack always leaves IsLlmOverride
    /// false, so this distinguishes "the cognitive layer responded" from "the reflex layer already handled it".
    /// </summary>
    [Test]
    public async Task EmergencyInterruptionLoop()
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

        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var busyDecision = new LlmCognitiveDecision(
                "duty", "чинить_апк", 0.8f, 0.9f, "Нужно починить APC.",
                PursueGoalAction.ActionName, new Dictionary<string, string> { ["goal"] = ProfessionalGoals.RepairMachine });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, busyDecision), Is.True);
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.EqualTo(PursueGoalAction.ActionName));

            // Far from ever firing on its own routine cadence - only the interruption below should reset this.
            server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator = 999f;

            var damageable = server.System<DamageableSystem>();
            var proto = server.ResolveDependency<IPrototypeManager>();
            var damage = new DamageSpecifier(proto.Index(BluntDamageType), FixedPoint2.New(10));
            damageable.TryChangeDamage(aiPlayer, damage, origin: attacker);
        });

        // AiBusyStateSystem's shared scan accumulator needs up to a full ~1s cycle to notice - same margin
        // AiBusyStateTests itself uses.
        await pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator, Is.LessThanOrEqualTo(45f),
                    "Test setup: the attack should have forced an immediate cognitive re-reflection.");
                Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.Null,
                    "Test setup: the interrupted commitment should have cleared.");
            });
        });

        // The cognitive layer's next decision, applied fresh - proves it can genuinely act on the emergency,
        // not just that the reflex layer's own flags got cleared.
        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var fleeDecision = new LlmCognitiveDecision(
                "safety", "бежать", 0.95f, 0.95f, "На меня напали, нужно уйти в безопасное место.",
                PursueGoalAction.ActionName, new Dictionary<string, string> { ["goal"] = AIGoals.Flee });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, fleeDecision), Is.True);

            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(goal.CurrentGoal, Is.EqualTo(AIGoals.Flee));
                Assert.That(goal.IsLlmOverride, Is.True,
                    "IsLlmOverride=true is the signal this came from the cognitive decision, not from DangerSystem's own reflex response.");
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(attacker);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Extends CognitiveActionLoopTests.TryApplyCognitiveDecision_ActionFailure_WritesOutcomeMemoryAndForcesFastReflection
    /// one step further: a follow-up decision, applied right after the traced failure, must actually succeed -
    /// positive proof the AI can move on, not just an inference from "memory was written and reflection was
    /// forced".
    /// </summary>
    [Test]
    public async Task ActionFailureTriggersReevaluation()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
        });

        await server.WaitAssertion(() =>
        {
            server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator = 30f;
            var memoriesBefore = server.EntMan.GetComponent<MemoryComponent>(aiPlayer).Memories.Count;

            var gateway = server.System<LlmGatewaySystem>();
            var badDecision = new LlmCognitiveDecision(
                "curiosity", "невозможное", 0.9f, 0.9f, "выдуманная цель",
                PursueGoalAction.ActionName, new Dictionary<string, string> { ["goal"] = "NotARealGoal" });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, badDecision), Is.False);

            var memoriesAfter = server.EntMan.GetComponent<MemoryComponent>(aiPlayer).Memories.Count;
            Assert.That(memoriesAfter, Is.EqualTo(memoriesBefore + 1));
            Assert.That(server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator, Is.EqualTo(0f));

            // The next decision - a real, valid one - should apply cleanly. Failed actions must not leave the
            // AI stuck unable to make further progress.
            var goodDecision = new LlmCognitiveDecision(
                "curiosity", "продолжить", 0.5f, 0.7f, "то не сработало, попробую другое",
                ContinueActivityAction.ActionName, new Dictionary<string, string>());
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, goodDecision), Is.True);

            var intent = server.EntMan.GetComponent<IntentComponent>(aiPlayer);
            Assert.That(intent.Name, Is.EqualTo("продолжить"),
                "Intent should reflect the follow-up decision, proving the AI moved on rather than repeating the failure forever.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The untested link in the memory -&gt; cognition chain: LandmarkNavigationTests already proves a memory
    /// reaches CognitiveState.KnownLocations and can drive a GoToKnownLocation decision; this proves a memory's
    /// Content literally reaches the next Cognitive LLM prompt's actual text (not just a CognitiveState field
    /// a human has to trust gets used), for a general "outcome" memory rather than a landmark one.
    /// </summary>
    [Test]
    public async Task MemoryAffectsNextDecision()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        const string memoryContent = "Я нашёл кухню рядом с Medbay.";

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: memoryContent, importance: 0.9f, source: "outcome");

            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(aiPlayer);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.KnownFacts, Does.Contain(memoryContent),
                "Test setup: a high-importance outcome memory should surface into KnownFacts.");

            var prompt = PromptBuilder.BuildCognitiveUserPrompt(state!);
            Assert.That(prompt, Does.Contain(memoryContent),
                "The memory's content should reach the actual next Cognitive LLM prompt text, not just a CognitiveState field.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>Cyrillic occupies U+0400-U+04FF - same cheap, dependency-free check RussianLocalizationTests
    /// already uses, kept local so this file doesn't reach across test files for it.</summary>
    private static bool ContainsCyrillic(string text) => text.Any(c => c is >= 'Ѐ' and <= 'ӿ');

    /// <summary>
    /// One composed trace through the whole runtime for a single AI (spec section 11's own worked example),
    /// rather than the isolated per-prompt-builder-call checks RussianLocalizationTests already covers.
    /// Includes AI Players 0.5.1's new EatOrDrinkAction among the real eligible actions, confirming it also
    /// follows the established Russian-description/English-technical-name split.
    /// </summary>
    [Test]
    public async Task RussianCognitiveRuntime()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(aiPlayer);
            Assert.That(state, Is.Not.Null);

            var userPrompt = PromptBuilder.BuildCognitiveUserPrompt(state!);
            var registry = server.System<AiActionRegistrySystem>();
            var eligibleCategories = registry.GetEligibleCategories(aiPlayer);
            var systemPrompt = PromptBuilder.BuildIntentSystemPrompt(new[] { "Rest" }, eligibleCategories);

            var eatOrDrink = registry.AllActions.Single(a => a.Name == EatOrDrinkAction.ActionName);

            Assert.Multiple(() =>
            {
                Assert.That(ContainsCyrillic(userPrompt), Is.True, "The cognitive user prompt should be Russian.");
                Assert.That(ContainsCyrillic(systemPrompt), Is.True, "The cognitive system prompt should be Russian.");
                Assert.That(ContainsCyrillic(eatOrDrink.Description), Is.True, "EatOrDrink's description should be Russian.");
                Assert.That(eatOrDrink.Name, Is.EqualTo("EatOrDrink"), "The technical action name must stay English/unmodified.");
                Assert.That(eatOrDrink.Category, Is.EqualTo(AiActionCategories.Work), "The technical category name must stay English/unmodified.");
                Assert.That(systemPrompt, Does.Contain(AiActionCategories.Work).Or.Contain(AiActionCategories.General),
                    "Category tokens embedded in the prompt must stay in English.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Extends HierarchicalActionSelectionTests' hand-built-list proof
    /// (BuildActionSelectionSystemPrompt_ListsOnlyTheGivenEligibleActions) with the real link: a live AI's
    /// actual AiActionRegistrySystem.GetEligibleActions output, fed unmodified into the Action Selection
    /// prompt, must never surface a genuinely-ineligible action (nothing nearby to pick up/use/talk to).
    /// </summary>
    [Test]
    public async Task OnlyEligibleActionsReachSelection()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
        });
        await ClearOtherMobsNearby(pair, aiPlayer, server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates, 10f);
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var registry = server.System<AiActionRegistrySystem>();
            var eligibleWork = registry.GetEligibleActions(aiPlayer).Where(a => a.Category == AiActionCategories.Work).ToList();

            // Nothing nearby: PickUpItem/UseInteractable/EatOrDrink should all be genuinely ineligible right
            // now, while PursueGoal/SearchArea (always eligible for any AI player) remain.
            Assert.That(eligibleWork.Select(a => a.Name), Does.Not.Contain(PickUpItemAction.ActionName));
            Assert.That(eligibleWork.Select(a => a.Name), Does.Not.Contain(UseInteractableAction.ActionName));
            Assert.That(eligibleWork.Select(a => a.Name), Does.Not.Contain(EatOrDrinkAction.ActionName));

            var prompt = PromptBuilder.BuildActionSelectionSystemPrompt(AiActionCategories.Work, eligibleWork);
            Assert.Multiple(() =>
            {
                Assert.That(prompt, Does.Not.Contain($"\"{PickUpItemAction.ActionName}\""),
                    "An action that isn't currently eligible must never appear as something the LLM could pick.");
                Assert.That(prompt, Does.Not.Contain($"\"{EatOrDrinkAction.ActionName}\""));
                Assert.That(prompt, Does.Contain($"\"{PursueGoalAction.ActionName}\""),
                    "Test setup: PursueGoal should still be offered - it's always eligible.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
