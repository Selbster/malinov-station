using Content.Shared._MalinovStation.Surgery;
using Content.Shared._MalinovStation.Surgery.Components;
using Content.Shared.Body;
using Content.Shared.DoAfter;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.Server._MalinovStation.Surgery;

/// <summary>
/// Handles opening the surgery UI, starting surgery-step DoAfters, and applying their effects.
/// </summary>
public sealed partial class SurgerySystem : SharedSurgerySystem
{
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private SharedUserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        base.Initialize();

        InitializeSteps();

        SubscribeLocalEvent<SurgeryToolComponent, AfterInteractEvent>(OnToolAfterInteract);
        Subs.BuiEvents<SurgeryProgressComponent>(SurgeryUiKey.Key,
            subs => subs.Event<SurgeryStepChosenBuiMsg>(OnStepChosen));

        SubscribeLocalEvent<SurgeryProgressComponent, DoAfterAttemptEvent<SurgeryStepDoAfterEvent>>(OnStepAttempt);
        SubscribeLocalEvent<SurgeryProgressComponent, SurgeryStepDoAfterEvent>(OnStepFinished);
    }

    private void OnToolAfterInteract(Entity<SurgeryToolComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        if (!HasComp<BodyComponent>(target))
            return;

        if (target == args.User)
        {
            _popup.PopupEntity(Loc.GetString("surgery-cant-operate-self"), args.User, args.User, PopupType.SmallCaution);
            return;
        }

        args.Handled = true;
        EnsureComp<SurgeryProgressComponent>(target);
        _ui.OpenUi(target, SurgeryUiKey.Key, args.User);
        RefreshUi(target);
    }

    private void OnStepChosen(Entity<SurgeryProgressComponent> body, ref SurgeryStepChosenBuiMsg args)
    {
        var user = args.Actor;

        if (!Proto.TryIndex(args.Surgery, out var surgery) || !Proto.TryIndex(args.Step, out var step))
            return;

        var cursor = GetStepIndex(body, args.Surgery);
        if (cursor >= surgery.Steps.Count || surgery.Steps[cursor] != args.Step)
            return;

        // Only gate on organ presence when *starting* a surgery - an extraction surgery's own
        // ExtractOrgan step removes the organ partway through, which shouldn't lock out its
        // remaining steps (e.g. Cauterize).
        if (cursor == 0 && HasOrgan(body, surgery.TargetOrgan) != surgery.RequireOrganPresent)
            return;

        if (!TryValidateStep(user, body, surgery, step, out var used, out var blockedReason))
        {
            if (blockedReason != null)
                _popup.PopupEntity(Loc.GetString(blockedReason), user, user, PopupType.SmallCaution);
            return;
        }

        var duration = step.Duration;
        var successRate = 1f;

        if (used is { } usedEnt && TryComp<SurgeryToolComponent>(usedEnt, out var toolComp))
        {
            duration *= toolComp.Speed;
            successRate = toolComp.SuccessRate;
            if (toolComp.StartSound != null)
                _audio.PlayPvs(toolComp.StartSound, usedEnt);
        }

        if (TryGetOperatingTable(body, out var table))
        {
            duration *= table.Comp.SpeedMultiplier;
            successRate = MathF.Min(1f, successRate + table.Comp.SuccessRateBonus);
        }

        var doAfterArgs = new DoAfterArgs(EntityManager, user, duration,
            new SurgeryStepDoAfterEvent(args.Surgery, args.Step, successRate), body.Owner, body.Owner, used)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            AttemptFrequency = AttemptFrequency.EveryTick,
        };

        _doAfter.TryStartDoAfter(doAfterArgs);
    }

    private void OnStepAttempt(Entity<SurgeryProgressComponent> body, ref DoAfterAttemptEvent<SurgeryStepDoAfterEvent> args)
    {
        var ev = args.Event;

        if (!Proto.TryIndex(ev.Surgery, out var surgery) || !Proto.TryIndex(ev.Step, out var step))
        {
            args.Cancel();
            return;
        }

        if (!TryValidateStep(args.DoAfter.Args.User, body, surgery, step, out _, out _))
            args.Cancel();
    }

    private void OnStepFinished(Entity<SurgeryProgressComponent> body, ref SurgeryStepDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled)
            return;

        args.Handled = true;

        if (!Proto.TryIndex(args.Surgery, out var surgery) || !Proto.TryIndex(args.Step, out var step))
            return;

        var user = args.User;

        if (!_random.Prob(args.SuccessRate))
        {
            _popup.PopupEntity(Loc.GetString("surgery-step-failed"), body, user, PopupType.SmallCaution);
            RefreshUi(body);
            return;
        }

        ApplyStepEffect(user, body, surgery, step);

        var nextIndex = GetStepIndex(body, args.Surgery) + 1;
        if (nextIndex >= surgery.Steps.Count)
            body.Comp.StepIndex.Remove(args.Surgery);
        else
            body.Comp.StepIndex[args.Surgery] = nextIndex;

        Dirty(body);
        RefreshUi(body);
    }

    private void RefreshUi(EntityUid body)
    {
        var surgeries = new List<SurgeryDisplay>();

        foreach (var surgery in GetSurgeriesToShow(body))
        {
            var cursor = GetStepIndex(body, surgery.ID);
            var steps = new List<SurgeryStepDisplay>(surgery.Steps.Count);

            for (var i = 0; i < surgery.Steps.Count; i++)
            {
                var status = i < cursor ? SurgeryStepStatus.Completed
                    : i == cursor ? SurgeryStepStatus.Available
                    : SurgeryStepStatus.Locked;

                steps.Add(new SurgeryStepDisplay(surgery.Steps[i], status, null));
            }

            surgeries.Add(new SurgeryDisplay(surgery, steps));
        }

        _ui.SetUiState(body, SurgeryUiKey.Key, new SurgeryBuiState { Surgeries = surgeries });
    }

    /// <summary>
    /// Surgeries to list in the UI: everything currently available per organ presence, plus anything
    /// already in progress (even if its own steps have since made it organ-presence-"unavailable" -
    /// e.g. an extraction surgery after its ExtractOrgan step, still needing Cauterize).
    /// </summary>
    private IEnumerable<SurgeryPrototype> GetSurgeriesToShow(EntityUid body)
    {
        var shown = new HashSet<string>();

        foreach (var surgery in GetAvailableSurgeries(body))
        {
            shown.Add(surgery.ID);
            yield return surgery;
        }

        if (!TryComp<SurgeryProgressComponent>(body, out var progress))
            yield break;

        foreach (var surgeryId in progress.StepIndex.Keys)
        {
            if (shown.Contains(surgeryId.Id) || !Proto.TryIndex(surgeryId, out var surgery))
                continue;

            yield return surgery;
        }
    }
}
