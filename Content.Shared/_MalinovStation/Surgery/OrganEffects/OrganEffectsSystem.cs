using Content.Shared.Body;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Speech.Muting;
using Content.Shared.Standing;

namespace Content.Shared._MalinovStation.Surgery.OrganEffects;

/// <summary>
/// Gameplay consequences of missing an organ that isn't already handled for free by an existing system
/// (breathing/lungs, eating/stomach and reagent metabolism/liver already fail automatically once their
/// organ leaves the body - see SharedSurgerySystem's remarks). Eyes, tongue, heart and legs have no such
/// existing hook, so this wires them up the same way HandOrganSystem/BrainSystem do: react to the organ
/// physically entering/leaving the body's container.
/// </summary>
public sealed partial class OrganEffectsSystem : EntitySystem
{
    [Dependency] private BlindableSystem _blindable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSurgerySystem _surgery = default!;
    [Dependency] private StandingStateSystem _standing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EyeOrganComponent, OrganGotRemovedEvent>(OnEyesRemoved);
        SubscribeLocalEvent<EyeOrganComponent, OrganGotInsertedEvent>(OnEyesInserted);

        SubscribeLocalEvent<TongueOrganComponent, OrganGotRemovedEvent>(OnTongueRemoved);
        SubscribeLocalEvent<TongueOrganComponent, OrganGotInsertedEvent>(OnTongueInserted);

        SubscribeLocalEvent<HeartOrganComponent, OrganGotRemovedEvent>(OnHeartRemoved);
        SubscribeLocalEvent<HeartOrganComponent, OrganGotInsertedEvent>(OnHeartInserted);

        SubscribeLocalEvent<LegOrganComponent, OrganGotRemovedEvent>(OnLegRemoved);
        SubscribeLocalEvent<LegOrganComponent, OrganGotInsertedEvent>(OnLegInserted);

        SubscribeLocalEvent<OneLegComponent, ComponentInit>(OnOneLegInit);
        SubscribeLocalEvent<OneLegComponent, ComponentShutdown>(OnOneLegShutdown);
        SubscribeLocalEvent<OneLegComponent, RefreshMovementSpeedModifiersEvent>(OnOneLegRefreshSpeed);

