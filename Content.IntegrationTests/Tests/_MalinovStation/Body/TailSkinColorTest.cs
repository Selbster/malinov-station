using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Body;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._MalinovStation.Body;

public sealed class TailSkinColorTest : GameTest
{
    [TestCase("Reptilian", "Torso", true)]
    [TestCase("Draconid", "Tail", false)]
    public async Task SkinChangesRecolorOnlyTailsThatMatchSkin(string species, string organCategory, bool matchSkin)
    {
        EntityUid mob = default;
        EntityUid doll = default;
        HumanoidCharacterProfile serverProfile = default!;
        HumanoidCharacterProfile clientProfile = default!;
        var customTailColor = Color.FromHex("#AB20CD");

        await Server.WaitPost(() =>
        {
            mob = SSpawn(SProtoMan.Index<SpeciesPrototype>(species).Prototype);
            serverProfile = HumanoidCharacterProfile.DefaultWithSpecies(species);
            var tails = serverProfile.Appearance.Markings[organCategory][HumanoidVisualLayers.Tail];
            tails[0] = tails[0].WithColor(customTailColor);
        });
        await Client.WaitPost(() =>
        {
            doll = CSpawn(CProtoMan.Index<SpeciesPrototype>(species).DollPrototype);
            clientProfile = HumanoidCharacterProfile.DefaultWithSpecies(species);
            var tails = clientProfile.Appearance.Markings[organCategory][HumanoidVisualLayers.Tail];
            tails[0] = tails[0].WithColor(customTailColor);
        });

        foreach (var skinColor in new[] { Color.FromHex("#228844"), Color.FromHex("#6644BB") })
        {
            await Server.WaitPost(() =>
            {
                serverProfile = serverProfile.WithCharacterAppearance(serverProfile.Appearance.WithSkinColor(skinColor));
                SEntMan.System<SharedVisualBodySystem>().ApplyProfileTo(mob, serverProfile);
            });
            await Client.WaitPost(() =>
            {
                // The lobby applies changed skin colors to the existing preview doll through this API.
                clientProfile = clientProfile.WithCharacterAppearance(clientProfile.Appearance.WithSkinColor(skinColor));
                CEntMan.System<SharedVisualBodySystem>().ApplyProfileTo(doll, clientProfile);
            });

            var expectedColor = matchSkin ? skinColor : customTailColor;
            await Server.WaitAssertion(() =>
            {
                var organ = SComp<BodyComponent>(mob).Organs!.ContainedEntities.Single(uid =>
                    SComp<OrganComponent>(uid).Category!.Value.Id == organCategory);
                var tail = SComp<VisualOrganMarkingsComponent>(organ).Markings[HumanoidVisualLayers.Tail].Single();
                Assert.That(tail.MarkingColors, Is.All.EqualTo(expectedColor));
            });
            await Client.WaitAssertion(() =>
            {
                var tail = clientProfile.Appearance.Markings[organCategory][HumanoidVisualLayers.Tail].Single();
                var marking = CProtoMan.Index<MarkingPrototype>(tail.MarkingId);
                var sprite = CComp<SpriteComponent>(doll);
                var spriteSystem = CEntMan.System<SpriteSystem>();
                foreach (var layer in marking.Sprites.Cast<SpriteSpecifier.Rsi>())
                {
                    var index = spriteSystem.LayerMapGet(doll, $"{marking.ID}-{layer.RsiState}");
                    Assert.That(sprite[index].Color, Is.EqualTo(expectedColor));
                }
            });
        }
    }
}
