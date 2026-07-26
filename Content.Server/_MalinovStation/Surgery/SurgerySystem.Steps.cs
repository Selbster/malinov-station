using Content.Shared._MalinovStation.Surgery;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Popups;
using Robust.Shared.Containers;

namespace Content.Server._MalinovStation.Surgery;

public sealed partial class SurgerySystem
{
    [Dependency] private SharedBloodstreamSystem _bloodstream = default!;

    /// <summary>Container holding a cascaded extremity (hand/foot) tucked inside its severed limb - see <see cref="LimbExtractionCascade"/>.</summary>
    private const string LimbCascadeContainerId = "surgery_cascade";

    /// <summary>
    /// Amputating a whole arm/leg takes the hand/foot on that side with it, tucked into a container on
    /// the limb so the two come away (and get carried) as a single item instead of two separate ones.
    /// Reattaching the limb pulls the tucked extremity back into the body at the same time.
    /// </summary>
    private static readonly Dictionary<string, string> LimbExtractionCascade = new()
    {
        [OrganCategoryIds.ArmLeft] = OrganCategoryIds.HandLeft,
        [OrganCategoryIds.ArmRight] = OrganCategoryIds.HandRight,
        [OrganCategoryIds.LegLeft] = OrganCategoryIds.FootLeft,
        [OrganCategoryIds.LegRight] = OrganCategoryIds.FootRight,
    };

    private void InitializeSteps()
    {
    }

    /// <summary>
    /// Applies whatever a successfully-completed step actually does: moving the organ, adjusting bleed,
    /// and any flavor sound/popup.
    /// </summary>
    private void ApplyStepEffect(EntityUid user, EntityUid body, SurgeryPrototype surgery, SurgeryStepPrototype step)
    {
        switch (step.Kind)
        {
            case SurgeryStepKind.ExtractOrgan:
                if (TryFindOrgan(body, surgery.TargetOrgan, out var organToExtract))
                {
                    if (LimbExtractionCascade.TryGetValue(surgery.TargetOrgan.Id, out var pairedCategory) &&
                        TryFindOrgan(body, pairedCategory, out var pairedOrgan))
                    {
                        var cascade = _container.EnsureContainer<ContainerSlot>(organToExtract, LimbCascadeContainerId);
                        _container.Insert(pairedOrgan, cascade);
                    }

                    _hands.PickupOrDrop(user, organToExtract);
                }
                break;

            case SurgeryStepKind.InsertOrgan:
                if (TryGetHeldOrgan(user, surgery.TargetOrgan, out var organToInsert) &&
                    _container.TryGetContainer(body, BodyComponent.ContainerID, out var organs))
                {
                    _hands.TryDrop(user, organToInsert);
                    _container.Insert(organToInsert, organs);

                    if (_container.TryGetContainer(organToInsert, LimbCascadeContainerId, out var cascade) &&
                        cascade is ContainerSlot { ContainedEntity: { } cascadedOrgan })
                    {
                        _container.Insert(cascadedOrgan, organs);
                    }
                }
                break;

            default:
                if (step.BleedDelta != 0)
                    _bloodstream.TryModifyBleedAmount(body, step.BleedDelta);
                if (step.Damage != null)
                    _damageable.TryChangeDamage(body, step.Damage, ignoreResistances: true, interruptsDoAfters: false);
                break;
        }

        if (step.Sound != null)
            _audio.PlayPvs(step.Sound, body);

        if (step.Popup != null)
            _popup.PopupEntity(Loc.GetString(step.Popup, ("user", user), ("target", body)), body, PopupType.Medium);
    }
}
