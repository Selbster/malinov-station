#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._MalinovStation.Nutrition;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._MalinovStation.Nutrition;

[TestFixture]
[TestOf(typeof(MilkProducerSystem))]
public sealed class MilkProducerTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: satiation
  id: MilkProducerTestHunger
  baseDecayRate: 0
  maximumValue: 100
  startingValueMinimum: 100
  startingValueMaximum: 100
  thresholds:
    Peckish: 20
  alertCategory: Hunger

- type: entity
  id: MilkProducerTestProducer
  components:
  - type: MobState
  - type: Solution
    id: milk
    solution:
      maxVol: 5
  - type: MilkProducer
    growthDelay: 0.25
    quantityPerUpdate: 5
    hungerPerUnit: 2
    minHungerThreshold: Peckish
    milkingDelay: 0.5

- type: entity
  parent: MilkProducerTestProducer
  id: MilkProducerTestProducerWithoutHunger
  components:
  - type: Satiation
    satiations:
      Thirst:
        prototype: MilkProducerTestHunger

- type: entity
  parent: MilkProducerTestProducer
  id: MilkProducerTestFedProducer
  components:
  - type: Satiation
    satiations:
      Hunger:
        prototype: MilkProducerTestHunger

- type: entity
  parent: MilkProducerTestFedProducer
  id: MilkProducerTestSource
  components:
  - type: MilkProducer
    growthDelay: 600

- type: entity
  parent: MobMuHuman
  id: MilkProducerTestMuHuman
  components:
  - type: MilkProducer
    growthDelay: 0.25
  - type: Satiation
    satiations:
      Hunger:
        prototype: MilkProducerTestHunger

- type: entity
  id: MilkProducerTestUser
  components:
  - type: ComplexInteraction
  - type: DoAfter
  - type: Hands
    hands:
      hand_right:
        location: Right
    sortedHands:
    - hand_right

- type: entity
  id: MilkProducerTestContainer
  components:
  - type: Item
  - type: Solution
    id: drink
    solution:
      maxVol: 10
  - type: RefillableSolution
    solution: drink
  - type: Openable
    opened: true
    closeable: true
    sound: null
