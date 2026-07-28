using Content.Shared._MalinovStation.Surgery;
using Content.Shared._MalinovStation.Surgery.Components;
using Content.Shared.Body;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.Popups;
using Robust.Shared.Prototypes;

namespace Content.Server._MalinovStation.Surgery;

public sealed partial class SurgerySystem
{
    [Dependency] private SharedBloodstreamSystem _bloodstream = default!;

    /// <summary>
    /// The (body, surgery) pair whose own Extract/InsertOrgan step is moving an organ right now, if any -
    /// lets <see cref="ReconcileStuckSurgeries"/> tell "this surgery's own action" apart from an organ
    /// moving for any other reason (damage-based amputation, gibbing, a different surgery entirely).
    /// </summary>
    private (EntityUid Body, ProtoId<SurgeryPrototype> Surgery)? _activeOrganMove;

    private void InitializeSteps()
    {
        SubscribeLocalEvent<OrganComponent, OrganGotRemovedEvent>(OnOrganMovedOut);
        SubscribeLocalEvent<OrganComponent, OrganGotInsertedEvent>(OnOrganMovedIn);
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
                // If the target is a whole limb (e.g. ArmLeft), its hand already lives permanently inside
                // its own BodyPartComponent container (see Content.Shared/Body/BodySystem.Parts.cs) - it
                // rides along for free, no special-casing needed here.
                if (TryFindOrgan(body, surgery.TargetOrgan, out var organToExtract))
                {
                    _activeOrganMove = (body, surgery.ID);
                    _hands.PickupOrDrop(user, organToExtract);
                    _activeOrganMove = null;
                }
                break;

            case SurgeryStepKind.InsertOrgan:
                if (TryGetHeldOrgan(user, surgery.TargetOrgan, out var organToInsert) &&
                    TryGetInstallContainer(body, surgery.TargetOrgan, out var container))
                {
                    _activeOrganMove = (body, surgery.ID);
                    _hands.TryDrop(user, organToInsert);
                    _container.Insert(organToInsert, container);
                    _activeOrganMove = null;
                }
                break;

            default:
                if (step.BleedDelta != 0)
                    _bloodstream.TryModifyBleedAmount(body, step.BleedDelta);
                if (step.Damage != null)
                    ApplyPartDamage(body, surgery, step.Damage);
                break;
        }

        if (step.Sound != null)
            _audio.PlayPvs(step.Sound, body);

        if (step.Popup != null)
            _popup.PopupEntity(Loc.GetString(step.Popup, ("user", user), ("target", body)), body, PopupType.Medium);
    }

    /// <summary>
    /// Applies damage to the patient as a whole (drives overall crit/death/pain, unchanged from before) and,
    /// if resolvable, to the specific anatomical part this surgery is being performed on - so a botched cut
    /// can actually risk prematurely severing the part being cut, the way <see cref="BodyPartThresholdSystem"/>'s
    /// per-part amputation threshold was built for. Silently a no-op on the part side when there's no such
    /// part yet (e.g. an installation surgery before its limb exists) or the part doesn't track its own
    /// damage (e.g. the torso, which can never be severed).
    /// </summary>
    private void ApplyPartDamage(EntityUid body, SurgeryPrototype surgery, DamageSpecifier damage)
    {
        _damageable.TryChangeDamage(body, damage, ignoreResistances: true, interruptsDoAfters: false);

        if (TryFindPart(body, GetOwningPartCategory(surgery.TargetOrgan), out var part))
            _damageable.TryChangeDamage(part, damage, ignoreResistances: true, interruptsDoAfters: false);
    }

    private void OnOrganMovedOut(Entity<OrganComponent> ent, ref OrganGotRemovedEvent args)
    {
        if (ent.Comp.Category is { } category)
            ReconcileStuckSurgeries(args.Target, category);
    }

    private void OnOrganMovedIn(Entity<OrganComponent> ent, ref OrganGotInsertedEvent args)
    {
        if (ent.Comp.Category is { } category)
            ReconcileStuckSurgeries(args.Target, category);
    }

    /// <summary>
    /// Drops any in-progress surgery targeting <paramref name="category"/> other than the one (if any)
    /// currently moving that organ itself via <see cref="_activeOrganMove"/> - i.e. the organ left/entered
    /// through something other than that specific surgery's own Extract/InsertOrgan step (damage-based
    /// amputation, gibbing, a different completed surgery, ...). Left alone, such a surgery could never
    /// resolve: its own Extract/InsertOrgan step would find the organ already gone/already present and
    /// block forever, with no way back to a fresh start (see SharedSurgerySystem.TryValidateStep).
    /// </summary>
    private void ReconcileStuckSurgeries(EntityUid body, ProtoId<OrganCategoryPrototype> category)
    {
        if (!TryComp<SurgeryProgressComponent>(body, out var progress) || progress.StepIndex.Count == 0)
            return;

        List<ProtoId<SurgeryPrototype>>? stale = null;

        foreach (var surgeryId in progress.StepIndex.Keys)
        {
            if (_activeOrganMove is { } active && active.Body == body && active.Surgery == surgeryId)
                continue;

            if (!Proto.TryIndex(surgeryId, out var surgery) || surgery.TargetOrgan != category)
                continue;

            (stale ??= new List<ProtoId<SurgeryPrototype>>()).Add(surgeryId);
        }

        if (stale == null)
            return;

        foreach (var surgeryId in stale)
            progress.StepIndex.Remove(surgeryId);

        Dirty(body, progress);
        RefreshUi(body);
    }
}
