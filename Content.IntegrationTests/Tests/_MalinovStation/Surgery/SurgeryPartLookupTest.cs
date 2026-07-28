using Content.IntegrationTests.Fixtures;
using Content.Server._MalinovStation.Surgery;
using Content.Shared.Body;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._MalinovStation.Surgery;

[TestFixture]
[TestOf(typeof(SurgerySystem))]
public sealed class SurgeryPartLookupTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestSurgeryLookupBody
  components:
  - type: Body

- type: entity
  id: TestSurgeryLookupTorso
  components:
  - type: Organ
    category: Torso
  - type: BodyPart
    category: Torso

- type: entity
  id: TestSurgeryLookupArm
  components:
  - type: Organ
    category: ArmLeft
  - type: BodyPart
    category: ArmLeft

- type: entity
  id: TestSurgeryLookupHeart
  components:
  - type: Organ
    category: Heart
";

    [Test]
    public async Task FindsPartsAndInstallContainers()
    {
        var pair = Pair;
        var server = pair.Server;

        await server.WaitIdleAsync();

        var entityManager = server.ResolveDependency<IEntityManager>();
        var mapData = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var container = entityManager.System<SharedContainerSystem>();
            var surgery = entityManager.System<SurgerySystem>();

            var body = entityManager.SpawnEntity("TestSurgeryLookupBody", mapData.GridCoords);
            var bodyOrgans = container.GetContainer(body, BodyComponent.ContainerID);

            var torso = entityManager.SpawnEntity("TestSurgeryLookupTorso", mapData.GridCoords);
            container.Insert(torso, bodyOrgans);

            var arm = entityManager.SpawnEntity("TestSurgeryLookupArm", mapData.GridCoords);
            container.Insert(arm, bodyOrgans);

            var torsoOrgans = container.GetContainer(torso, BodyPartComponent.ContainerID);
            var heart = entityManager.SpawnEntity("TestSurgeryLookupHeart", mapData.GridCoords);
            container.Insert(heart, torsoOrgans);

            // A leaf organ resolves to its owning structural part.
            Assert.That(surgery.TryFindPart(body, "Torso", out var foundTorso), Is.True);
            Assert.That(foundTorso, Is.EqualTo(torso));

            // Installing a heart should go inside the torso's own container, not flat on the body.
            Assert.That(surgery.TryGetInstallContainer(body, "Heart", out var heartContainer), Is.True);
            Assert.That(heartContainer, Is.EqualTo(torsoOrgans));

            // Installing a whole limb (itself a structural part) goes straight into the body's container.
            Assert.That(surgery.TryGetInstallContainer(body, "ArmLeft", out var armInstallContainer), Is.True);
            Assert.That(armInstallContainer, Is.EqualTo(bodyOrgans));

            // No leg was ever attached - nowhere to install a foot into.
            Assert.That(surgery.TryGetInstallContainer(body, "FootLeft", out _), Is.False);
        });
    }
}
