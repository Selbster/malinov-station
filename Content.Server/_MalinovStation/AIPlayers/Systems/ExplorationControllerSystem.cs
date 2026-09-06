using System.Linq;
using System.Numerics;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC.Pathfinding;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// What kind of thing an <see cref="ExplorationTarget"/> is (AI Players 0.6.2 spec section 9). Kept to the two
/// that are genuinely distinct in behaviour: one the AI can name and remembers the way to, and one it cannot.
/// </summary>
public enum ExplorationTargetKind : byte
{
    /// <summary>A place this AI knows by name but is not yet familiar with (spec section 6, case A).</summary>
    KnownLocation,

    /// <summary>Somewhere reachable it has no name for at all - the "я не знаю, что находится там, но мне
    /// интересно посмотреть" case (spec sections 6 case B, 7 and 8).</summary>
    UnknownFrontier,
}

/// <summary>
/// AI Players 0.6.2: one candidate answer to "given that this AI has decided to explore, where can it
/// reasonably go?". <see cref="Reason"/> is Russian and written in the AI's own voice - it is what reaches the
/// trace and the outcome memory, so exploration is always explainable after the fact (spec section 7: the
/// primary decision must never read as "random room").
/// </summary>
public sealed record ExplorationTarget(
    ExplorationTargetKind Kind,
    EntityCoordinates Coordinates,
    string? PlaceName,
    float Score,
    string Reason);

/// <summary>
/// AI Players 0.6.2, spec section 5: the deterministic half of exploration. It does NOT decide *whether* to
/// explore - that stays with the cognitive layer (boredom -> desire -> LLM -> <c>ExploreStation</c>). It only
/// answers where, cheaply and without any LLM involvement (spec section 32).
///
/// Everything it reasons over already exists: <see cref="MemorySystem.GetExplorationCandidates"/> for
/// known-but-unfamiliar places, <see cref="LocationKnowledgeComponent"/> for real visit history,
/// <see cref="MemorySystem.GetLocationSentiment"/> for how those visits felt,
/// <see cref="MemorySystem.GetKnownPersonLocations"/> for who tends to be where, and
/// <see cref="PersonalityComponent.Curiosity"/> for how much any of that should matter to this particular
/// character. No new memory or navigation system (spec section 34.2/34.3/34.6) - the chosen point is handed
/// straight to the existing <c>ForcedDestination</c> HTN rails.
/// </summary>
public sealed partial class ExplorationControllerSystem : EntitySystem
{
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private PathfindingSystem _pathfinding = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private SharedMapSystem _mapSystem = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>
    /// Distance, in tiles, at which a known place stops being an attractive walk and starts being a trek.
    /// Not a hard cutoff - <see cref="DistanceScore"/> decays smoothly past it, so a genuinely interesting far
    /// place can still win over a dull near one (spec section 10 forbids reducing this to any single factor).
    /// </summary>
    private const float ComfortableTravelDistance = 30f;

    /// <summary>
    /// How far out to look for a nameless frontier point, longest first. The first distance is deliberately
    /// beyond <see cref="LandmarkPerceptionComponent.ScanRadius"/> so a frontier trip actually leaves the area
    /// the AI can already see; the shorter fallbacks keep an AI in a small or awkwardly-shaped room from
    /// concluding it has nowhere to go simply because every long probe happened to land in a wall.
    /// </summary>
    private static readonly float[] FrontierDistances = { 14f, 9f, 5f };

    /// <summary>Directions probed for a frontier point, as unit-ish offsets. Eight compass points is enough to
    /// find a way out of any ordinary room without being an expensive search.</summary>
    private static readonly Vector2[] FrontierDirections =
    {
        new(1, 0), new(-1, 0), new(0, 1), new(0, -1),
        new(0.7071f, 0.7071f), new(-0.7071f, 0.7071f), new(0.7071f, -0.7071f), new(-0.7071f, -0.7071f),
    };

