using System.Numerics;
using System.Reflection;
using Content.IntegrationTests.Fixtures;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Events;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._MalinovStation.Shuttles;

public sealed class MalinovFTLValidationTest : GameTest
{
    public override PoolSettings PoolSettings => new() { Connected = true, Dirty = true };

    [Test]
    public async Task InvalidDestinationDoesNotStartFTLAndValidDestinationStillWorks()
    {
        var map = await Pair.CreateTestMap();
        await Server.WaitAssertion(() =>
        {
            var shuttle = SEntMan.GetComponent<ShuttleComponent>(map.Grid);
            var system = SEntMan.System<ShuttleSystem>();
            var origin = SEntMan.GetComponent<TransformComponent>(map.Grid).Coordinates;
            var validCoordinates = new EntityCoordinates(origin.EntityId, new Vector2(50, 50));
            var invalidCoordinates = new[]
            {
                new Vector2(float.NaN, 0),
                new Vector2(0, float.NaN),
                new Vector2(float.PositiveInfinity, 0),
                new Vector2(0, float.NegativeInfinity),
            };

            foreach (var position in invalidCoordinates)
            {
                system.FTLToCoordinates(map.Grid, shuttle, new EntityCoordinates(origin.EntityId, position), Angle.Zero);
                Assert.That(SEntMan.HasComponent<FTLComponent>(map.Grid), Is.False);
            }

            foreach (var angle in new[] { double.NaN, double.PositiveInfinity, double.NegativeInfinity })
            {
                system.FTLToCoordinates(map.Grid, shuttle, validCoordinates, new Angle(angle));
                Assert.That(SEntMan.HasComponent<FTLComponent>(map.Grid), Is.False);
            }

            system.FTLToCoordinates(map.Grid, shuttle, EntityCoordinates.Invalid, Angle.Zero);
            Assert.That(SEntMan.HasComponent<FTLComponent>(map.Grid), Is.False);
            Assert.That(SEntMan.GetComponent<TransformComponent>(map.Grid).Coordinates, Is.EqualTo(origin));

            system.FTLToCoordinates(map.Grid, shuttle, validCoordinates, Angle.Zero);
            var ftl = SEntMan.GetComponent<FTLComponent>(map.Grid);
            Assert.That(ftl.TargetCoordinates, Is.EqualTo(validCoordinates));
            Assert.That(ftl.TargetAngle, Is.EqualTo(Angle.Zero));
        });
    }

    [Test]
    public async Task ConsoleRejectsUnknownMapBeforeLookingUpShuttle()
    {
        await Server.WaitAssertion(() =>
        {
            var system = SEntMan.System<ShuttleConsoleSystem>();
            var handler = typeof(ShuttleConsoleSystem).GetMethod("OnPositionFTLMessage", BindingFlags.Instance | BindingFlags.NonPublic)!;
            var message = new ShuttleConsoleFTLPositionMessage
            {
                Coordinates = new MapCoordinates(Vector2.Zero, new MapId(int.MaxValue)),
                Angle = Angle.Zero,
            };

            Assert.DoesNotThrow(() => handler.Invoke(system, new object[] { default(Entity<ShuttleConsoleComponent>), message }));
        });
    }
}
