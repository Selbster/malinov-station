using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared._MalinovStation.Surgery.Components;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Standing;
using Content.Shared.StatusEffectNew;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Shared._MalinovStation.Surgery;

/// <summary>
/// Shared logic for the organ surgery mechanic: which surgeries/steps are currently valid for a patient,
/// independent of client or server. Step *effects* (moving organs, applying bleed, etc) are server-only
/// and live in Content.Server's SurgerySystem.
/// </summary>
public abstract partial class SharedSurgerySystem : EntitySystem
{
    [Dependency] protected IPrototypeManager Proto = default!;
    [Dependency] private BodySystem _body = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private StatusEffectsSystem _statusEffects = default!;

    /// <summary>Which structural part category an organ category physically lives inside. Categories not
    /// present here (Torso/Head/ArmLeft/ArmRight/LegLeft/LegRight) own themselves.</summary>
    private static readonly Dictionary<string, string> OwningPartCategory = new()
    {
        [OrganCategoryIds.Heart] = OrganCategoryIds.Torso,
        [OrganCategoryIds.Lungs] = OrganCategoryIds.Torso,
        [OrganCategoryIds.Stomach] = OrganCategoryIds.Torso,
        [OrganCategoryIds.Liver] = OrganCategoryIds.Torso,
        [OrganCategoryIds.Kidneys] = OrganCategoryIds.Torso,
        [OrganCategoryIds.Appendix] = OrganCategoryIds.Torso,
        [OrganCategoryIds.Brain] = OrganCategoryIds.Head,
        [OrganCategoryIds.Eyes] = OrganCategoryIds.Head,
        [OrganCategoryIds.Tongue] = OrganCategoryIds.Head,
        [OrganCategoryIds.Ears] = OrganCategoryIds.Head,
        [OrganCategoryIds.HandLeft] = OrganCategoryIds.ArmLeft,
        [OrganCategoryIds.HandRight] = OrganCategoryIds.ArmRight,
        [OrganCategoryIds.FootLeft] = OrganCategoryIds.LegLeft,
        [OrganCategoryIds.FootRight] = OrganCategoryIds.LegRight,
    };

    public override void Initialize()
    {
        base.Initialize();
    }

    public bool IsLyingDown(EntityUid target)
    {
        return TryComp<StandingStateComponent>(target, out var standing) && !standing.Standing;
    }

    /// <summary>
    /// Whether the patient is properly sedated for painless surgery - chemically forced asleep (e.g. via
    /// a surgical anesthetic), as opposed to merely lying down or naturally napping in a bed. A naturally
    /// sleeping patient still wakes up (and feels everything) the instant surgery deals it any damage -
    /// see <c>SleepingSystem.OnDamageChanged</c>, which is blocked only by <see cref="ForcedSleepingStatusEffectComponent"/>.
    /// </summary>
    public bool IsAnesthetized(EntityUid patient)
    {
        return HasComp<SleepingComponent>(patient) && _statusEffects.HasEffectComp<ForcedSleepingStatusEffectComponent>(patient);
    }

    /// <summary>
    /// Whether the patient is buckled to furniture with an <see cref="OperatingTableComponent"/>.
    /// </summary>
    public bool TryGetOperatingTable(EntityUid body, out Entity<OperatingTableComponent> table)
    {
        table = default;

        if (!TryComp<BuckleComponent>(body, out var buckle) || buckle.BuckledTo is not { } buckledTo)
            return false;

        if (!TryComp<OperatingTableComponent>(buckledTo, out var tableComp))
            return false;

        table = (buckledTo, tableComp);
        return true;
    }

    public bool TryFindOrgan(EntityUid body, ProtoId<OrganCategoryPrototype> category, out EntityUid organ)
    {
        organ = default;

        foreach (var candidate in _body.EnumerateOrgans(body))
        {
            if (!TryComp<OrganComponent>(candidate, out var organComp) || organComp.Category != category)
                continue;

            organ = candidate;
            return true;
        }

        return false;
    }

    public bool HasOrgan(EntityUid body, ProtoId<OrganCategoryPrototype> category)
    {
        return TryFindOrgan(body, category, out _);
    }

    /// <summary>The structural part category an organ category physically lives inside (itself, if it already is one).</summary>
    public ProtoId<OrganCategoryPrototype> GetOwningPartCategory(ProtoId<OrganCategoryPrototype> target)
        => OwningPartCategory.GetValueOrDefault(target.Id, target.Id);

    /// <summary>Finds the patient's current, live part entity of the given structural category, if attached.</summary>
    public bool TryFindPart(EntityUid body, ProtoId<OrganCategoryPrototype> category, out EntityUid part)
    {
        part = default;

        foreach (var candidate in _body.EnumerateParts(body))
        {
            if (!TryComp<OrganComponent>(candidate, out var organ) || organ.Category != category)
                continue;

            part = candidate;
            return true;
        }

        return false;
    }

    /// <summary>
    /// Where an InsertOrgan step should place its organ: the body's own container if the target category IS
    /// a structural part, otherwise the owning part's own container (e.g. a heart goes inside the torso).
    /// </summary>
    public bool TryGetInstallContainer(EntityUid body, ProtoId<OrganCategoryPrototype> targetCategory, [NotNullWhen(true)] out BaseContainer? container)
    {
        container = null;
        var owner = GetOwningPartCategory(targetCategory);

        if (owner == targetCategory)
            return _container.TryGetContainer(body, BodyComponent.ContainerID, out container);

        return TryFindPart(body, owner, out var part) && _container.TryGetContainer(part, BodyPartComponent.ContainerID, out container);
    }

