using System.Numerics;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Shared.Atmos.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Detects the "dynamic danger events" AI players react to (spec Milestone 7, extended in Milestone 1):
/// <list type="bullet">
/// <item>Being attacked (via <see cref="DamageChangedEvent"/>) - raises Safety, records a memory, and turns
/// the attacker's relationship negative (this is where Milestone 6's "only ever positive" relationship
/// nudges get a reason to go the other way).</item>
/// <item>Seeing another character incapacitated nearby (a periodic scan of what Perception already
/// recorded) - drives the HelpInjured goal.</item>
/// <item>Being near an active fire (a periodic scan of the grid's atmos hotspot state, reusing vanilla atmos
/// directly rather than a parallel fire system) - drives Flee exactly like being attacked does.</item>
/// </list>
/// Populates <see cref="DangerComponent"/>; <see cref="GoalSystem"/> reads it to prioritize Flee/HelpInjured.
/// </summary>
public sealed partial class DangerSystem : EntitySystem
{
    [Dependency] private NeedsSystem _needs = default!;
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private RelationshipSystem _relationships = default!;
    [Dependency] private EmotionSystem _emotion = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private AiLodSystem _lod = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("aiplayers.danger");

        SubscribeLocalEvent<AIPlayerComponent, DamageChangedEvent>(OnDamaged);
    }

    private void OnDamaged(EntityUid uid, AIPlayerComponent component, DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        if (!TryComp<DangerComponent>(uid, out var danger) || !TryComp<GoalComponent>(uid, out var goal))
            return;

        _needs.ModifySafety(uid, 0.6f);
        // AI Players 2.0 Milestone 1: no-op for a legacy AI player (no EmotionComponent).
        _emotion.Modify(uid, fearDelta: 0.5f, angerDelta: 0.3f);

        var origin = args.Origin;
        var isSelfInflicted = origin == uid;

        _memory.AddMemory(
            uid,
            content: origin is { } attacker && !isSelfInflicted
                ? $"Was attacked by {Comp<MetaDataComponent>(attacker).EntityName}!"
                : "Was hurt!",
            importance: 0.7f,
            source: "danger",
            participants: origin is { } o ? new[] { o } : null,
            emotionalWeight: -0.8f);

        if (origin is { } attackerUid && !isSelfInflicted && !Deleted(attackerUid))
        {
            danger.ThreatSource = attackerUid;
            danger.ThreatExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(danger.ThreatDurationSeconds);

            _relationships.ModifyRelationship(
                uid,
                attackerUid,
                trustDelta: -0.3f,
                friendshipDelta: -0.2f,
                fearDelta: 0.3f,
                angerDelta: 0.2f);

            _sawmill.Info($"[AI:{ToPrettyString(uid)}] Danger: attacked by {ToPrettyString(attackerUid)}.");
        }

        // Being attacked overrides whatever the LLM last told this AI player to do - self-preservation
        // takes priority over a stale directive, and the next GoalSystem tick should react immediately
        // rather than waiting out the routine reconsider cooldown.
        goal.IsLlmOverride = false;
        goal.ReconsiderAccumulator = 0f;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DangerComponent, PerceptionComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var danger, out var perception, out var xform))
        {
            if (danger.ThreatSource is not null && _timing.CurTime >= danger.ThreatExpiresAt)
                danger.ThreatSource = null;

            danger.ScanAccumulator -= frameTime;
            if (danger.ScanAccumulator > 0f)
                continue;

            // Only the routine scans (injured/fire) are LOD-scaled - the attack reaction in OnDamaged above is
            // event-driven and always fires immediately regardless of LOD.
            danger.ScanAccumulator = danger.ScanCooldown * _lod.GetMultiplier(uid);
            ScanForInjured(uid, danger, perception);
            ScanForFireHazard(uid, danger, xform);
        }
    }

    private void ScanForInjured(EntityUid uid, DangerComponent danger, PerceptionComponent perception)
    {
        var hadInjured = danger.NearbyInjured is not null;
        danger.NearbyInjured = null;

        if (perception.LastObservation is not { } observation)
            return;

        foreach (var other in observation.VisibleCharacters)
        {
            if (other == uid || Deleted(other))
                continue;

            if (!_mobState.IsIncapacitated(other))
                continue;

            danger.NearbyInjured = other;

            // Only on newly noticing them, not every ~2s scan they're still visible - same "hadHazard"
            // pattern ScanForFireHazard already uses below.
            if (!hadInjured)
                _emotion.Modify(uid, sadnessDelta: 0.2f);

            return;
        }
    }

    /// <summary>
    /// Looks for the closest active fire (atmos hotspot) within <see cref="DangerComponent.FireScanRadius"/> of
    /// the owner, reusing vanilla atmos state directly (<see cref="GridAtmosphereComponent.HotspotTiles"/>)
    /// rather than a parallel fire-detection system. On first noticing a hazard, forces an immediate goal
    /// reconsideration (same as <see cref="OnDamaged"/> does for being attacked) instead of waiting out the
    /// routine cooldown.
    /// </summary>
    private void ScanForFireHazard(EntityUid uid, DangerComponent danger, TransformComponent xform)
    {
        var hadHazard = danger.FireHazardLocation is not null;
        danger.FireHazardLocation = null;

        if (xform.GridUid is not { } gridUid ||
            !TryComp<GridAtmosphereComponent>(gridUid, out var gridAtmos) ||
            gridAtmos.HotspotTiles.Count == 0 ||
            !TryComp<MapGridComponent>(gridUid, out var grid))
        {
            return;
        }

        var ownerWorldPos = _transform.GetWorldPosition(xform);
        var closestDistanceSq = danger.FireScanRadius * danger.FireScanRadius;

        foreach (var tile in gridAtmos.HotspotTiles)
        {
            if (!tile.Hotspot.Valid)
                continue;

            var tileCoords = _mapSystem.GridTileToLocal(gridUid, grid, tile.GridIndices);
            var distanceSq = (_transform.ToMapCoordinates(tileCoords).Position - ownerWorldPos).LengthSquared();
            if (distanceSq > closestDistanceSq)
                continue;

            closestDistanceSq = distanceSq;
            danger.FireHazardLocation = tileCoords;
        }

        if (hadHazard || danger.FireHazardLocation is null || !TryComp<GoalComponent>(uid, out var goal))
            return;

        _emotion.Modify(uid, fearDelta: 0.3f);
        goal.IsLlmOverride = false;
        goal.ReconsiderAccumulator = 0f;
        _sawmill.Info($"[AI:{ToPrettyString(uid)}] Danger: noticed a nearby fire hazard.");
    }
}
