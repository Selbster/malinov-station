#nullable enable
using System.Numerics;
using System.Collections.Generic;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.Power.Components;
using Content.Shared.DoAfter;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Doors.Components;
using Content.Shared.Doors.Systems;
using Content.Shared.Hands.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

public sealed partial class AiJourneyLifecycleTests
{
    [Test]
    public async Task RestoredDoorAccess_ClearsCachedDenial_AndAllowsTravel()
    {
        await Prepare();
        EntityUid door = default;
        await Server.WaitPost(() =>
        {
            var coords = _origin.Offset(new Vector2(-6, 0));
            door = Server.EntMan.SpawnEntity("TestJourneyAirlock", coords);
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(Vector2.UnitY));
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(-Vector2.UnitY));
            Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
            Server.System<AccessReaderSystem>().TrySetAccesses(
                (door, Server.EntMan.GetComponent<AccessReaderComponent>(door)),
                new List<ProtoId<AccessLevelPrototype>> { "Engineering" });
        });
        await Until(() => Server.EntMan.GetComponent<DoorApproachComponent>(_actor).DeniedDoors.ContainsKey(door),
            "The inaccessible door was not recorded.");
        await Server.WaitPost(() => Server.System<AccessReaderSystem>().TrySetAccesses(
            (door, Server.EntMan.GetComponent<AccessReaderComponent>(door)), new List<ProtoId<AccessLevelPrototype>>()));
        await Until(() => !Server.EntMan.GetComponent<DoorApproachComponent>(_actor).DeniedDoors.ContainsKey(door),
            "The restored access did not release the cached denial.");
        await Server.WaitPost(() => Move(12));
        await Until(() => Busy.CurrentAction is null, "The now accessible route did not finish.");
        await Server.WaitAssertion(() => Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed)));
    }

    [TestCase("power")]
    [TestCase("tool")]
    [TestCase("replace")]
    [TestCase("bolts")]
    [TestCase("weld")]
    public async Task Prying_ChangedConditions_ReleaseOldOperationAndAllowFurtherTravel(string change)
    {
        await Prepare();
        EntityUid door = default;
        EntityUid crowbar = default;
        await Server.WaitPost(() =>
        {
            var coords = _origin.Offset(new Vector2(-6, 0));
            door = Server.EntMan.SpawnEntity("TestJourneyAirlock", coords);
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(Vector2.UnitY));
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(-Vector2.UnitY));
            crowbar = Server.EntMan.SpawnEntity("Crowbar", _origin);
            Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(_actor, crowbar), Is.True);
        });
        await Pair.RunTicksSync(15);
        await Server.WaitPost(() => Move(12));
        await Until(() => Server.EntMan.GetComponent<DoorApproachComponent>(_actor).PryDoAfter is not null,
            "The passenger never began prying.");
        long old = 0;
        DoAfterId? pry = null;
        await Server.WaitPost(() =>
        {
            old = Busy.ExecutionId;
            pry = Server.EntMan.GetComponent<DoorApproachComponent>(_actor).PryDoAfter;
            switch (change)
            {
                case "power":
                    Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
                    break;
                case "tool":
                    Assert.That(Server.System<SharedHandsSystem>().TryDrop(_actor, crowbar), Is.True);
                    break;
                case "replace":
                    Move(1);
                    break;
                case "bolts":
                    // Bolts need electricity to engage; restore power before issuing the real bolt operation.
                    Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).Powered = true;
                    Server.System<SharedDoorSystem>().SetBoltsDown(
                        (door, Server.EntMan.GetComponent<DoorBoltComponent>(door)), true);
                    Assert.That(Server.EntMan.GetComponent<DoorBoltComponent>(door).BoltsDown, Is.True);
                    Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).Powered = false;
                    break;
                case "weld":
                    Server.System<SharedDoorSystem>().SetState(door, DoorState.Welded);
                    break;
            }
        });
        // A changed condition should release the owned do-after promptly, not wait for the 25s pry timeout.
        await Until(() => Server.EntMan.GetComponent<DoorApproachComponent>(_actor).PryDoAfter is null,
            "The obsolete prying operation was retained.", iterations: 30);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<SharedDoAfterSystem>().GetStatus(pry), Is.Not.EqualTo(DoAfterStatus.Running));
        });
        if (change == "power")
        {
            await Until(() => Busy.CurrentAction is null, "The powered door never allowed arrival.");
            await Server.WaitAssertion(() => Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed)));
            return;
        }

        if (change != "replace")
        {
            await Until(() => Busy.CurrentAction is null, "The blocked journey remained active.");
            await Server.WaitPost(() => Move(1));
        }
        await Until(() => Busy.CurrentAction is null, "A new reachable route did not complete.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed));
            Assert.That(Trace.GetExecution(_actor, old)!.Result?.Outcome, Is.Not.EqualTo(AiActionOutcome.Completed));
            Assert.That(Server.EntMan.GetComponent<DoorComponent>(door).State, Is.Not.EqualTo(DoorState.Open));
        });
    }
}