    /// <summary>
    /// Whether this AI can currently think of anywhere worth exploring. Cheap enough for
    /// <see cref="Actions.IAiAction.IsEligible"/> to call every eligibility pass, and deliberately the same
    /// code path <see cref="TryGetTarget"/> uses, so the action is never offered a choice it cannot then make
    /// (spec section 28).
    /// </summary>
    public bool HasAnyTarget(EntityUid uid) => TryGetTarget(uid, out _);

    /// <summary>
    /// Picks the best-scoring exploration target, or returns false if there genuinely isn't one - which the
    /// caller must report as <see cref="Actions.AiActionOutcome.NoTarget"/> rather than silently doing nothing
    /// (spec section 15).
    /// </summary>
    public bool TryGetTarget(EntityUid uid, out ExplorationTarget target)
    {
        target = default!;

        if (!TryComp(uid, out TransformComponent? xform))
            return false;

        var candidates = new List<ExplorationTarget>();
        CollectKnownLocations(uid, xform, candidates);

        if (TryGetFrontier(uid, xform) is { } frontier)
            candidates.Add(frontier);

        if (candidates.Count == 0)
            return false;

        target = candidates.MaxBy(c => c.Score)!;
        return true;
    }

    /// <summary>
    /// Known-by-name places this AI is not yet familiar with (spec section 6 case A). Scored on several axes
    /// at once - unfamiliarity, how it felt last time, whether someone it knows tends to be there, and how far
    /// it is - because spec section 10 explicitly rules out "just take the lowest familiarity": a friend in a
    /// well-known room may legitimately beat a stranger's corridor.
    /// </summary>
    private void CollectKnownLocations(EntityUid uid, TransformComponent xform, List<ExplorationTarget> candidates)
    {
        var curiosity = TryComp<PersonalityComponent>(uid, out var personality) ? personality.Curiosity : 0.5f;
        var socialPlaces = _memory.GetKnownPersonLocations(uid);
        var currentArea = TryComp<LandmarkPerceptionComponent>(uid, out var landmark) ? landmark.CurrentAreaLabel : null;

        TryComp<ExplorationComponent>(uid, out var exploration);

        foreach (var name in _memory.GetExplorationCandidates(uid, max: 8))
        {
            // Standing in it already is not exploring it.
            if (currentArea is not null && string.Equals(name, currentArea, StringComparison.OrdinalIgnoreCase))
                continue;

            // Somewhere this AI has already set out for and failed to reach. Offering it again is how an AI
            // ends up trying the same blocked route every reflection for the rest of the round.
            if (exploration is not null &&
                exploration.UnreachablePlaces.TryGetValue(name, out var writtenOffUntil) &&
                _timing.CurTime < writtenOffUntil)
            {
                continue;
            }

            if (_memory.FindKnownLocation(uid, name) is not { } coordinates)
                continue;

            var (visits, familiarity) = _memory.GetLocationFamiliarity(uid, name);

            // The novelty of the place, weighted by how much this character cares about novelty at all - a
            // Curiosity 0.1 passenger barely distinguishes an unseen room from a familiar one (spec 19).
            var novelty = (1f - familiarity) * (0.4f + curiosity);

            // How the place felt last time. Small next to novelty, but it is what makes repeat visits to a
            // liked spot plausible and keeps the AI away from somewhere it had a bad time (spec section 24).
            var sentiment = _memory.GetLocationSentiment(uid, name) * 0.3f;

            // Somewhere a known person tends to be is worth seeing even if the room itself is dull (spec 10).
            var social = socialPlaces.Any(line => line.Contains(name, StringComparison.OrdinalIgnoreCase)) ? 0.35f : 0f;

            var score = novelty + sentiment + social + DistanceScore(xform, coordinates)
                + _random.NextFloat(-0.02f, 0.02f);

            candidates.Add(new ExplorationTarget(
                ExplorationTargetKind.KnownLocation,
                coordinates,
                name,
                score,
                DescribeKnownLocation(name, visits, social > 0f)));
        }
    }

