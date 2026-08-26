using Content.Server._MalinovStation.AIPlayers.Components;
using Robust.Server.Player;
using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

file static class AiLevelOfDetailExtensions
{
    /// <summary>Multiplier a given tier applies, mirroring <see cref="AiLodComponent.GetMultiplier"/> -
    /// needed here to compute what an entity's *new* tier's multiplier will be before that component's
    /// own <see cref="AiLodComponent.Level"/> field has actually been updated to it yet.</summary>
    public static float MultiplierFor(this AiLevelOfDetail level, AiLodComponent lod) => level switch
    {
        AiLevelOfDetail.Reduced => lod.ReducedMultiplier,
        AiLevelOfDetail.Background => lod.BackgroundMultiplier,
        _ => 1f,
    };
}

/// <summary>
/// Classifies every AI player's <see cref="AiLevelOfDetail"/> by distance to the nearest connected player,
/// on its own throttled cadence (spec section 25). Other systems (Perception, Danger, Goal, the LLM
/// gateway, Social) read <see cref="AiLodComponent"/> to scale their own update frequency or, for
/// Background-tier AI, skip spending LLM budget on someone nobody can currently see.
/// </summary>
public sealed partial class AiLodSystem : EntitySystem
{
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<AiLodComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var lod, out var xform))
        {
            lod.ReassessAccumulator -= frameTime;
            if (lod.ReassessAccumulator > 0f)
                continue;

            lod.ReassessAccumulator = lod.ReassessCooldown;

            var previousLevel = lod.Level;
            lod.Level = ComputeLevel(xform, lod);

            // AI Players 0.6.1: a tier *improvement* (someone just got close enough to notice this AI)
            // must not leave a cognitive reflection stuck counting down a wait sized for the old, less
            // attentive tier - see AiLodComponent's own doc comment on why state here isn't supposed to
            // visibly "freeze" like this. Only ever shrinks the wait, never extends it, and only on
            // improvement - a tier downgrade doesn't need to touch anything, IsBackground already gates
            // new decisions out for that case.
            if (lod.Level < previousLevel && TryComp<CognitiveModeComponent>(uid, out var cognitive))
            {
                var rescaledWait = cognitive.ReflectionCooldown * lod.Level.MultiplierFor(lod);
                cognitive.ReflectionAccumulator = MathF.Min(cognitive.ReflectionAccumulator, rescaledWait);
            }
        }
    }

    private AiLevelOfDetail ComputeLevel(TransformComponent xform, AiLodComponent lod)
    {
        if (xform.MapUid is null)
            return AiLevelOfDetail.Background;

        var mapId = xform.MapID;
        var position = _transform.GetWorldPosition(xform);
        var nearestDistance = float.MaxValue;

        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } playerEntity || Deleted(playerEntity))
                continue;

            var playerXform = Transform(playerEntity);
            if (playerXform.MapID != mapId)
                continue;

            var distance = (position - _transform.GetWorldPosition(playerXform)).Length();
            if (distance < nearestDistance)
                nearestDistance = distance;
        }

        if (nearestDistance <= lod.FullRange)
            return AiLevelOfDetail.Full;

        return nearestDistance <= lod.ReducedRange ? AiLevelOfDetail.Reduced : AiLevelOfDetail.Background;
    }

    /// <summary>
    /// The cooldown multiplier for <paramref name="uid"/>, or 1 (full frequency) if it has no
    /// <see cref="AiLodComponent"/>.
    /// </summary>
    public float GetMultiplier(EntityUid uid)
    {
        return TryComp<AiLodComponent>(uid, out var lod) ? lod.GetMultiplier() : 1f;
    }

    /// <summary>
    /// Convenience check for systems (Social, the LLM gateway) that should skip Background-tier AI players
    /// entirely rather than just running less often.
    /// </summary>
    public bool IsBackground(EntityUid uid)
    {
        return TryComp<AiLodComponent>(uid, out var lod) && lod.Level == AiLevelOfDetail.Background;
    }
}
