using Content.Shared._MalinovStation.Surgery;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Popups;

namespace Content.Server._MalinovStation.Surgery;

public sealed partial class SurgerySystem
{
    [Dependency] private SharedBloodstreamSystem _bloodstream = default!;

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
                    _hands.PickupOrDrop(user, organToExtract);
                break;

            case SurgeryStepKind.InsertOrgan:
                if (TryGetHeldOrgan(user, surgery.TargetOrgan, out var organToInsert) &&
                    _container.TryGetContainer(body, BodyComponent.ContainerID, out var organs))
                {
                    _hands.TryDrop(user, organToInsert);
                    _container.Insert(organToInsert, organs);
                }
                break;

            default:
                if (step.BleedDelta != 0)
                    _bloodstream.TryModifyBleedAmount(body, step.BleedDelta);
                break;
        }

        if (step.Sound != null)
            _audio.PlayPvs(step.Sound, body);

        if (step.Popup != null)
            _popup.PopupEntity(Loc.GetString(step.Popup, ("user", user), ("target", body)), body, PopupType.Medium);
    }
}
