using System.Diagnostics.CodeAnalysis;
using Content.Shared._MalinovStation.Nutrition;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration.Logs;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.Nutrition;

/// <summary>
/// Converts hunger into a reservoir of milk and transfers it through validated, interruptible interactions.
/// </summary>
public sealed partial class MilkProducerSystem : EntitySystem
{
    [Dependency] private ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private ISharedAdminLogManager _adminLogger = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private SatiationSystem _satiation = default!;
    [Dependency] private SharedSolutionContainerSystem _solutionContainer = default!;
    [Dependency] private SolutionTransferSystem _solutionTransfer = default!;
    [Dependency] private IGameTiming _timing = default!;

    private const float MilkingRange = 1.5f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MilkProducerComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<MilkProducerComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<MilkProducerComponent, DoAfterAttemptEvent<MilkProducerDoAfterEvent>>(OnDoAfterAttempt);
        SubscribeLocalEvent<MilkProducerComponent, MilkProducerDoAfterEvent>(OnDoAfter);
    }

    private void OnStartup(Entity<MilkProducerComponent> ent, ref ComponentStartup args)
    {
        ent.Comp.ConfigurationValid = ValidateConfiguration(ent);
        if (ent.Comp.ConfigurationValid)
            ent.Comp.NextGrowth = _timing.CurTime + ent.Comp.GrowthDelay;
    }

    private void OnGetVerbs(Entity<MilkProducerComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } container)
            return;

        if (!TryGetTransfer(ent, args.User, container, out _))
            return;

        var producer = ent.Owner;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("milk-producer-verb-milk"),
            Priority = 2,
            Act = () => TryStartMilking(producer, user, container),
        });
    }

    private void OnDoAfterAttempt(Entity<MilkProducerComponent> ent, ref DoAfterAttemptEvent<MilkProducerDoAfterEvent> args)
    {
        // A death interrupts the action even if the producer is revived before the original deadline.
        if (!IsLiving(ent))
            args.Cancel();
    }

    private void OnDoAfter(Entity<MilkProducerComponent> ent, ref MilkProducerDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } container)
            return;

        args.Handled = true;
        if (!TryGetTransfer(ent, args.User, container, out var transfer))
            return;

        var transferred = _solutionTransfer.Transfer(transfer);
        if (transferred <= FixedPoint2.Zero)
            return;

        _adminLogger.Add(LogType.Action,
            LogImpact.Low,
            $"{ToPrettyString(args.User):player} milked {ToPrettyString(ent):target} (prototype: {Prototype(ent)?.ID ?? "unknown"}) for {transferred}u into {ToPrettyString(container)}");
        _popup.PopupEntity(Loc.GetString("milk-producer-success",
                ("amount", transferred),
                ("container", Identity.Entity(container, EntityManager))),
            ent,
            args.User,
            PopupType.Medium);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<MilkProducerComponent>();
        while (query.MoveNext(out var uid, out var milkProducer))
        {
            if (!milkProducer.ConfigurationValid || _timing.CurTime < milkProducer.NextGrowth)
                continue;

            milkProducer.NextGrowth += milkProducer.GrowthDelay;
            if (!IsLiving(uid))
                continue;

            if (!TryComp<SatiationComponent>(uid, out var satiation))
                continue;

            if (!TryGetReservoir((uid, milkProducer), out var reservoir))
                continue;

            var requested = FixedPoint2.Min(milkProducer.QuantityPerUpdate, reservoir.Value.Comp.Solution.AvailableVolume);
            if (requested <= FixedPoint2.Zero)
                continue;

            var cost = requested.Float() * milkProducer.HungerPerUnit;
            if (!_satiation.IsValueInRange((uid, satiation),
                    SatiationSystem.Hunger,
                    above: milkProducer.MinHungerThreshold,
                    hypotheticalValueDelta: -cost))
                continue;

            // The return value means "all fitted"; a partial addition still consumes its actual nutrition cost.
            _solutionContainer.TryAddReagent(reservoir.Value, milkProducer.ReagentId, requested, out var accepted);
            if (accepted > FixedPoint2.Zero)
                _satiation.ModifyValue((uid, satiation), SatiationSystem.Hunger, -accepted.Float() * milkProducer.HungerPerUnit);
        }
    }

    private void TryStartMilking(EntityUid producer, EntityUid user, EntityUid container)
    {
        if (!TryComp<MilkProducerComponent>(producer, out var milkProducer))
            return;

        if (!TryGetTransfer((producer, milkProducer), user, container, out _))
            return;

        var attempt = new SolutionTransferAttemptEvent(producer, container);
        RaiseLocalEvent(producer, ref attempt);
        if (attempt.CancelReason is null)
            RaiseLocalEvent(container, ref attempt);

        if (attempt.CancelReason is { } reason)
        {
            _popup.PopupEntity(reason, producer, user);
            return;
        }

        var args = new DoAfterArgs(EntityManager, user, milkProducer.MilkingDelay, new MilkProducerDoAfterEvent(), producer, producer, container)
        {
            NeedHand = true,
            BreakOnHandChange = true,
            BreakOnDropItem = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            RequireCanInteract = true,
            DistanceThreshold = MilkingRange,
            AttemptFrequency = AttemptFrequency.EveryTick,
            BlockDuplicate = true,
            CancelDuplicate = true,
        };
        _doAfter.TryStartDoAfter(args);
    }

    private bool TryGetTransfer(Entity<MilkProducerComponent> ent, EntityUid user, EntityUid container, out SolutionTransferData transfer)
    {
        transfer = default;
        if (!ent.Comp.ConfigurationValid || !Exists(ent) || !Exists(user) || !Exists(container) || !IsLiving(ent))
            return false;

        if (!_actionBlocker.CanInteract(user, ent) || !_interaction.InRangeAndAccessible(user, ent.Owner, MilkingRange))
            return false;

        if (_hands.GetActiveItem(user) != container)
            return false;

        if (!TryGetReservoir(ent, out var source))
            return false;

        if (!TryComp<RefillableSolutionComponent>(container, out var refillable))
            return false;

        if (!_solutionContainer.TryGetRefillableSolution((container, refillable, null), out var target, out var targetSolution))
            return false;

        if (source.Value.Owner == target.Value.Owner)
            return false;

        var amount = FixedPoint2.Min(source.Value.Comp.Solution.Volume, targetSolution.AvailableVolume);
        if (refillable.MaxRefill is { } maxRefill)
            amount = FixedPoint2.Min(amount, maxRefill);

        if (amount <= FixedPoint2.Zero)
            return false;

        transfer = new SolutionTransferData(user, ent, source.Value, container, target.Value, amount);
        return true;
    }

    private bool TryGetReservoir(Entity<MilkProducerComponent> ent, [NotNullWhen(true)] out Entity<SolutionComponent>? reservoir)
    {
        if (_solutionContainer.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out reservoir, out _))
            return true;

        if (!ent.Comp.MissingSolutionLogged)
        {
            Log.Warning($"Milk producer {ToPrettyString(ent)} (prototype: {Prototype(ent)?.ID ?? "unknown"}) cannot access solution '{ent.Comp.SolutionName}'.");
            ent.Comp.MissingSolutionLogged = true;
        }

        return false;
    }

    private bool IsLiving(EntityUid uid)
    {
        return TryComp<MobStateComponent>(uid, out var mobState) && mobState.CurrentState is MobState.Alive or MobState.Critical;
    }

    private bool ValidateConfiguration(Entity<MilkProducerComponent> ent)
    {
        var milkProducer = ent.Comp;
        if (!string.IsNullOrWhiteSpace(milkProducer.SolutionName)
            && milkProducer.QuantityPerUpdate > FixedPoint2.Zero
            && milkProducer.GrowthDelay > TimeSpan.Zero
            && milkProducer.GrowthDelay < TimeSpan.MaxValue - _timing.CurTime
            && milkProducer.MilkingDelay > TimeSpan.Zero
            && milkProducer.MilkingDelay < TimeSpan.MaxValue - _timing.CurTime
            && float.IsFinite(milkProducer.HungerPerUnit)
            && milkProducer.HungerPerUnit >= 0
            && float.IsFinite(milkProducer.QuantityPerUpdate.Float() * milkProducer.HungerPerUnit)
            && _prototype.HasIndex(milkProducer.ReagentId))
            return true;

        Log.Warning($"Disabled milk producer {ToPrettyString(ent)} (prototype: {Prototype(ent)?.ID ?? "unknown"}): invalid configuration for solution '{milkProducer.SolutionName}', reagent '{milkProducer.ReagentId}', yield {milkProducer.QuantityPerUpdate}, interval {milkProducer.GrowthDelay}, hunger cost {milkProducer.HungerPerUnit}, milking delay {milkProducer.MilkingDelay}.");
        return false;
    }
}
