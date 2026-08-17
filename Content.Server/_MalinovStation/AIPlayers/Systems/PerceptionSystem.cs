using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Perception;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Prometheus;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Periodically scans an AI player's surroundings and builds a <see cref="WorldObservation"/> from what it
/// could actually see: other characters within <see cref="PerceptionComponent.VisionRadius"/> and an
/// unobstructed line of sight. Never reads global/omniscient state - this is the AI's only route to
/// learning about other entities, which is what keeps Memory/Relationships honest about what the character
/// could plausibly know.
/// </summary>
public sealed partial class PerceptionSystem : EntitySystem
{
    /// <summary>
    /// Stabilization milestone stage 10: total perception scans performed, for "perception updates/sec"
    /// (Prometheus rate()) - same convention as LlmGatewaySystem's request counters.
    /// </summary>
    public static readonly Counter PerceptionScansMetric = Metrics.CreateCounter(
        "aiplayers_perception_scans_total",
        "Total number of AI player perception scans performed.");

    /// <summary>
    /// Wall-clock time per scan - the spatial lookup + line-of-sight raycasts are the single most expensive
    /// thing this subsystem does per entity (see the LOD comment below), so this is the metric to watch for
    /// perception becoming a real cost as AI player population grows.
    /// </summary>
    public static readonly Histogram PerceptionScanDurationMetric = Metrics.CreateHistogram(
        "aiplayers_perception_scan_duration_seconds",
        "Wall-clock time spent per AI player perception scan.",
        new HistogramConfiguration { Buckets = Histogram.ExponentialBuckets(0.0001, 2, 14) });

    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private RelationshipSystem _relationships = default!;
    [Dependency] private AiLodSystem _lod = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly HashSet<Entity<HumanoidProfileComponent>> _nearby = new();

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<PerceptionComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var perception, out var xform))
        {
            perception.PerceiveAccumulator -= frameTime;
            if (perception.PerceiveAccumulator > 0f)
                continue;

            // The spatial lookup + line-of-sight raycasts below are the single most expensive thing this
            // subsystem does per entity - this is the primary target for LOD (spec section 25).
            perception.PerceiveAccumulator = perception.PerceiveCooldown * _lod.GetMultiplier(uid);

            PerceptionScansMetric.Inc();
            using (PerceptionScanDurationMetric.NewTimer())
            {
                Perceive(uid, perception, xform);
            }
        }
    }

    private void Perceive(EntityUid uid, PerceptionComponent perception, TransformComponent xform)
    {
        _nearby.Clear();
        _lookup.GetEntitiesInRange(xform.Coordinates, perception.VisionRadius, _nearby);

        var visible = new List<EntityUid>();

        foreach (var (other, _) in _nearby)
        {
            if (other == uid)
                continue;

            if (!_interaction.InRangeUnobstructed(uid, other, perception.VisionRadius, CollisionGroup.Opaque))
                continue;

            visible.Add(other);
            Notice(uid, perception, other, xform.Coordinates);
        }

        perception.LastObservation = new WorldObservation(xform.Coordinates, visible, _timing.CurTime);
    }

    /// <summary>
    /// Records a first-impression memory and ensures a relationship entry exists for a newly (re)noticed
    /// character. Repeated sightings of the same entity are throttled by <see cref="PerceptionComponent.ResightCooldown"/>
    /// so standing near someone doesn't spam memory. The very first sighting ever also raises
    /// <see cref="AiPlayerMetNewCharacterEvent"/> for anything (e.g. the LLM gateway) that treats meeting
    /// someone new as worth a closer look.
    /// </summary>
    private void Notice(EntityUid uid, PerceptionComponent perception, EntityUid other, EntityCoordinates location)
    {
        var isFirstSighting = !perception.LastSeen.ContainsKey(other);

        if (!isFirstSighting &&
            perception.LastSeen.TryGetValue(other, out var lastSeen) &&
            _timing.CurTime - lastSeen < TimeSpan.FromSeconds(perception.ResightCooldown))
        {
            return;
        }

        perception.LastSeen[other] = _timing.CurTime;

        _relationships.EnsureRelationship(uid, other);

        var name = Comp<MetaDataComponent>(other).EntityName;
        _memory.AddMemory(
            uid,
            content: $"Saw {name} nearby.",
            importance: 0.02f,
            source: "perception",
            participants: new[] { other },
            location: location);

        if (isFirstSighting)
        {
            var ev = new AiPlayerMetNewCharacterEvent(uid, other);
            RaiseLocalEvent(uid, ref ev);
        }
    }
}
