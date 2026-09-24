using System.Linq;
using Content.Client._MalinovStation.Body;
using Content.Client.Inventory;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Inventory;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._MalinovStation.Client;

public sealed class TailLayerOrderTest : GameTest
{
    [SidedDependency(Side.Client)] private readonly SharedVisualBodySystem _visualBody = default!;
    [SidedDependency(Side.Client)] private readonly SpriteSystem _sprite = default!;
    [SidedDependency(Side.Client)] private readonly InventorySystem _inventory = default!;

    [TestCase("AppearanceDraconid", "Draconid", "Tail", "DraconidTailSpikesAnimated")]
    [TestCase("MobDraconid", "Draconid", "Tail", "DraconidTailSpikesAnimated")]
    [TestCase("AppearanceReptilian", "Reptilian", "Torso", "LizardTailSmooth")]
    [TestCase("MobReptilian", "Reptilian", "Torso", "LizardTailSmooth")]
    [TestCase("AppearanceReptilian", "Reptilian", "Torso", "LizardTailSpikesAnimated")]
    [TestCase("MobReptilian", "Reptilian", "Torso", "LizardTailSpikesAnimated")]
    public async Task TailOrderFollowsViewDirectionAfterProfileUpdates(string prototype, string species,
        string organ, string markingId)
    {
        EntityUid body = default;
        HumanoidCharacterProfile profile = null!;
        var tailColor = Color.FromHex("#3A7BCD");
        var equipped = false;

        await Client.WaitPost(() =>
        {
            body = CSpawn(prototype);
            profile = HumanoidCharacterProfile.DefaultWithSpecies(species);
            var marking = CProtoMan.Index<MarkingPrototype>(markingId);
            profile.Appearance.Markings[organ][HumanoidVisualLayers.Tail] = [marking.AsMarking().WithColor(tailColor)];
            _visualBody.ApplyProfileTo(body, profile);

            var clothing = CSpawn("ClothingUniformJumpsuitColorGrey");
            equipped = _inventory.TryEquip(body, clothing, "jumpsuit", silent: true, force: true);
        });

        var layerCount = 0;
        await Client.WaitAssertion(() =>
        {
            Assert.That(equipped, Is.True);
            layerCount = CComp<SpriteComponent>(body).AllLayers.Count();
        });

        // Updating the body recreates marking layers; neither their order nor their chosen color may drift.
        foreach (var skinColor in new[] { Color.FromHex("#C89D75"), Color.FromHex("#F1D5B6") })
        {
            await Client.WaitPost(() =>
            {
                profile = profile.WithCharacterAppearance(profile.Appearance.WithSkinColor(skinColor));
                _visualBody.ApplyProfileTo(body, profile);
            });

            var expectedColor = species == "Reptilian" ? skinColor : tailColor;
            foreach (var degrees in new[] { 0, 90, 180, 270, 180, 0 })
            {
                await Client.WaitPost(() =>
                {
                    var rotation = Angle.FromDegrees(degrees);
                    CEntMan.System<SharedTransformSystem>().SetLocalRotation(body, rotation);
                    CEntMan.System<DirectionalTailSystem>().FrameUpdate(0f);
                });
                await Client.WaitAssertion(() => AssertTailOrder(body, markingId, expectedColor, degrees == 180));
            }

            // A rotated camera and SpriteView's explicit direction must use the rendered facing.
            await Client.WaitPost(() => CEntMan.System<DirectionalTailSystem>()
                .UpdateOrder(body, Angle.Zero, Angle.FromDegrees(180)));
            await Client.WaitAssertion(() => AssertTailOrder(body, markingId, expectedColor, true));
            await Client.WaitPost(() => CEntMan.System<DirectionalTailSystem>()
                .UpdateOrder(body, Angle.Zero, Angle.FromDegrees(180), Direction.South));
            await Client.WaitAssertion(() =>
            {
                AssertTailOrder(body, markingId, expectedColor, false);
                Assert.That(CComp<SpriteComponent>(body).AllLayers.Count(), Is.EqualTo(layerCount),
                    "Reapplying a profile must replace the old marking layers without accumulating copies.");
            });
        }
    }

    private void AssertTailOrder(EntityUid body, string markingId, Color tailColor, bool inFront)
    {
        var marking = CProtoMan.Index<MarkingPrototype>(markingId);
        var clothingKeys = CComp<InventorySlotsComponent>(body).VisualLayerKeys["jumpsuit"];
        Assert.That(clothingKeys, Is.Not.Empty, "The comparison must include actual equipped clothing layers.");
        Assert.That(marking.Sprites, Is.Not.Empty);

        foreach (var sprite in marking.Sprites)
        {
            Assert.That(sprite, Is.TypeOf<SpriteSpecifier.Rsi>());
            var rsi = (SpriteSpecifier.Rsi) sprite;
            var tailIndex = _sprite.LayerMapGet(body, $"{marking.ID}-{rsi.RsiState}");
            Assert.That(_sprite.TryGetLayer(body, tailIndex, out var tailLayer, logMissing: false), Is.True);
            Assert.That(tailLayer!.Visible, Is.True);
            Assert.That(tailLayer.Color, Is.EqualTo(tailColor));

            foreach (var part in new[] { HumanoidVisualLayers.Chest, HumanoidVisualLayers.LLeg, HumanoidVisualLayers.RLeg })
                Assert.That(tailIndex > _sprite.LayerMapGet(body, part), Is.EqualTo(inFront),
                    $"Tail ordering relative to {part} must follow the rendered direction.");

            foreach (var clothingKey in clothingKeys)
                Assert.That(tailIndex > _sprite.LayerMapGet(body, clothingKey), Is.EqualTo(inFront),
                    "Tail ordering relative to clothing must follow the rendered direction.");
        }
    }
}