    /// <summary>
    /// A reachable point this AI has no name for (spec sections 6 case B, 7, 8). Validated with
    /// <see cref="PathfindingSystem.GetPoly"/> - a null poly means "not on the navigable mesh at all", so a
    /// point behind a wall or out in space is never offered. That is a navigability check, not a full route
    /// check: whether a route actually exists is discovered by the existing HTN movement rails, and comes back
    /// as <see cref="Actions.AiActionOutcome.NoPath"/> feedback rather than being pre-computed here (spec
    /// section 32 wants this controller cheap and deterministic).
    /// </summary>
    private ExplorationTarget? TryGetFrontier(EntityUid uid, TransformComponent xform)
    {
        var origin = _transform.GetMapCoordinates(xform);
        var curiosity = TryComp<PersonalityComponent>(uid, out var personality) ? personality.Curiosity : 0.5f;

        // Shuffled so a blocked-in AI does not probe the same direction first every single attempt - variety
        // between otherwise equivalent options only, never the primary decision (spec section 7).
        var directions = FrontierDirections.ToList();
        _random.Shuffle(directions);

        foreach (var distance in FrontierDistances)
        {
            foreach (var direction in directions)
            {
                var candidate = new EntityCoordinates(xform.ParentUid, xform.LocalPosition + direction * distance);

                if (!IsSomewhereYouCouldStand(xform, candidate))
                    continue;

                // A nameless target is inherently speculative, so it sits deliberately below a good
                // known-location score and above a thoroughly familiar one: worth doing when the AI has run
                // out of interesting named places, not worth doing while one is still on offer.
                var score = 0.35f + curiosity * 0.35f + _random.NextFloat(-0.02f, 0.02f);

                return new ExplorationTarget(
                    ExplorationTargetKind.UnknownFrontier,
                    candidate,
                    null,
                    score,
                    "Ты не знаешь, что находится в той стороне, но тебе интересно посмотреть.");
            }
        }

        return null;
    }

    /// <summary>
    /// Whether <paramref name="candidate"/> is real floor the AI could plausibly walk onto.
    ///
    /// The navigation mesh is the better answer when it exists, so it is asked first. But it is built lazily
    /// per grid and can legitimately be absent - a freshly created grid, a barely-populated one - and treating
    /// "no nav mesh yet" as "nowhere to explore" would silently make exploration impossible rather than merely
    /// imprecise. So an actual non-space tile on the grid is accepted as the fallback: cruder, but it is a
    /// genuine physical fact about the world rather than an assumption, and whether a *route* exists is
    /// something the HTN movement rails will discover and report as NoPath either way (spec section 15).
    /// </summary>
    private bool IsSomewhereYouCouldStand(TransformComponent xform, EntityCoordinates candidate)
    {
        if (_pathfinding.GetPoly(candidate) is not null)
            return true;

        return xform.GridUid is { } gridUid &&
            TryComp<MapGridComponent>(gridUid, out var grid) &&
            _mapSystem.TryGetTileRef(gridUid, grid, candidate, out var tile) &&
            !tile.Tile.IsEmpty;
    }

    /// <summary>Mild preference for somewhere you can actually walk to soon: flat within
    /// <see cref="ComfortableTravelDistance"/>, decaying gently after it. Never disqualifying.</summary>
    private float DistanceScore(TransformComponent xform, EntityCoordinates destination)
    {
        if (!destination.TryDistance(EntityManager, xform.Coordinates, out var distance))
            return 0f;

        return distance <= ComfortableTravelDistance
            ? 0.1f
            : 0.1f - MathF.Min(0.3f, (distance - ComfortableTravelDistance) / 200f);
    }

    private static string DescribeKnownLocation(string name, int visits, bool social)
    {
        if (social)
            return $"Ты знаешь «{name}» и там обычно кто-то бывает.";

        return visits == 0
            ? $"Ты знаешь про «{name}», но ни разу там не был(а)."
            : $"Ты был(а) в «{name}» всего {visits} раз(а) — там ещё есть что посмотреть.";
    }
}
