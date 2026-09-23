using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server._MalinovStation.Nutrition;
using Content.Shared.Body;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.Body;

public sealed class HumanoidSpeciesTest : GameTest
{
    [SidedDependency(Side.Server)] private readonly MarkingManager _marking = default!;
    [SidedDependency(Side.Server)] private readonly HumanoidProfileSystem _profile = default!;
    [SidedDependency(Side.Server)] private readonly SharedVisualBodySystem _visualBody = default!;
    [SidedDependency(Side.Server)] private readonly SharedSolutionContainerSystem _solution = default!;

    [TestCase("Draconid")]
    [TestCase("MuHuman")]
    public async Task BodiesPreserveHumanOrgansAndRelationships(string species)
    {
        EntityUid human = default;
        EntityUid mob = default;
        EntityUid doll = default;
        await Server.WaitPost(() =>
        {
            var prototype = SProtoMan.Index<SpeciesPrototype>(species);
            human = SSpawn("MobHuman");
            mob = SSpawn(prototype.Prototype);
            doll = SSpawn(prototype.DollPrototype);
        });

        await Server.WaitAssertion(() =>
        {
            var humanOrgans = GetOrgans(human);
            foreach (var body in new[] { mob, doll })
            {
                var organs = GetOrgans(body);
                Assert.That(organs.Keys, Is.SupersetOf(humanOrgans.Keys));
                Assert.That(organs, Has.Count.EqualTo(humanOrgans.Count + 2));
                Assert.That(organs.ContainsKey("Wings"), Is.False);
                Assert.That(SComp<HumanoidProfileComponent>(body).Species.Id, Is.EqualTo(species));

                foreach (var (category, humanOrgan) in humanOrgans)
                {
                    var organ = organs[category];
                    if (category != "Head" && category != "Torso")
                    {
                        Assert.That(SComp<MetaDataComponent>(organ).EntityPrototype!.ID,
                            Is.EqualTo(SComp<MetaDataComponent>(humanOrgan).EntityPrototype!.ID));
                    }

                    if (STryComp<ChildOrganComponent>(humanOrgan, out var child) && child.Parent is { } parent)
                    {
                        Assert.That(SComp<ChildOrganComponent>(organ).Parent,
                            Is.EqualTo(organs[SComp<OrganComponent>(parent).Category!.Value]));
                    }
                }

                Assert.That(SComp<ChildOrganComponent>(organs["Horns"]).Parent, Is.EqualTo(organs["Head"]));
                Assert.That(SComp<ChildOrganComponent>(organs["Tail"]).Parent, Is.EqualTo(organs["Torso"]));
                Assert.That(SEntMan.HasComponent<DetachableOrganComponent>(organs["Horns"]), Is.False);
                Assert.That(SEntMan.HasComponent<DetachableOrganComponent>(organs["Tail"]), Is.False);
            }

            Assert.That(SEntMan.HasComponent<MilkProducerComponent>(doll), Is.False);
            Assert.That(SEntMan.HasComponent<MilkProducerComponent>(mob), Is.EqualTo(species == "MuHuman"));
            Assert.That(_solution.TryGetSolution(mob, "bloodstream", out _, out _), Is.True);
            Assert.That(_solution.TryGetSolution(mob, "milk", out _, out _), Is.EqualTo(species == "MuHuman"));
            Assert.That(SProtoMan.Index<SpeciesPrototype>(species).RoundStart, Is.EqualTo(species == "Draconid"),
                "Only Draconid is temporarily enabled for character editor testing.");
        });
    }

    [TestCase("Draconid")]
    [TestCase("MuHuman")]
    public async Task HumanMarkingsApplyToActualSpeciesBody(string species)
    {
        EntityUid mob = default;
        await Server.WaitPost(() =>
        {
            var prototype = SProtoMan.Index<SpeciesPrototype>(species);
            mob = SSpawn(prototype.Prototype);
            var profile = HumanoidCharacterProfile.DefaultWithSpecies(species, Sex.Female);
            profile.Appearance.Markings = _marking.ConvertMarkings(
                [new Marking("HumanHairLongBedhead2", [Color.Red])], species);
            profile.Appearance = HumanoidCharacterAppearance.EnsureValid(profile.Appearance, species, profile.Sex);
            _profile.ApplyProfileTo(mob, profile);
            _visualBody.ApplyProfileTo(mob, profile);
        });

        await Server.WaitAssertion(() =>
        {
            var organs = GetOrgans(mob);
            var head = SComp<VisualOrganMarkingsComponent>(organs["Head"]);
            Assert.That(head.MarkingData.Group.Id, Is.EqualTo("Human"));
            Assert.That(SComp<VisualOrganMarkingsComponent>(organs["Torso"]).MarkingData.Group.Id,
                Is.EqualTo("Human"));
            var hair = head.Markings[HumanoidVisualLayers.Hair];
            Assert.That(hair, Has.Count.EqualTo(1));
            Assert.That(hair[0].MarkingId, Is.EqualTo("HumanHairLongBedhead2"));
            Assert.That(hair[0].MarkingColors, Is.EqualTo(new[] { Color.Red }));
            Assert.That(SComp<VisualOrganComponent>(organs["Head"]).Profile.Sex, Is.EqualTo(Sex.Female));

            var owners = new Dictionary<HumanoidVisualLayers, ProtoId<OrganCategoryPrototype>>();
            foreach (var (category, data) in _marking.GetMarkingData(species))
            {
                foreach (var layer in data.Layers)
                {
                    Assert.That(owners.TryAdd(layer, category), Is.True,
                        $"{species} has multiple organs claiming marking layer {layer}.");
                }
            }

            Assert.That(owners[HumanoidVisualLayers.HeadTop].Id, Is.EqualTo("Horns"));
            Assert.That(owners[HumanoidVisualLayers.Tail].Id, Is.EqualTo("Tail"));
            Assert.That(owners[HumanoidVisualLayers.Overlay].Id, Is.EqualTo("Torso"));
        });
    }

    [TestCase("Draconid")]
    [TestCase("MuHuman")]
    public async Task SurvivalLoadoutsChooseOxygen(string species)
    {
        await Server.WaitAssertion(() =>
        {
            var profile = HumanoidCharacterProfile.DefaultWithSpecies(species);
            var loadout = new RoleLoadout("RoleSurvivalStandard");
            var collection = Server.InstanceDependencyCollection;

            foreach (var oxygen in new[] { "EmergencyOxygen", "LoadoutSpeciesEVAOxygen", "LoadoutSpeciesPocketDoubleOxygen" })
                Assert.That(loadout.IsValid(profile, null, oxygen, collection, out _), Is.True, oxygen);

            foreach (var nitrogen in new[] { "EmergencyNitrogen", "LoadoutSpeciesEVANitrogen", "LoadoutSpeciesPocketDoubleNitrogen" })
                Assert.That(loadout.IsValid(profile, null, nitrogen, collection, out _), Is.False, nitrogen);
        });
    }

    private Dictionary<ProtoId<OrganCategoryPrototype>, EntityUid> GetOrgans(EntityUid body)
    {
        return SComp<BodyComponent>(body).Organs!.ContainedEntities
            .ToDictionary(organ => SComp<OrganComponent>(organ).Category!.Value);
    }
}
