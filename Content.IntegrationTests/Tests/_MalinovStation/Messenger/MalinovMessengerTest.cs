using Content.IntegrationTests.Fixtures;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.CartridgeLoader;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._MalinovStation.Messenger;

[TestFixture]
[TestOf(typeof(MalinovMessengerCartridgeComponent))]
public sealed class MalinovMessengerTest : GameTest
{
    [Test]
    public async Task MessengerProgramAutoInstalledInPassengerPda()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var cartridgeLoaderSystem = entityManager.EntitySysManager.GetEntitySystem<CartridgeLoaderSystem>();

        await pair.CreateTestMap();
        var coords = pair.TestMap!.GridCoords;

        EntityUid pda = default;

        await server.WaitAssertion(() =>
        {
            pda = entityManager.SpawnEntity("PassengerPDA", coords);

            Assert.That(entityManager.TryGetComponent(pda, out CartridgeLoaderComponent loader), Is.True);
            Assert.That(cartridgeLoaderSystem.HasProgram<MalinovMessengerCartridgeComponent>((pda, loader)), Is.True,
                "Messenger program should be auto-installed in PassengerPDA");
        });

        await server.WaitRunTicks(2);
        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerProgramNotInstalledInCentcomPda()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var cartridgeLoaderSystem = entityManager.EntitySysManager.GetEntitySystem<CartridgeLoaderSystem>();

        await pair.CreateTestMap();
        var coords = pair.TestMap!.GridCoords;

        EntityUid pda = default;

        await server.WaitAssertion(() =>
        {
            pda = entityManager.SpawnEntity("CentcomPDA", coords);

            Assert.That(entityManager.TryGetComponent(pda, out CartridgeLoaderComponent loader), Is.True);
            Assert.That(cartridgeLoaderSystem.HasProgram<MalinovMessengerCartridgeComponent>((pda, loader)), Is.False,
                "Messenger program should not be auto-installed in CentcomPDA");
        });

        await server.WaitRunTicks(2);
        await server.WaitIdleAsync();
    }
}