        SubscribeLocalEvent<NoLegsComponent, ComponentInit>(OnNoLegsInit);
        SubscribeLocalEvent<NoLegsComponent, ComponentShutdown>(OnNoLegsShutdown);
        SubscribeLocalEvent<NoLegsComponent, RefreshMovementSpeedModifiersEvent>(OnNoLegsRefreshSpeed);
        SubscribeLocalEvent<NoLegsComponent, StandAttemptEvent>(OnNoLegsStandAttempt);
    }

    private void OnEyesRemoved(Entity<EyeOrganComponent> ent, ref OrganGotRemovedEvent args)
    {
        if (LifeStage(args.Target) >= EntityLifeStage.Terminating || !TryComp<BlindableComponent>(args.Target, out var blindable))
            return;

        ent.Comp.SavedEyeDamage = blindable.EyeDamage;
        ent.Comp.SavedMinDamage = blindable.MinDamage;

        var wasBlind = blindable.IsBlind;
        _blindable.AdjustEyeDamage((args.Target, blindable), blindable.MaxDamage - blindable.EyeDamage);

        if (!wasBlind && !_mobState.IsDead(args.Target))
            _popup.PopupEntity(Loc.GetString("surgery-organ-effect-eyes-lost"), args.Target, args.Target, PopupType.LargeCaution);
    }

    private void OnEyesInserted(Entity<EyeOrganComponent> ent, ref OrganGotInsertedEvent args)
    {
        if (!TryComp<BlindableComponent>(args.Target, out var blindable))
            return;

        var wasBlind = blindable.IsBlind;

        _blindable.SetMinDamage((args.Target, blindable), ent.Comp.SavedMinDamage ?? 0);
        _blindable.AdjustEyeDamage((args.Target, blindable), (ent.Comp.SavedEyeDamage ?? 0) - blindable.EyeDamage);

        ent.Comp.SavedEyeDamage = null;
        ent.Comp.SavedMinDamage = null;

        if (wasBlind && !blindable.IsBlind && !_mobState.IsDead(args.Target))
            _popup.PopupEntity(Loc.GetString("surgery-organ-effect-eyes-restored"), args.Target, args.Target, PopupType.LargeCaution);
    }

    private void OnTongueRemoved(Entity<TongueOrganComponent> ent, ref OrganGotRemovedEvent args)
    {
        if (LifeStage(args.Target) >= EntityLifeStage.Terminating)
            return;

        ent.Comp.WasMuted = HasComp<MutedComponent>(args.Target);
        if (!ent.Comp.WasMuted)
        {
            EnsureComp<MutedComponent>(args.Target);
            if (!_mobState.IsDead(args.Target))
                _popup.PopupEntity(Loc.GetString("surgery-organ-effect-tongue-lost"), args.Target, args.Target, PopupType.LargeCaution);
        }
    }

    private void OnTongueInserted(Entity<TongueOrganComponent> ent, ref OrganGotInsertedEvent args)
    {
        if (!ent.Comp.WasMuted && RemComp<MutedComponent>(args.Target) && !_mobState.IsDead(args.Target))
            _popup.PopupEntity(Loc.GetString("surgery-organ-effect-tongue-restored"), args.Target, args.Target, PopupType.LargeCaution);

        ent.Comp.WasMuted = false;
    }

    private void OnHeartRemoved(Entity<HeartOrganComponent> ent, ref OrganGotRemovedEvent args)
    {
        if (LifeStage(args.Target) >= EntityLifeStage.Terminating)
            return;

        if (!EnsureComp<NoHeartComponent>(args.Target, out _) && !_mobState.IsDead(args.Target))
            _popup.PopupEntity(Loc.GetString("surgery-organ-effect-heart-lost"), args.Target, args.Target, PopupType.LargeCaution);
    }

    private void OnHeartInserted(Entity<HeartOrganComponent> ent, ref OrganGotInsertedEvent args)
    {
        if (RemComp<NoHeartComponent>(args.Target) && !_mobState.IsDead(args.Target))
            _popup.PopupEntity(Loc.GetString("surgery-organ-effect-heart-restored"), args.Target, args.Target, PopupType.LargeCaution);
    }

    private void OnLegRemoved(Entity<LegOrganComponent> ent, ref OrganGotRemovedEvent args)
    {
        if (LifeStage(args.Target) >= EntityLifeStage.Terminating)
            return;

        RecalculateLegs(args.Target);
    }

    private void OnLegInserted(Entity<LegOrganComponent> ent, ref OrganGotInsertedEvent args)
    {
        RecalculateLegs(args.Target);
    }

    /// <summary>
    /// Re-derives the body's movement penalty from how many leg organs it currently has: 2 (or more,
    /// for exotic species) = no penalty, 1 = slowed, 0 = can't stand, crawls.
    /// </summary>
    private void RecalculateLegs(EntityUid body)
    {
        var legCount = (_surgery.HasOrgan(body, OrganCategoryIds.LegLeft) ? 1 : 0) + (_surgery.HasOrgan(body, OrganCategoryIds.LegRight) ? 1 : 0);

        if (legCount >= 2)
        {
            RemComp<OneLegComponent>(body);
            RemComp<NoLegsComponent>(body);
        }
        else if (legCount == 1)
        {
            RemComp<NoLegsComponent>(body);
            EnsureComp<OneLegComponent>(body);
        }
        else
        {
            RemComp<OneLegComponent>(body);
            EnsureComp<NoLegsComponent>(body);
        }
    }

    private void OnOneLegInit(Entity<OneLegComponent> ent, ref ComponentInit args)
        => _movementSpeed.RefreshMovementSpeedModifiers(ent);

    private void OnOneLegShutdown(Entity<OneLegComponent> ent, ref ComponentShutdown args)
        => _movementSpeed.RefreshMovementSpeedModifiers(ent);

    private void OnOneLegRefreshSpeed(Entity<OneLegComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
        => args.ModifySpeed(ent.Comp.SpeedModifier);

    private void OnNoLegsInit(Entity<NoLegsComponent> ent, ref ComponentInit args)
    {
        _movementSpeed.RefreshMovementSpeedModifiers(ent);
        _standing.Down(ent, playSound: true, dropHeldItems: false, force: true);
    }

    private void OnNoLegsShutdown(Entity<NoLegsComponent> ent, ref ComponentShutdown args)
        => _movementSpeed.RefreshMovementSpeedModifiers(ent);

    private void OnNoLegsRefreshSpeed(Entity<NoLegsComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
        => args.ModifySpeed(ent.Comp.SpeedModifier);

    private void OnNoLegsStandAttempt(Entity<NoLegsComponent> ent, ref StandAttemptEvent args)
        => args.Cancel();
}
