using System.Numerics;
using Content.Shared.Humanoid;
using NUnit.Framework;
using Robust.Shared.Maths;

namespace Content.Tests.Shared._MalinovStation.Humanoid;

[TestFixture]
public sealed class HumanSkinColorationTest
{
    [Test]
    public void HumanSkinTonesRemainValidAfterProfileQuantization()
    {
        ISkinColorationStrategy strategy = new HumanTonedSkinColoration();

        for (var i = 0; i <= 10000; i++)
        {
            var tone = i / 100f;
            var appearance = new HumanoidCharacterAppearance(Color.Black, strategy.FromUnary(tone), new());
            var validated = new HumanoidCharacterAppearance(
                appearance.EyeColor, strategy.EnsureVerified(appearance.SkinColor), new());

            Assert.That(strategy.VerifySkinColor(validated.SkinColor, out var reason), Is.True,
                $"Skin tone {tone} became invalid after profile quantization: {validated.SkinColor}. {reason}");
        }
    }

    [TestCase(0f)]
    [TestCase(23f)]
    [TestCase(47f)]
    [TestCase(180f)]
    public void HueOutsideQuantizationToleranceIsRejected(float hue)
    {
        var strategy = new HumanTonedSkinColoration();
        var color = Color.FromHsv(new Vector4(hue / 360f, 0.5f, 0.5f, 1f));

        Assert.That(strategy.VerifySkinColor(color, out _), Is.False);
    }

    [TestCase(0.18f, 1f)]
    [TestCase(1f, 0.18f)]
    public void SaturationAndValueLimitsArePreserved(float saturation, float value)
    {
        var strategy = new HumanTonedSkinColoration();
        var color = Color.FromHsv(new Vector4(35f / 360f, saturation, value, 1f));

        Assert.That(strategy.VerifySkinColor(color, out _), Is.False);
    }
}
