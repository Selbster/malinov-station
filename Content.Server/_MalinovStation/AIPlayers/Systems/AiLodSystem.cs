using Content.Server._MalinovStation.AIPlayers.Components;
using Robust.Server.Player;
using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

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
            lod.Level = ComputeLevel(xform, lod);
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
