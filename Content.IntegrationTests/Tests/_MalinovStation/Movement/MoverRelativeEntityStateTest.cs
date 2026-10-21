#nullable enable
using Content.IntegrationTests.Fixtures;
using Content.Shared.Movement.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._MalinovStation.Movement;

/// <summary>
/// Heisentest #49: a grid can be deleted while a mover still uses it as <see cref="InputMoverComponent.RelativeEntity"/>.
/// Serializing the mover state must not log a missing entity, which fails the test pair.
/// </summary>
public sealed class MoverRelativeEntityStateTest : GameTest
{
    [Test]
    public async Task DeletedRelativeEntityIsSentAsNull()
    {
        var map = await Pair.CreateTestMap();
        EntityUid mover = default;
        InputMoverComponentState? state = null;

        await Server.WaitPost(() =>
        {
            mover = SEntMan.SpawnEntity(null, map.GridCoords);
            var relative = SEntMan.SpawnEntity(null, map.GridCoords);
            SEntMan.EnsureComponent<InputMoverComponent>(mover).RelativeEntity = relative;
            SEntMan.DeleteEntity(relative);
        });

        // Get the state the same way PVS does when it sends the mover to a player.
        await Server.WaitPost(() =>
        {
            var comp = SEntMan.GetComponent<InputMoverComponent>(mover);
            state = (InputMoverComponentState?) SEntMan.GetComponentState(SEntMan.EventBus, comp, null, GameTick.Zero);
        });

        await Server.WaitAssertion(() =>
        {
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.RelativeEntity, Is.Null);
        });

        await Server.WaitPost(() => SEntMan.DeleteEntity(mover));
    }
}
