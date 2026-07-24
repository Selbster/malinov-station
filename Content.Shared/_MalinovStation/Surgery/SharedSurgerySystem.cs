using System.Linq;
using Content.Shared._MalinovStation.Surgery.Components;
using Content.Shared.Body;
using Content.Shared.Buckle.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Standing;
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
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    public override void Initialize()
    {
        base.Initialize();
    }

    public bool IsLyingDown(EntityUid target)
    {
        return TryComp<StandingStateComponent>(target, out var standing) && !standing.Standing;
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

        if (!_container.TryGetContainer(body, BodyComponent.ContainerID, out var container))
            return false;

        foreach (var candidate in container.ContainedEntities)
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

    /// <summary>
    /// All surgeries currently applicable to this patient - organ presence/absence and species match.
    /// </summary>
    public IEnumerable<SurgeryPrototype> GetAvailableSurgeries(EntityUid body)
    {
        ProtoId<Humanoid.Prototypes.SpeciesPrototype>? species = null;
        if (TryComp<HumanoidProfileComponent>(body, out var profile))
            species = profile.Species;

        foreach (var surgery in Proto.EnumeratePrototypes<SurgeryPrototype>())
        {
            if (HasOrgan(body, surgery.TargetOrgan) != surgery.RequireOrganPresent)
                continue;

            if (surgery.SpeciesWhitelist is { } whitelist && (species is not { } s || !whitelist.Contains(s)))
                continue;

            yield return surgery;
        }
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
