using Content.IntegrationTests.Fixtures;
using Robust.Client.GameObjects;
using Robust.Client.Timing;
using Robust.Shared;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._MalinovStation.Containers;

public sealed class ContainerExpectedEntityTest : GameTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ExpectedSlotReservesSpaceUntilEntityArrives(bool applyingState)
    {
        var map = await Pair.CreateTestMap();
        var serverContainers = Server.System<SharedContainerSystem>();
        var clientContainers = Client.System<ContainerSystem>();
        EntityUid owner = default;
        EntityUid expected = default;
        EntityUid other = default;

        await Server.WaitPost(() =>
        {
            Server.CfgMan.SetCVar(CVars.NetPVS, true);
            owner = SEntMan.SpawnEntity(null, map.GridCoords);
            expected = SEntMan.SpawnEntity(null, map.GridCoords);
            other = SEntMan.SpawnEntity(null, map.GridCoords);
            var slot = serverContainers.EnsureContainer<ContainerSlot>(owner, "slot");
            serverContainers.Insert(expected, slot);
            Server.System<SharedVisibilitySystem>().AddLayer(expected, 10);
            SEntMan.AddComponent<EyeComponent>(owner);
            Server.PlayerMan.SetAttachedEntity(Pair.Player!, owner);
            Server.PlayerMan.JoinGame(Pair.Player!);
        });
        await Pair.RunTicksSync(10);
        await Pair.SyncTicks();
        var clientOwner = Pair.ToClientUid(owner);
        var clientOther = Pair.ToClientUid(other);
        var expectedNet = SEntMan.GetNetEntity(expected);
        ContainerSlot clientSlot = null!;
        EntityCoordinates otherCoordinates = default;
        await Client.WaitAssertion(() =>
        {
            clientSlot = (ContainerSlot) clientContainers.GetContainer(clientOwner, "slot");
            otherCoordinates = Client.Transform(clientOther).Coordinates;
            Assert.That(clientSlot.ContainedEntity, Is.Null);
            Assert.That(clientSlot.ExpectedEntities, Does.Contain(expectedNet));
            Assert.That(CEntMan.TryGetEntity(expectedNet, out _), Is.False);
            Assert.That(clientContainers.CanInsert(clientOther, clientSlot, assumeEmpty: true), Is.True);
        });

        // State-application side effects are not recorded for prediction rollback.
        // A conflicting insertion must be rejected before the missing server item arrives.
        var inserted = false;
        await Client.WaitPost(() =>
        {
            if (applyingState)
            {
                using var _ = Client.Resolve<IClientGameTiming>().StartStateApplicationArea();
                inserted = clientContainers.Insert(clientOther, clientSlot);
            }
            else
            {
                inserted = clientContainers.Insert(clientOther, clientSlot);
            }
        });

        await Server.WaitPost(() => Server.System<SharedVisibilitySystem>().RemoveLayer(expected, 10));
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            var clientExpected = CEntMan.GetEntity(expectedNet);
            Assert.That(clientSlot.ContainedEntity, Is.EqualTo(clientExpected));
            Assert.That(clientSlot.ExpectedEntities, Is.Empty);
            Assert.That(clientContainers.ExpectedEntities.ContainsKey(expectedNet), Is.False);
            Assert.That(inserted, Is.False);
            Assert.That(CEntMan.EntityExists(clientOther), Is.True);
            Assert.That(Client.Transform(clientOther).Coordinates, Is.EqualTo(otherCoordinates));
            Assert.That(Client.MetaData(clientOther).Flags.HasFlag(MetaDataFlags.InContainer), Is.False);
        });

        await Server.WaitPost(() => serverContainers.Remove(expected, serverContainers.GetContainer(owner, "slot")));
        await Pair.RunTicksSync(10);
        await Client.WaitAssertion(() =>
        {
            Assert.That(clientSlot.ContainedEntity, Is.Null);
            Assert.That(clientSlot.ExpectedEntities, Is.Empty);
            Assert.That(clientContainers.CanInsert(clientOther, clientSlot), Is.True);
        });
    }
}
