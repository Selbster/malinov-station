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
using Content.Server.Hands.Systems;
using Content.Server.NPC.HTN;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Components;
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
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// Milestone 11 acceptance checks: the first data-driven professional/job-specific goal (spec section 12).
/// An eligible AI player already holding a welder autonomously walks to a nearby damaged machine it can see
/// and repairs it via the same vanilla InteractUsingEvent -> RepairableSystem pipeline a real player's click
/// would use - and, symmetrically, an AI player whose job isn't eligible, or who isn't holding the right
/// tool, never does so even with the same opportunity right next to it.
/// </summary>
[TestFixture]
public sealed class ProfessionalGoalsTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<JobPrototype> StationEngineer = "StationEngineer";
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";

    private const string Map = "AIPlayerProfessionalGoalsTestMap";
    private const string TestMachine = "TestRepairableMachine";

    [TestPrototypes]
    private static readonly string AIPlayerProfessionalGoalsTestMap = @$"
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
    /// Spawns an AI player with the given job, co-located with a freshly-damaged TestRepairableMachine. Forces
    /// RepairOpportunitySystem's scan to run immediately and lets it complete *before* forcing GoalSystem's
    /// own reconsideration - resetting both accumulators in the same tick would race (system update order
    /// isn't guaranteed), exactly like the Milestone 7 Perception/Danger test race this mirrors the fix for.
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

    private async Task GiveWelder(TestPair pair, EntityUid aiPlayer, bool switchAwayFromItsHand = false)
    {
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            var hands = server.System<HandsSystem>();
            var welder = server.EntMan.SpawnEntity("Welder", server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates);
            Assert.That(hands.TryPickup(aiPlayer, welder), Is.True, "Test setup: AI player should be able to pick up the welder.");

            if (!switchAwayFromItsHand)
                return;

            // Simulates a welder ending up in whichever hand isn't active (e.g. handed over by an admin) -
            // the AI should still find and use it rather than only ever looking at the active hand.
            var welderHand = hands.EnumerateHands(aiPlayer).First(h => hands.GetHeldItem(aiPlayer, h) == welder);
            var otherHand = hands.EnumerateHands(aiPlayer).First(h => h != welderHand);
            Assert.That(hands.TrySetActiveHand(aiPlayer, otherHand), Is.True, "Test setup: should be able to switch away from the welder's hand.");
            Assert.That(hands.GetActiveItem(aiPlayer), Is.Not.EqualTo(welder), "Test setup: welder should no longer be in the active hand.");
        });
    }

    /// <summary>
    /// Equips a backpack and puts a welder inside it - nothing in any hand at all. Simulates the AI's actual
    /// starting gear (tools live in the belt/backpack, not the hands) rather than an admin manually placing
    /// the tool directly in a hand.
    /// </summary>
    private async Task GiveWelderInBackpack(TestPair pair, EntityUid aiPlayer)
    {
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            var inventory = server.System<InventorySystem>();
            var container = server.System<SharedContainerSystem>();

            // The AI's random loadout may already have something in "back" - make room for our own backpack.
            inventory.TryUnequip(aiPlayer, "back", silent: true, force: true);

            var backpack = server.EntMan.SpawnEntity("ClothingBackpackSatchel", coords);
            Assert.That(inventory.TryEquip(aiPlayer, backpack, "back", force: true), Is.True,
                "Test setup: should be able to equip the backpack.");

            var welder = server.EntMan.SpawnEntity("Welder", coords);
            var storage = server.EntMan.GetComponent<StorageComponent>(backpack);
            Assert.That(container.Insert(welder, storage.Container), Is.True,
                "Test setup: should be able to put the welder in the backpack.");

            Assert.That(server.System<HandsSystem>().GetActiveItem(aiPlayer), Is.Null,
                "Test setup: welder should not be in a hand at all.");
        });
    }

    /// <summary>
    /// Empties (and deletes) the contents of the AI's belt. StationEngineer's real starting belt
    /// (ClothingBeltUtilityEngineering) comes pre-filled with a welder via EntityTableContainerFill, so tests
    /// that need to prove "no tool anywhere" (or that a tool was found via one *specific* source, e.g. the
    /// backpack) need to get rid of that default welder first, or they'd trivially pass/fail for the wrong
    /// reason.
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
        var damageable = server.EntMan.GetComponent<DamageableComponent>(machine);
        return (float)server.System<DamageableSystem>().GetTotalDamage((machine, damageable));
    }

    /// <summary>
    /// The core end-to-end path: an eligible AI player (StationEngineer) already holding a welder, seeing a
    /// damaged machine, autonomously walks over and repairs it - fully unattended, no LLM involved.
    /// </summary>
    [Test]
    public async Task RepairMachine_EligibleEngineerWithWelder_RepairsNearbyDamagedMachine()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnAiWithDamagedMachine(pair, station, StationEngineer);
        await GiveWelder(pair, aiPlayer);

        Assert.That(GetTotalDamage(pair, machine), Is.GreaterThan(0f), "Test setup: machine should start damaged.");

        await WaitForCondition(pair, () => GetTotalDamage(pair, machine) <= 0f);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetTotalDamage(pair, machine), Is.LessThanOrEqualTo(0f),
                "AI engineer with a welder should have repaired the nearby damaged machine.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// A Passenger (not in RepairMachine's job list) never pursues the goal even with a welder in hand and a
    /// damaged machine right next to it - the job filter is a hard gate, not a soft preference.
    /// </summary>
    [Test]
    public async Task RepairMachine_IneligibleJob_NeverRepairs()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnAiWithDamagedMachine(pair, station, Passenger);
        await GiveWelder(pair, aiPlayer);

        var damageBefore = GetTotalDamage(pair, machine);
        Assert.That(damageBefore, Is.GreaterThan(0f), "Test setup: machine should start damaged.");

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetTotalDamage(pair, machine), Is.EqualTo(damageBefore),
                "A Passenger should never attempt to repair a machine, even with a welder in hand.");

            var goal = server.EntMan.GetComponent<GoalComponent>(aiPlayer);
            Assert.That(goal.CurrentGoal, Is.Not.EqualTo(ProfessionalGoals.RepairMachine));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// An eligible engineer with no Welding-quality tool anywhere - not hands, not belt, not backpack - never
    /// repairs the machine, since it simply has nothing to act on the opportunity with. Explicitly strips the
    /// belt because StationEngineer's real starting belt normally comes with a welder already inside it.
    /// </summary>
    [Test]
    public async Task RepairMachine_NoToolAnywhere_NeverRepairs()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnAiWithDamagedMachine(pair, station, StationEngineer);
        await StripBeltContents(pair, aiPlayer);

        var damageBefore = GetTotalDamage(pair, machine);
        Assert.That(damageBefore, Is.GreaterThan(0f), "Test setup: machine should start damaged.");

        await pair.RunTicksSync(60);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetTotalDamage(pair, machine), Is.EqualTo(damageBefore),
                "An engineer with no welder anywhere should never manage to repair the machine.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Regression check for a real-gameplay finding: a welder handed to the AI (e.g. by an admin) doesn't
    /// necessarily land in whichever hand happens to be "active" - the AI must still find and use it rather
    /// than only ever looking at the active hand the way a human player's hotbar would.
    /// </summary>
    [Test]
    public async Task RepairMachine_ToolInNonActiveHand_StillRepairs()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnAiWithDamagedMachine(pair, station, StationEngineer);
        await GiveWelder(pair, aiPlayer, switchAwayFromItsHand: true);

        Assert.That(GetTotalDamage(pair, machine), Is.GreaterThan(0f), "Test setup: machine should start damaged.");

        await WaitForCondition(pair, () => GetTotalDamage(pair, machine) <= 0f);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetTotalDamage(pair, machine), Is.LessThanOrEqualTo(0f),
                "AI engineer should still repair the machine even with the welder in a non-active hand.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The realistic case: a welder sitting loose inside an equipped backpack (nowhere near a hand at all,
    /// and - belt stripped - nowhere else either). The AI should find it there, pull it out into a hand, and
    /// still repair the machine - matching how a real engineer's tools normally live in their bag/belt/
    /// pockets rather than already in-hand.
    /// </summary>
    [Test]
    public async Task RepairMachine_ToolInBackpack_StillRepairs()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (aiPlayer, machine) = await SpawnAiWithDamagedMachine(pair, station, StationEngineer);
        await StripBeltContents(pair, aiPlayer);
        await GiveWelderInBackpack(pair, aiPlayer);

        Assert.That(GetTotalDamage(pair, machine), Is.GreaterThan(0f), "Test setup: machine should start damaged.");

        await WaitForCondition(pair, () => GetTotalDamage(pair, machine) <= 0f);

        await server.WaitAssertion(() =>
        {
            Assert.That(GetTotalDamage(pair, machine), Is.LessThanOrEqualTo(0f),
                "AI engineer should fetch the welder from its backpack and repair the machine.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(aiPlayer);
            server.EntMan.DeleteEntity(machine);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The LLM decision whitelist (spec section 19) must also accept a data-driven professional goal id, not
    /// just the fixed AIGoals.All set - this is what makes an LLM-proposed "RepairMachine" intent valid.
    /// </summary>
    [Test]
    public async Task LlmGateway_AcceptsProfessionalGoalIntent_RepairMachine()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid? aiPlayer = null;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(StationEngineer, station);
            Assert.That(aiPlayer, Is.Not.Null);
            var uid = aiPlayer!.Value;

            var gateway = server.System<LlmGatewaySystem>();
            var applied = gateway.TryApplyDecision(uid, new LlmDecision(ProfessionalGoals.RepairMachine, 0.7f, "there is a broken machine nearby"));
            Assert.That(applied, Is.True, "A professional goal id should pass the LLM decision whitelist.");

            var goal = server.EntMan.GetComponent<GoalComponent>(uid);
            Assert.That(goal.CurrentGoal, Is.EqualTo(ProfessionalGoals.RepairMachine));
        });

        await server.WaitPost(() =>
        {
            if (aiPlayer is { } uid)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
