using Content.Shared.Body;
using Content.Shared.Humanoid;
using Robust.Shared.Prototypes;

namespace Content.Client._MalinovStation.Humanoid;

/// <summary>
/// Presents the separate tail organ under the torso without changing saved marking owners.
/// </summary>
internal static class MarkingPickerGrouping
{
    private static readonly ProtoId<OrganCategoryPrototype>[] TorsoOrgans = ["Torso", "Tail"];

    public static IEnumerable<(string LocKey, ProtoId<OrganCategoryPrototype>[] Organs)> WithTorsoGroup(
        IEnumerable<(string LocKey, ProtoId<OrganCategoryPrototype>[] Organs)> groups)
    {
        foreach (var group in groups)
            yield return group;

        yield return ("markings-organ-Torso", TorsoOrgans);
    }

    // Preserve the torso's existing exclusions; a separate tail organ remains editable.
    public static bool IsHiddenTorsoLayer(ProtoId<OrganCategoryPrototype> organ, HumanoidVisualLayers layer)
    {
        return organ == "Torso" && layer is HumanoidVisualLayers.Tail or HumanoidVisualLayers.Special;
    }

    // A torso has several distinct layers, while the limb groups need individual organ names.
    public static bool UseOrganTitle(ProtoId<OrganCategoryPrototype> organ)
    {
        return organ != "Torso";
    }
}