    /// <summary>
    /// All surgeries currently applicable to this patient - organ presence/absence, prerequisite organ,
    /// and species match. Shared between the UI-facing surgery list and the server's authoritative gate on
    /// actually starting a surgery, so a surgery that isn't offered can't be started by a raw BUI message either.
    /// </summary>
    public IEnumerable<SurgeryPrototype> GetAvailableSurgeries(EntityUid body)
    {
        foreach (var surgery in Proto.EnumeratePrototypes<SurgeryPrototype>())
        {
            if (IsEligiblePatient(body, surgery))
                yield return surgery;
        }
    }

    /// <summary>Whether <paramref name="body"/> currently meets every precondition to start <paramref name="surgery"/>.</summary>
    public bool IsEligiblePatient(EntityUid body, SurgeryPrototype surgery)
    {
        return HasOrgan(body, surgery.TargetOrgan) == surgery.RequireOrganPresent && MeetsSurgeryPrerequisites(body, surgery);
    }

    /// <summary>
    /// The subset of <see cref="IsEligiblePatient"/>'s checks that hold for a surgery's entire duration,
    /// unlike <see cref="SurgeryPrototype.TargetOrgan"/> presence (which an ExtractOrgan/InsertOrgan step
    /// deliberately changes partway through) - safe to re-check on every tick of an already-running step,
    /// not just once when the surgery is started.
    /// </summary>
    public bool MeetsSurgeryPrerequisites(EntityUid body, SurgeryPrototype surgery)
    {
        if (surgery.RequiresOrgan is { } required && !HasOrgan(body, required))
            return false;

        if (surgery.SpeciesWhitelist is { } whitelist &&
            (!TryComp<HumanoidProfileComponent>(body, out var profile) || !whitelist.Contains(profile.Species)))
            return false;

        return true;
    }

    public int GetStepIndex(EntityUid body, ProtoId<SurgeryPrototype> surgery)
    {
        if (!TryComp<SurgeryProgressComponent>(body, out var progress))
            return 0;

        return progress.StepIndex.GetValueOrDefault(surgery, 0);
    }

    public bool TryGetHeldSurgeryTool(EntityUid user, SurgeryToolType type, out EntityUid tool, out SurgeryToolComponent toolComp)
    {
        foreach (var held in _hands.EnumerateHeld((user, null)))
        {
            if (!TryComp<SurgeryToolComponent>(held, out var comp) || comp.ToolType != type)
                continue;

            tool = held;
            toolComp = comp;
            return true;
        }

        tool = default;
        toolComp = default!;
        return false;
    }

    /// <summary>
    /// Finds an organ of the given category held (not worn/in a body) by the user - used by InsertOrgan steps.
    /// </summary>
    public bool TryGetHeldOrgan(EntityUid user, ProtoId<OrganCategoryPrototype> category, out EntityUid organ)
    {
        foreach (var held in _hands.EnumerateHeld((user, null)))
        {
            if (!TryComp<OrganComponent>(held, out var organComp) || organComp.Category != category || organComp.Body != null)
                continue;

            organ = held;
            return true;
        }

        organ = default;
        return false;
    }

    /// <summary>
    /// Checks whether <paramref name="user"/> can perform <paramref name="step"/> of <paramref name="surgery"/>
    /// on <paramref name="body"/> right now. On success, <paramref name="used"/> is the tool/organ entity that
    /// should be passed along as the DoAfter's "used" entity. Shared between server-side gating and
    /// client-side display.
    /// </summary>
    public bool TryValidateStep(
        EntityUid user,
        EntityUid body,
        SurgeryPrototype surgery,
        SurgeryStepPrototype step,
        out EntityUid? used,
        out string? blockedReason)
    {
        used = null;
        blockedReason = null;

        if (!IsLyingDown(body))
        {
            blockedReason = "surgery-blocked-needs-lying-down";
            return false;
        }

        switch (step.Kind)
        {
            case SurgeryStepKind.ExtractOrgan:
                if (!TryFindOrgan(body, surgery.TargetOrgan, out var organToExtract))
                {
                    blockedReason = "surgery-blocked-no-organ";
                    return false;
                }
                used = organToExtract;
                return true;

            case SurgeryStepKind.InsertOrgan:
                if (!TryGetHeldOrgan(user, surgery.TargetOrgan, out var organToInsert))
                {
                    blockedReason = "surgery-blocked-missing-organ";
                    return false;
                }

                // Holding the organ isn't enough - there also has to be somewhere to put it (e.g. no Head
                // means no container to install Eyes/Tongue/Ears/Brain into). Without this check, a step
                // could run its full DoAfter, show a success popup, and advance the surgery's cursor while
                // silently leaving the organ in the surgeon's hand - see ApplyStepEffect's own matching check.
                if (!TryGetInstallContainer(body, surgery.TargetOrgan, out _))
                {
                    blockedReason = "surgery-blocked-no-part";
                    return false;
                }

                used = organToInsert;
                return true;

            default:
                if (step.RequiredTool is not { } toolType)
                    return true;

                if (!TryGetHeldSurgeryTool(user, toolType, out var tool, out _))
                {
                    blockedReason = "surgery-blocked-missing-tool";
                    return false;
                }
                used = tool;
                return true;
        }
    }
}