""";

    [SidedDependency(Side.Server)] private readonly SharedSolutionContainerSystem _solutions = default!;
    [SidedDependency(Side.Server)] private readonly SatiationSystem _satiation = default!;
    [SidedDependency(Side.Server)] private readonly MobStateSystem _mobState = default!;
    [SidedDependency(Side.Server)] private readonly SharedHandsSystem _hands = default!;
    [SidedDependency(Side.Server)] private readonly SharedVerbSystem _verbs = default!;
    [SidedDependency(Side.Server)] private readonly SharedDoAfterSystem _doAfter = default!;
    [SidedDependency(Side.Server)] private readonly OpenableSystem _openable = default!;
    [SidedDependency(Side.Server)] private readonly MetaDataSystem _metaData = default!;

    private EntityCoordinates _coordinates;

    public override PoolSettings PoolSettings => new();

    public override async Task DoSetup()
    {
        await base.DoSetup();
        var map = await Pair.CreateTestMap();
        _coordinates = map.GridCoords;
    }

    [TestCase(100, 0, MobState.Alive, 5, 90)]
    [TestCase(100, 0, MobState.Critical, 5, 90)]
    [TestCase(100, 0, MobState.Dead, 0, 100)]
    [TestCase(100, 5, MobState.Alive, 5, 100)]
    [TestCase(100, 4.75, MobState.Alive, 5, 99.5)]
    [TestCase(31, 0, MobState.Alive, 5, 21)]
    [TestCase(30, 0, MobState.Alive, 0, 30)]
    [TestCase(29, 0, MobState.Alive, 0, 29)]
    [TestCase(20, 0, MobState.Alive, 0, 20)]
    public async Task ProductionChargesOnlyAcceptedMilk(
        int hunger,
        double initialMilk,
        MobState state,
        int expectedMilk,
        double expectedHunger)
    {
        EntityUid producer = default;
        await Server.WaitPost(() =>
        {
            producer = SSpawnAtPosition("MilkProducerTestFedProducer", _coordinates);
            _satiation.SetValue(SEntity<SatiationComponent>(producer), SatiationSystem.Hunger, value: hunger);
            _mobState.ChangeMobState(producer, state);
            AddReagent(producer, "Milk", FixedPoint2.New(initialMilk));
        });

        // Multiple growth intervals also catch charging hunger again while the reservoir is full.
        await RunFor(TimeSpan.FromSeconds(1));

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<SolutionComponent>(producer).Solution.Volume, Is.EqualTo(FixedPoint2.New(expectedMilk)));
            Assert.That(_satiation.GetValueOrNull(SEntity<SatiationComponent>(producer), SatiationSystem.Hunger),
                Is.EqualTo(expectedHunger).Within(0.0001));
        });
    }

    [TestCase("MilkProducerTestProducer")]
    [TestCase("MilkProducerTestProducerWithoutHunger")]
    public async Task MissingHungerDoesNotProduceMilk(string prototype)
    {
        var producer = await SpawnAtPosition(prototype, _coordinates);

        await RunFor(TimeSpan.FromSeconds(1));

        await Server.WaitAssertion(() =>
        {
            if (STryComp<SatiationComponent>(producer, out var satiation))
                Assert.That(_satiation.GetValueOrNull((producer, satiation), SatiationSystem.Hunger), Is.Null);
            Assert.That(SComp<SolutionComponent>(producer).Solution.Volume, Is.EqualTo(FixedPoint2.Zero));
        });
    }

    [Test]
    public async Task MuHumanProducesMilkInItsOwnSolutionWithoutReplacingBlood()
    {
        var producer = await SpawnAtPosition("MilkProducerTestMuHuman", _coordinates);

        await RunFor(TimeSpan.FromSeconds(0.3));

        await Server.WaitAssertion(() =>
        {
            Assert.That(_solutions.TryGetSolution(producer, "milk", out var milk), Is.True);
            Assert.That(_solutions.TryGetSolution(producer, BloodstreamComponent.DefaultBloodSolutionName, out var blood), Is.True);
            Assert.That(milk!.Value.Owner, Is.Not.EqualTo(producer), "Mu-humans must use their configured child solution.");
            Assert.That(milk.Value.Owner, Is.Not.EqualTo(blood!.Value.Owner));
            Assert.That(SComp<MetaDataComponent>(milk.Value).EntityPrototype?.ID, Is.EqualTo("SolutionMilk"));
            Assert.That(milk.Value.Comp.Solution.GetTotalPrototypeQuantity("Milk"), Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(blood.Value.Comp.Solution.GetTotalPrototypeQuantity("Milk"), Is.EqualTo(FixedPoint2.Zero));
        });
    }

    [Test]
    public async Task PausingPreservesTheRemainingGrowthDelay()
    {
        EntityUid producer = default;
        await Server.WaitPost(() =>
        {
            producer = SSpawnAtPosition("MilkProducerTestFedProducer", _coordinates);
            _metaData.SetEntityPaused(producer, true);
        });

        // The pause spans four production intervals and must not accumulate milk or spend hunger.
        await RunFor(TimeSpan.FromSeconds(1));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<SolutionComponent>(producer).Solution.Volume, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(_satiation.GetValueOrNull(SEntity<SatiationComponent>(producer), SatiationSystem.Hunger), Is.EqualTo(100));
        });

        await Server.WaitPost(() => _metaData.SetEntityPaused(producer, false));
        await RunFor(TimeSpan.FromSeconds(0.1));
        await Server.WaitAssertion(() =>
            Assert.That(SComp<SolutionComponent>(producer).Solution.Volume, Is.EqualTo(FixedPoint2.Zero),
                "Unpausing must preserve the remaining interval instead of catching up paused production."));

        await RunFor(TimeSpan.FromSeconds(0.25));
        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<SolutionComponent>(producer).Solution.Volume, Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(_satiation.GetValueOrNull(SEntity<SatiationComponent>(producer), SatiationSystem.Hunger), Is.EqualTo(90));
        });
    }

    [TestCase(0, null, 5)]
    [TestCase(0, 2, 2)]
    [TestCase(9, 4, 1)]
    public async Task MilkingRespectsAvailableVolumeAndMaxRefill(int initialWater, int? maxRefill, int expectedMilk)
    {
        var interaction = await CreateInteraction(initialWater, maxRefill);
        await StartMilking(interaction);

        // No milk changes hands until the real DoAfter completes.
        await Server.WaitAssertion(() => AssertMilk(interaction, 5, 0));
        await FinishMilking();

        await Server.WaitAssertion(() =>
        {
            AssertMilk(interaction, 5 - expectedMilk, expectedMilk);
            Assert.That(SComp<SolutionComponent>(interaction.Container).Solution.GetTotalPrototypeQuantity("Water"),
                Is.EqualTo(FixedPoint2.New(initialWater)));
            Assert.That(_satiation.GetValueOrNull(SEntity<SatiationComponent>(interaction.Producer), SatiationSystem.Hunger), Is.EqualTo(100),
                "Milking must not charge hunger a second time.");
        });
    }

    [TestCase(true, 0)]
    [TestCase(false, 10)]
    public async Task ClosedOrFullContainerCannotStartMilking(bool closed, int initialWater)
    {
        var interaction = await CreateInteraction(initialWater);
        await Server.WaitPost(() =>
        {
            _openable.SetOpen(interaction.Container, !closed);
            if (GetMilkingVerb(interaction) is { } verb)
                _verbs.ExecuteVerb(verb, interaction.User, interaction.Producer);
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(SComp<DoAfterComponent>(interaction.User).DoAfters.Values.Any(x => !x.Cancelled && !x.Completed), Is.False);
            AssertMilk(interaction, 5, 0);
        });
    }

    [TestCase(CompletionChange.CloseContainer)]
    [TestCase(CompletionChange.FillContainer)]
    [TestCase(CompletionChange.KillProducer)]
    [TestCase(CompletionChange.ReplaceHeldContainer)]
    public async Task CompletionRechecksChangedParticipants(CompletionChange change)
    {
        var interaction = await CreateInteraction();
        await StartMilking(interaction);
        EntityUid replacement = default;

        await Server.WaitPost(() =>
        {
            switch (change)
            {
                case CompletionChange.CloseContainer:
                    _openable.SetOpen(interaction.Container, false);
                    break;
                case CompletionChange.FillContainer:
                    AddReagent(interaction.Container, "Water", 10);
                    break;
                case CompletionChange.KillProducer:
                    _mobState.ChangeMobState(interaction.Producer, MobState.Dead);
                    break;
                case CompletionChange.ReplaceHeldContainer:
                    _hands.TryDrop(interaction.User, interaction.Container);
                    replacement = SSpawnAtPosition("MilkProducerTestContainer", _coordinates);
                    _hands.TryPickupAnyHand(interaction.User, replacement, animate: false);
                    break;
            }
        });

        await FinishMilking();

        await Server.WaitAssertion(() =>
        {
            AssertMilk(interaction, 5, 0);
            if (change == CompletionChange.ReplaceHeldContainer)
            {
                Assert.That(_hands.GetActiveItem(interaction.User), Is.EqualTo(replacement));
                Assert.That(SComp<SolutionComponent>(replacement).Solution.Volume, Is.EqualTo(FixedPoint2.Zero));
            }
        });
    }

    [Test]
    public async Task CancelledMilkingCanBeRetried()
    {
        var interaction = await CreateInteraction();
        await StartMilking(interaction);

        await Server.WaitPost(() =>
        {
            var active = SComp<DoAfterComponent>(interaction.User).DoAfters.Values.Single(x => !x.Cancelled && !x.Completed);
            _doAfter.Cancel(interaction.User, active.Index);
        });
        await FinishMilking();
        await Server.WaitAssertion(() => AssertMilk(interaction, 5, 0));

        await StartMilking(interaction);
        await FinishMilking();
        await Server.WaitAssertion(() => AssertMilk(interaction, 0, 5));
    }

    [Test]
    public async Task ConcurrentMilkingConservesTheSharedReservoir()
    {
        var first = await CreateInteraction(maxRefill: 4);
        MilkInteraction second = default;
        await Server.WaitPost(() => second = CreateUserAndContainer(first.Producer, maxRefill: 4));

        await StartMilking(first);
        await StartMilking(second);
        await FinishMilking();

        await Server.WaitAssertion(() =>
        {
            var firstMilk = SComp<SolutionComponent>(first.Container).Solution.GetTotalPrototypeQuantity("Milk");
            var secondMilk = SComp<SolutionComponent>(second.Container).Solution.GetTotalPrototypeQuantity("Milk");
            Assert.That(SComp<SolutionComponent>(first.Producer).Solution.Volume, Is.EqualTo(FixedPoint2.Zero));
            Assert.That(firstMilk + secondMilk, Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(firstMilk, Is.GreaterThan(FixedPoint2.Zero).And.LessThanOrEqualTo(FixedPoint2.New(4)));
            Assert.That(secondMilk, Is.GreaterThan(FixedPoint2.Zero).And.LessThanOrEqualTo(FixedPoint2.New(4)));
        });
    }

    private async Task<MilkInteraction> CreateInteraction(int initialWater = 0, int? maxRefill = null)
    {
        MilkInteraction interaction = default;
        await Server.WaitPost(() =>
        {
            var producer = SSpawnAtPosition("MilkProducerTestSource", _coordinates);
            AddReagent(producer, "Milk", 5);
            interaction = CreateUserAndContainer(producer, initialWater, maxRefill);
        });

        await Server.WaitAssertion(() =>
            Assert.That(_hands.GetActiveItem(interaction.User), Is.EqualTo(interaction.Container)));
        return interaction;
    }

    private MilkInteraction CreateUserAndContainer(EntityUid producer, int initialWater = 0, int? maxRefill = null)
    {
        var user = SSpawnAtPosition("MilkProducerTestUser", _coordinates);
        var container = SSpawnAtPosition("MilkProducerTestContainer", _coordinates);
        SComp<RefillableSolutionComponent>(container).MaxRefill = maxRefill.HasValue ? FixedPoint2.New(maxRefill.Value) : null;
        AddReagent(container, "Water", initialWater);
        _hands.TryPickupAnyHand(user, container, animate: false);
        return new MilkInteraction(producer, user, container);
    }

    private async Task StartMilking(MilkInteraction interaction)
    {
        Verb? verb = null;
        await Server.WaitPost(() => verb = GetMilkingVerb(interaction));
        Assert.That(verb, Is.Not.Null, "A nearby user holding an open refillable container must be able to milk.");

        await Server.WaitPost(() => _verbs.ExecuteVerb(verb!, interaction.User, interaction.Producer));
        await Server.WaitAssertion(() =>
            Assert.That(SComp<DoAfterComponent>(interaction.User).DoAfters.Values.Count(x => !x.Cancelled && !x.Completed),
                Is.EqualTo(1)));
    }

    private Verb? GetMilkingVerb(MilkInteraction interaction)
    {
        var text = Loc.GetString("milk-producer-verb-milk");
        return _verbs.GetLocalVerbs(interaction.Producer, interaction.User, typeof(AlternativeVerb))
            .SingleOrDefault(verb => verb.Text == text);
    }

    private void AddReagent(EntityUid owner, string reagent, FixedPoint2 quantity)
    {
        _solutions.TryAddReagent(SEntity<SolutionComponent>(owner), reagent, quantity);
    }

    private void AssertMilk(MilkInteraction interaction, int sourceMilk, int containerMilk)
    {
        Assert.That(SComp<SolutionComponent>(interaction.Producer).Solution.GetTotalPrototypeQuantity("Milk"),
            Is.EqualTo(FixedPoint2.New(sourceMilk)));
        Assert.That(SComp<SolutionComponent>(interaction.Container).Solution.GetTotalPrototypeQuantity("Milk"),
            Is.EqualTo(FixedPoint2.New(containerMilk)));
    }

    private Task FinishMilking() => RunFor(TimeSpan.FromSeconds(1));

    private Task RunFor(TimeSpan duration)
    {
        var ticks = (int) Math.Ceiling(duration / SGameTiming.TickPeriod) + 1;
        return Pair.RunTicksSync(ticks);
    }

    public enum CompletionChange : byte
    {
        CloseContainer,
        FillContainer,
        KillProducer,
        ReplaceHeldContainer,
    }

    private readonly record struct MilkInteraction(EntityUid Producer, EntityUid User, EntityUid Container);
}
