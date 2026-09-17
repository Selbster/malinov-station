using System;
using System.Numerics;
using Content.Shared._MalinovStation.Shuttles;
using NUnit.Framework;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.Tests._MalinovStation.Shuttles;

[TestFixture]
public sealed class MalinovFTLValidationTest
{
    [TestCase(double.NaN)]
    [TestCase(double.PositiveInfinity)]
    [TestCase(double.NegativeInfinity)]
    public void RejectsNonFiniteAngles(double angle)
    {
        Assert.That(MalinovFTLValidation.IsFinite(Vector2.Zero, new Angle(angle)), Is.False);
    }

    [TestCase(float.NaN)]
    [TestCase(float.PositiveInfinity)]
    [TestCase(float.NegativeInfinity)]
    public void RejectsNonFiniteCoordinatesAndCenter(float value)
    {
        Assert.Multiple(() =>
        {
            Assert.That(MalinovFTLValidation.IsFinite(new Vector2(value, 0), Angle.Zero), Is.False);
            Assert.That(MalinovFTLValidation.IsFinite(new Vector2(0, value), Angle.Zero), Is.False);
            Assert.That(MalinovFTLValidation.TryAdjustCoordinates(
                new EntityCoordinates(default, Vector2.Zero), Angle.Zero, new Vector2(value, 0), out _), Is.False);
        });
    }

    [Test]
    public void RejectsOverflowFromFiniteInputs()
    {
        var coordinates = new EntityCoordinates(default, new Vector2(float.MaxValue, 0));
        Assert.That(MalinovFTLValidation.TryAdjustCoordinates(
            coordinates, Angle.Zero, new Vector2(-float.MaxValue, 0), out _), Is.False);
    }

    [Test]
    public void PreservesCenterOfMassAdjustment()
    {
        var coordinates = new EntityCoordinates(default, new Vector2(10, 20));
        Assert.That(MalinovFTLValidation.TryAdjustCoordinates(
            coordinates, new Angle(Math.PI / 2), new Vector2(2, 0), out var adjusted), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(adjusted.EntityId, Is.EqualTo(coordinates.EntityId));
            Assert.That(adjusted.X, Is.EqualTo(10).Within(0.0001));
            Assert.That(adjusted.Y, Is.EqualTo(18).Within(0.0001));
        });
    }
}
