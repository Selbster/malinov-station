using System.Linq;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.Pinpointer;
using Content.Shared.Interaction;
using Content.Shared.Physics;
using Content.Shared.Pinpointer;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Periodically scans for the nearest vanilla station beacon (<c>NavMapBeaconComponent</c> - Kitchen, Medbay,
/// Engineering etc, placed on every station map, see <c>NavMapSystem.TryGetNearestBeacon</c>) within
/// unobstructed line of sight, and remembers a genuinely new one by name (AI Navigation Controller v1),
/// mirroring <see cref="RepairOpportunitySystem"/>'s own scan-and-remember shape and reusing the same
/// non-omniscience discipline <see cref="PerceptionSystem"/> uses for characters: an AI player can only come
/// to know a place it could actually see, never a fresh station-wide query at decision time (see
/// <see cref="MemorySystem.FindKnownLocation"/>, which is the only thing that ever reads this memory back).
/// Deliberately its own system rather than folded into <see cref="PerceptionSystem"/>, whose own doc comment
/// scopes it to characters only.
///
/// Only ever added to AI players spawned in cognitive mode (see <see cref="AIPlayerSystem.SpawnAiPlayer"/>) -
/// a legacy AI player never has <see cref="LandmarkPerceptionComponent"/>, so this scan never runs for it at
/// all (matches the "new memory-writing side effects are cognitive-only" convention the last milestone
/// established, and avoids the added spatial-scan cost for every legacy/background AI player).
/// </summary>
public sealed partial class LandmarkPerceptionSystem : EntitySystem
{
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private AiLodSystem _lod = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private AiTraceSystem _trace = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<LandmarkPerceptionComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var landmark, out var xform))
        {
            landmark.ScanAccumulator -= frameTime;
            if (landmark.ScanAccumulator > 0f)
                continue;

            landmark.ScanAccumulator = landmark.ScanCooldown * _lod.GetMultiplier(uid);
            Scan(uid, landmark, xform);
        }
    }

    private void Scan(EntityUid uid, LandmarkPerceptionComponent landmark, TransformComponent xform)
    {
        string? nearbyText = null;

        if (FindNearestVisibleBeacon(uid, xform, landmark.ScanRadius) is var (beaconUid, text) && text is not null)
        {
            nearbyText = text;

            // Already remembered this exact beacon - nothing new to learn. Query-based rather than tracked
            // in a separate "known beacons" set: if this memory later gets evicted under
            // MemoryComponent.MaxMemories, re-noticing and re-adding it here is fine/self-healing, not a
            // bug to design around.
            var alreadyKnown = TryComp<MemoryComponent>(uid, out var memory) &&
                memory.Memories.Any(m => m.Source == "landmark" && m.Participants.Contains(beaconUid));

            if (!alreadyKnown)
            {
                _memory.AddMemory(
                    uid,
                    content: $"Неподалёку есть место под названием «{text}».",
                    importance: 0.25f,
                    source: "landmark",
                    participants: new[] { beaconUid },
                    location: Transform(beaconUid).Coordinates,
                    subject: text);
            }
        }

        UpdateCurrentArea(uid, landmark, nearbyText);
    }

    /// <summary>
    /// AI Players 0.6.2: the nearest beacon this AI can actually <em>see</em>, within
    /// <paramref name="radius"/>.
    ///
    /// This used to ask vanilla's <c>NavMapSystem.TryGetNearestBeacon</c> for the single nearest beacon
    /// station-wide and then test line of sight to that one. On a real station that is subtly wrong and it is
    /// the reason visits mostly stopped being recorded: beacons are dense, so the raw-nearest one is often in
    /// an adjacent room behind a wall. Line of sight to it fails, the whole scan concludes "not in any named
    /// area", and an AI standing directly on the beacon it just walked to never registers as having arrived
    /// anywhere - so <see cref="LocationKnowledge"/> never moves off zero and every place stays equally
    /// unfamiliar forever.
    ///
    /// Same candidate set and enabled/text filters vanilla itself uses, just ranked among the beacons that
    /// pass the sight check rather than ranking first and checking afterwards.
    /// </summary>
    private (EntityUid Uid, string? Text) FindNearestVisibleBeacon(EntityUid uid, TransformComponent xform, float radius)
    {
        var origin = _transform.GetWorldPosition(xform);
        var mapId = xform.MapID;

        EntityUid nearestUid = default;
        string? nearestText = null;
        var nearestDistanceSquared = radius * radius;

        var query = EntityQueryEnumerator<ConfigurableNavMapBeaconComponent, NavMapBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var beaconUid, out _, out var navBeacon, out var beaconXform))
        {
            if (!navBeacon.Enabled || string.IsNullOrWhiteSpace(navBeacon.Text) || beaconXform.MapID != mapId)
                continue;

            var distanceSquared = (origin - _transform.GetWorldPosition(beaconXform)).LengthSquared();
            if (distanceSquared > nearestDistanceSquared)
                continue;

            // Checked last: it is the expensive part, and only ever for a beacon already close enough to beat
            // the best candidate so far.
            if (!_interaction.InRangeUnobstructed(uid, beaconUid, radius, CollisionGroup.Opaque))
                continue;

            nearestDistanceSquared = distanceSquared;
            nearestUid = beaconUid;
            nearestText = navBeacon.Text;
        }

        return (nearestUid, nearestText);
    }

    /// <summary>
    /// AI Players 0.6: distinct from the landmark-memory write above - "am I currently standing in a named
    /// area" (physical presence, re-derived every scan) rather than "have I ever perceived this beacon"
    /// (a one-time discovery, deduped forever). Only fires on a genuine transition (nearest-in-range beacon's
    /// text differs from last scan), not every scan tick while stationary - see <see cref="RecordVisit"/> for
    /// why that granularity matters. Feeds both <see cref="Systems.NeedsSystem"/>'s boredom "same area for too
    /// long" signal and <see cref="LocationKnowledgeComponent"/>'s visit/familiarity tracking.
    /// </summary>
    private void UpdateCurrentArea(EntityUid uid, LandmarkPerceptionComponent landmark, string? nearbyText)
    {
        if (nearbyText == landmark.CurrentAreaLabel)
            return;

        landmark.CurrentAreaLabel = nearbyText;

        // Left every beacon's range (e.g. walking through a corridor) - nothing to record a visit to.
        if (nearbyText is null)
            return;

        // AI Players 0.6.3: only arriving somewhere *different* counts as having got anywhere. Stepping out of
        // a room into the corridor and back is pacing, not travel, and treating it as travel is what kept
        // boredom pinned near zero (see LandmarkPerceptionComponent.AreaEnteredAt).
        if (landmark.LastNamedArea != nearbyText)
        {
            landmark.LastNamedArea = nearbyText;
            landmark.AreaEnteredAt = _timing.CurTime;
        }

        RecordVisit(uid, nearbyText);
    }

    /// <summary>
    /// AI Players 0.6: bumps this AI player's personal <see cref="LocationKnowledge"/> for
    /// <paramref name="placeName"/> on arrival - the "known by name" (<see cref="MemorySystem.GetKnownLocationNames"/>)
    /// vs "actually familiar" (this) distinction spec section 6's own example draws
    /// ("Cargo: known=true, visits=1, familiarity=0.2"). No-ops for a legacy AI player (no
    /// <see cref="LocationKnowledgeComponent"/>, added cognitive-mode-only in <see cref="AIPlayerSystem.SpawnAiPlayer"/>).
    /// The formula is deliberately simple/deterministic (spec section 12: candidate filtering can be
    /// deterministic, the Cognitive LLM makes the meaningful choice between candidates) - not tuned for realism,
    /// just monotonic and boundable. <see cref="AiMemory.EmotionalWeight"/> is stamped from the AI's current mood
    /// (<see cref="EmotionComponent.Joy"/> minus <see cref="EmotionComponent.Sadness"/>) rather than left at its
    /// default 0 - gives spec section 21's "habits" (see <see cref="MemorySystem.GetLocationSentiment"/>) a real
    /// signal to aggregate instead of one that never varies.
    /// </summary>
    private void RecordVisit(EntityUid uid, string placeName)
    {
        if (!TryComp<LocationKnowledgeComponent>(uid, out var knowledge))
            return;

        if (!knowledge.Places.TryGetValue(placeName, out var place))
        {
            place = new LocationKnowledge { FirstVisitedAt = _timing.CurTime };
            knowledge.Places[placeName] = place;
        }

        place.VisitCount++;
        place.LastVisitedAt = _timing.CurTime;

        // Spec section 3: the far end of the chain - somewhere was actually reached and is now known better
        // than it was. Recorded against the decision that set off for it.
        _trace.DecisionDiscovery(uid, placeName);
        place.Familiarity = MathF.Min(1f, 0.15f * place.VisitCount);

        var mood = TryComp<EmotionComponent>(uid, out var emotion) ? emotion.Joy - emotion.Sadness : 0f;

        _memory.AddMemory(
            uid,
            content: $"Побывал(а) в «{placeName}».",
            importance: 0.2f,
            source: "visit",
            emotionalWeight: mood,
            subject: placeName);
    }

    /// <summary>
    /// One-shot pre-seed of every real station beacon's landmark memory at spawn time, called from
    /// <see cref="AIPlayerSystem.SpawnAiPlayer"/> - unlike <see cref="Scan"/> above (discovered gradually by
    /// actually walking near each one), this gives a cognitive AI player the general station layout knowledge
    /// a real, already-employed station worker would reasonably have on day one, instead of making them
    /// rediscover their own workplace from scratch. Distinct from this AI's "no omniscience" discipline
    /// elsewhere (e.g. <see cref="PerceptionComponent"/> never seeing an unperceived event): that principle is
    /// about dynamic, real-time information (what just happened), not static geography a real employee already
    /// knows (where the kitchen is) - so this deliberately bypasses the line-of-sight gate <see cref="Scan"/>
    /// itself still enforces for anything discovered during play.
    /// </summary>
    public void SeedKnownBeacons(EntityUid uid, TransformComponent xform)
    {
        var mapId = xform.MapID;

        var query = EntityQueryEnumerator<ConfigurableNavMapBeaconComponent, NavMapBeaconComponent, TransformComponent>();
        while (query.MoveNext(out var beaconUid, out _, out var navBeacon, out var beaconXform))
        {
            if (!navBeacon.Enabled || string.IsNullOrWhiteSpace(navBeacon.Text))
                continue;

            if (beaconXform.MapID != mapId)
                continue;

            var text = navBeacon.Text;

            if (TryComp<MemoryComponent>(uid, out var memory) &&
                memory.Memories.Any(m => m.Source == "landmark" && m.Participants.Contains(beaconUid)))
            {
                continue;
            }

            _memory.AddMemory(
                uid,
                content: $"Ты уже знаешь дорогу к «{text}».",
                importance: 0.25f,
                source: "landmark",
                participants: new[] { beaconUid },
                location: beaconXform.Coordinates,
                subject: text);
        }
    }
}
