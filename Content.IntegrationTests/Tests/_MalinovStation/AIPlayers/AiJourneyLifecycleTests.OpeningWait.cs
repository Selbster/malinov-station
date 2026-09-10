#nullable enable
using System;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.Power.Components;
using Content.Server.NPC.Components;
using Content.Shared.Doors.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Timing;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

public sealed partial class AiJourneyLifecycleTests
{
    [Test]
    public async Task RejectedDoorActivation_RetriesAfterDelay_WithoutDenyingTheRoute()
    {
        await Prepare(human: true);
        EntityUid door = default;
        await Server.WaitPost(() =>
        {
            var coords = _origin.Offset(new Vector2(-6, 0));
            door = Server.EntMan.SpawnEntity("TestJourneyAirlock", coords);
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(Vector2.UnitY));
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(-Vector2.UnitY));
            Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
        });
        await Pair.RunTicksSync(60);
        await Server.WaitPost(() => Move(12));
        await Until(() => Server.EntMan.GetComponent<DoorApproachComponent>(_actor).ActiveDoor == door,
            "The bot did not approach the door.");
        await Server.WaitPost(() =>
        {
            Server.System<UseDelaySystem>().SetLength(door, TimeSpan.FromSeconds(1.2));
            Server.System<UseDelaySystem>().TryResetDelay(door);
        });
        await Until(() => Server.EntMan.GetComponent<DoorApproachComponent>(_actor).NextActivateAttempt > TimeSpan.Zero,
            "The bot did not attempt activation.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.EntMan.GetComponent<DoorApproachComponent>(_actor).LastAttemptAt, Is.Null);
            Assert.That(Busy.CurrentAction, Is.EqualTo("MoveTo"));
        });
        await Until(() => Busy.CurrentAction is null, "The delayed door left its journey stuck.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed));
            Assert.That(Server.EntMan.GetComponent<DoorApproachComponent>(_actor).DeniedDoors.ContainsKey(door), Is.False);
        });
    }

    [Test]
    public async Task DoorOpenedExternally_RefreshesRouteAndReachesDestination()
    {
        await Prepare(human: true);
        EntityUid door = default;
        await Server.WaitPost(() =>
        {
            var coords = _origin.Offset(new Vector2(-14, 0));
            door = Server.EntMan.SpawnEntity("TestJourneyAirlock", coords);
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(Vector2.UnitY));
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(-Vector2.UnitY));
            Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
        });
        await Pair.RunTicksSync(60);
        await Server.WaitPost(() => Move(22));
        await Until(() => Server.EntMan.TryGetComponent<NPCSteeringComponent>(_actor, out var steering) &&
            steering.CurrentPath.Count > 0, "No initial route through the door was built.");
        await Server.WaitAssertion(() => Assert.That(
            Server.System<Content.Shared.Doors.Systems.SharedDoorSystem>().TryOpen(door), Is.True));
        await Until(() => Busy.CurrentAction is null, "Opening the door externally left the journey stuck.");
        await Server.WaitAssertion(() => Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed)));
    }

    private async Task<EntityUid> StartOpeningDoor(bool slow = false)
    {
        EntityUid door = default;
        await Server.WaitPost(() =>
        {
            var coords = _origin.Offset(new Vector2(-6, 0));
            door = Server.EntMan.SpawnEntity("TestJourneyAirlock", coords);
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(Vector2.UnitY));
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(-Vector2.UnitY));
            Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
            if (slow)
                Server.EntMan.GetComponent<DoorComponent>(door).OpenTimeTwo = TimeSpan.FromSeconds(1.5);
        });
        await Pair.RunTicksSync(60);
        await Server.WaitPost(() => Move(12));
        await Until(() => Server.EntMan.GetComponent<DoorApproachComponent>(_actor).OpeningWaitUntil is not null,
            "The bot did not enter the opening wait.");
        return door;
    }

    [TestCase(true, false)]
    [TestCase(false, false)]
    [TestCase(true, true)]
    public async Task OpeningDoor_BotWaitsOneSecond_ThenPassesWithoutBackingAway(bool cognitive, bool slow)
    {
        await Prepare(cognitive);
        var door = await StartOpeningDoor(slow);
        var deadline = TimeSpan.Zero;
        var stoppedAt = Vector2.Zero;
        await Server.WaitPost(() =>
        {
            deadline = Server.EntMan.GetComponent<DoorApproachComponent>(_actor).OpeningWaitUntil!.Value;
            stoppedAt = Server.EntMan.GetComponent<TransformComponent>(_actor).Coordinates.Position;
        });
        var pausedTicks = 0;
        var completed = false;
        var furthestX = stoppedAt.X;
        var lastNavigation = string.Empty;
        for (var i = 0; i < 300 && !completed; i++)
        {
            await Pair.RunTicksSync(1);
            await Server.WaitAssertion(() =>
            {
                var position = Server.EntMan.GetComponent<TransformComponent>(_actor).Coordinates.Position;
                if (Server.Timing.CurTime < deadline || Server.EntMan.GetComponent<DoorComponent>(door).State == DoorState.Opening)
                {
                    pausedTicks++;
                    Assert.That(Server.EntMan.GetComponent<DoorApproachComponent>(_actor).ActiveDoor, Is.EqualTo(door));
                    Assert.That(Server.EntMan.GetComponent<InputMoverComponent>(_actor).CurTickSprintMovement, Is.EqualTo(Vector2.Zero));
                    Assert.That(Vector2.Distance(position, stoppedAt), Is.LessThan(0.3f));
                }
                Server.EntMan.TryGetComponent<NPCSteeringComponent>(_actor, out var steering);
                if (steering is not null)
                    lastNavigation = $"status={steering.Status}, failedPaths={steering.FailedPathCount}, " +
                        $"path={string.Join(';', steering.CurrentPath.Take(4).Select(p => $"{p.Coordinates}:{p.Data.Flags}"))}";
                Assert.That(position.X, Is.LessThanOrEqualTo(furthestX + 0.2f),
                    $"The bot backed away. Start={stoppedAt}, now={position}, door={Server.EntMan.GetComponent<DoorComponent>(door).State}, " +
                    $"wait={Server.EntMan.GetComponent<DoorApproachComponent>(_actor).OpeningWaitUntil}, " +
                    $"path={(steering is null ? "finished" : string.Join(';', steering.CurrentPath.Take(4).Select(p => p.Coordinates)))}");
                furthestX = MathF.Min(furthestX, position.X);
                completed = Busy.CurrentAction is null;
            });
        }
        await Server.WaitAssertion(() =>
        {
            Assert.That(pausedTicks, Is.GreaterThan(15));
            Assert.That(completed, Is.True);
            Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed), lastNavigation);
            Assert.That(Server.EntMan.GetComponent<DoorApproachComponent>(_actor).OpeningWaitUntil, Is.Null);
        });
    }

    [Test]
    public async Task OpeningWait_ChangingDestinationCancelsPauseAndCompletesNewRoute()
    {
        await Prepare();
        await StartOpeningDoor();
        await Server.WaitAssertion(() =>
        {
            var old = Busy.ExecutionId;
            Move(1);
            Assert.That(Server.EntMan.GetComponent<DoorApproachComponent>(_actor).OpeningWaitUntil, Is.Null);
            Assert.That(Trace.GetExecution(_actor, old)!.Result?.Outcome, Is.EqualTo(AiActionOutcome.Cancelled));
        });
        await Until(() => Busy.CurrentAction is null, "Changing the route left the bot waiting at the old door.");
        await Server.WaitAssertion(() => Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed)));
    }
}
