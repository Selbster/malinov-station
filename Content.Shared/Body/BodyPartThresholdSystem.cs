using Content.Shared.Body.Systems;
using Content.Shared.Damage.Systems;
using Robust.Shared.Network;

namespace Content.Shared.Body;

/// <summary>
/// Severs a <see cref="BodyPartComponent"/> once its own damage crosses its <see cref="BodyPartThresholdsComponent"/>.
/// </summary>
public sealed partial class BodyPartThresholdSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstream = default!;
    [Dependency] private INetManager _net = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyPartThresholdsComponent, DamageChangedEvent>(OnPartDamaged);
    }

    private void OnPartDamaged(EntityUid uid, BodyPartThresholdsComponent thresholds, DamageChangedEvent args)
    {
        if (!TryComp<BodyPartComponent>(uid, out var part) || part.Body is not { } bodyId)
            return; // not currently attached - already severed, or a misconfigured prototype

        if (_damageable.GetTotalDamage((uid, args.Damageable)) < thresholds.AmputationThreshold)
            return;

        Amputate(uid, bodyId, thresholds);
    }

    private void Amputate(EntityUid partId, EntityUid bodyId, BodyPartThresholdsComponent thresholds)
    {
        // Mirrors GibbingSystem's own guard for this class of mutating, container-reparenting operation.
        if (!_net.IsServer)
            return;

        // Detaches the part - cascades BodySystem's OrganGotRemovedEvent for the part itself and everything
        // nested inside it (e.g. OrganEffectsSystem's leg-loss slowdown), same as a surgical extraction.
        _transform.AttachToGridOrMap(partId);

        _bloodstream.TryModifyBleedAmount(bodyId, thresholds.AmputationBleedAmount);

        // Reset after detaching, not before: clearing damage raises its own DamageChangedEvent, and by then
        // part.Body is already null, so the re-entrant call above bails out immediately instead of re-evaluating.
        _damageable.ClearAllDamage(partId);
    }
}
