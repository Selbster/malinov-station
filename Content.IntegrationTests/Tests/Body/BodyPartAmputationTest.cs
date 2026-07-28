using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Body.Components;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Body;

[TestFixture]
[TestOf(typeof(BodyPartThresholdSystem))]
public sealed class BodyPartAmputationTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestAmputationBody
  components:
  - type: Body
  - type: Bloodstream
    bloodlossDamage:
      types:
        Bloodloss: 0.5
    bloodlossHealDamage:
      types:
        Bloodloss: -1
  - type: EntityTableContainerFill
    containers:
      body_organs: !type:AllSelector
        children:
        - id: TestAmputationArm

- type: entity
  id: TestAmputationArm
  components:
  - type: Organ
  - type: BodyPart
  - type: Damageable
  - type: Injurable
    damageContainer: Biological
  - type: BodyPartThresholds
    amputationThreshold: 10
    amputationBleedAmount: 5
";

    [Test]
    public async Task DamagedPartIsSevered()
    {
        var pair = Pair;
        var server = pair.Server;

        await server.WaitIdleAsync();

        var entityManager = server.ResolveDependency<IEntityManager>();
        var mapData = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var container = entityManager.System<SharedContainerSystem>();
            var damageable = entityManager.System<DamageableSystem>();

            var body = entityManager.SpawnEntity("TestAmputationBody", mapData.GridCoords);
            var bodyOrgans = container.GetContainer(body, BodyComponent.ContainerID);
            var arm = bodyOrgans.ContainedEntities[0];
            var bloodstream = entityManager.GetComponent<BloodstreamComponent>(body);

            Assert.That(entityManager.GetComponent<BodyPartComponent>(arm).Body, Is.EqualTo(body));

            var damage = new DamageSpecifier();
            damage.DamageDict.Add("Blunt", FixedPoint2.New(15));
            damageable.TryChangeDamage(arm, damage, ignoreResistances: true);

            Assert.That(bodyOrgans.ContainedEntities, Does.Not.Contain(arm));
            Assert.That(entityManager.GetComponent<BodyPartComponent>(arm).Body, Is.Null);
            Assert.That(damageable.GetTotalDamage(arm), Is.EqualTo(FixedPoint2.Zero));
            Assert.That(bloodstream.BleedAmount, Is.EqualTo(5f));
        });
    }
}
