using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Body;

[TestFixture]
[TestOf(typeof(BodySystem))]
public sealed class BodyPartTest : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: TestPartBody
  components:
  - type: Body

- type: entity
  id: TestBodyPart
  components:
  - type: Organ
  - type: BodyPart

- type: entity
  id: TestNestedOrgan
  components:
  - type: Organ
";

    [Test]
    public async Task NestedOrganReachesTrueBody()
    {
        var pair = Pair;
        var server = pair.Server;

        await server.WaitIdleAsync();

        var entityManager = server.ResolveDependency<IEntityManager>();
        var mapData = await pair.CreateTestMap();

        await server.WaitAssertion(() =>
        {
            var container = entityManager.System<SharedContainerSystem>();
            var bodySystem = entityManager.System<BodySystem>();

            var body = entityManager.SpawnEntity("TestPartBody", mapData.GridCoords);
            var bodyOrgans = container.GetContainer(body, BodyComponent.ContainerID);

            // Assembled off-body first (organ inserted into the part before the part joins the body),
            // then the whole assembly is inserted at once.
            var partA = entityManager.SpawnEntity("TestBodyPart", mapData.GridCoords);
            var partAOrgans = container.GetContainer(partA, BodyPartComponent.ContainerID);
            var organA = entityManager.SpawnEntity("TestNestedOrgan", mapData.GridCoords);
            container.Insert(organA, partAOrgans);

            Assert.That(entityManager.GetComponent<OrganComponent>(organA).Body, Is.Null);

            container.Insert(partA, bodyOrgans);

            Assert.That(entityManager.GetComponent<OrganComponent>(organA).Body, Is.EqualTo(body));
            Assert.That(bodySystem.EnumerateOrgans(body), Does.Contain(partA));
            Assert.That(bodySystem.EnumerateOrgans(body), Does.Contain(organA));

            // Part attached to the body first (empty), organ inserted into it afterward.
            var partB = entityManager.SpawnEntity("TestBodyPart", mapData.GridCoords);
            container.Insert(partB, bodyOrgans);

            var partBOrgans = container.GetContainer(partB, BodyPartComponent.ContainerID);
            var organB = entityManager.SpawnEntity("TestNestedOrgan", mapData.GridCoords);
            container.Insert(organB, partBOrgans);

            Assert.That(entityManager.GetComponent<OrganComponent>(organB).Body, Is.EqualTo(body));
            Assert.That(bodySystem.EnumerateOrgans(body), Does.Contain(organB));

            // Removing a part from the body detaches its nested organ too.
            container.Remove(partA, bodyOrgans);

            Assert.That(entityManager.GetComponent<OrganComponent>(organA).Body, Is.Null);
            Assert.That(bodySystem.EnumerateOrgans(body), Does.Not.Contain(organA));
        });
    }
}
