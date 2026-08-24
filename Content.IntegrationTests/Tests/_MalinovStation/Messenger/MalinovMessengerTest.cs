using Content.IntegrationTests.Fixtures;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.CartridgeLoader;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._MalinovStation.Messenger;

[TestFixture]
[TestOf(typeof(MalinovMessengerCartridgeComponent))]
public sealed class MalinovMessengerTest : GameTest
{
    [Test]
    public async Task InstallMessengerCartridgeIntoPda()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var cartridgeLoaderSystem = entityManager.EntitySysManager.GetEntitySystem<CartridgeLoaderSystem>();

        EntityUid pda = default;
        EntityUid cartridge = default;

        await server.WaitAssertion(() =>
        {
            pda = entityManager.SpawnEntity("PassengerPDA", MapCoordinates.Nullspace);
            cartridge = entityManager.SpawnEntity("MalinovMessengerCartridge", MapCoordinates.Nullspace);

            Assert.That(entityManager.TryGetComponent(pda, out CartridgeLoaderComponent loader), Is.True);

            var installed = cartridgeLoaderSystem.InstallCartridge((pda, loader), cartridge);
            Assert.That(installed, Is.True, "Failed to install MalinovMessengerCartridge into PDA");

            var hasProgram = cartridgeLoaderSystem.HasProgram<MalinovMessengerCartridgeComponent>((pda, loader));
            Assert.That(hasProgram, Is.True, "PDA does not have MalinovMessengerCartridgeComponent program");
        });

        await server.WaitRunTicks(2);
        await server.WaitIdleAsync();
    }
}
