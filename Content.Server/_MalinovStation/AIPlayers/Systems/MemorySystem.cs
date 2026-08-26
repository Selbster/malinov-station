using System.Linq;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Shared._MalinovStation.AIPlayers;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Creates and retrieves <see cref="AiMemory"/> entries on <see cref="MemoryComponent"/>. Retrieval is
/// deliberately narrow (about one entity, or the N most important/relevant) rather than "give me everything"
/// so callers (HTN operators today, LLM context building later) can't accidentally dump the whole store.
/// Implements <see cref="IMemoryStore"/>/<see cref="IMemoryRetriever"/> (AI Players 0.4 Milestone 7) purely
/// additively - every existing method/call site here is unchanged in shape, the interfaces just formalize a
/// subset of this same public surface for a future alternative implementation to slot into.
/// </summary>
public sealed partial class MemorySystem : EntitySystem, IMemoryStore, IMemoryRetriever
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>AI Players 0.6.1: how recently-visited counts as "just came from there" for
    /// <see cref="GetExplorationCandidates"/> - long enough that a single loop through a familiar wing
    /// doesn't just re-suggest the room the AI left thirty seconds ago (spec section 22).</summary>
    private static readonly TimeSpan RecentVisitPenaltyWindow = TimeSpan.FromMinutes(10);

    private float _decayHalfLifeSeconds;

    public override void Initialize()
    {
        base.Initialize();
        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersMemoryDecayHalfLifeSeconds, v => _decayHalfLifeSeconds = v, true);
    }

    AiMemory? IMemoryStore.AddMemory(
        EntityUid uid,
        string content,
        float importance,
        string source,
        IReadOnlyList<EntityUid>? participants,
        EntityCoordinates? location,
        float emotionalWeight,
        string? subject) =>
        AddMemory(uid, content, importance, source, participants, location, emotionalWeight, subject: subject);

    /// <summary>
    /// Records a new memory. No-ops (returns null) if the entity has no <see cref="MemoryComponent"/>.
    /// </summary>
    public AiMemory? AddMemory(
        EntityUid uid,
        string content,
        float importance,
        string source,
        IReadOnlyList<EntityUid>? participants = null,
        EntityCoordinates? location = null,
        float emotionalWeight = 0f,
        MemoryComponent? memory = null,
        string? subject = null)
    {
        if (!Resolve(uid, ref memory, false))
            return null;

        var entry = new AiMemory
        {
            Timestamp = _timing.CurTime,
            Importance = Math.Clamp(importance, 0f, 1f),
            Source = source,
            Participants = participants?.ToList() ?? new List<EntityUid>(),
            Location = location,
            Content = content,
            EmotionalWeight = Math.Clamp(emotionalWeight, -1f, 1f),
            Subject = subject,
        };

        memory.Memories.Add(entry);

        if (memory.Memories.Count > memory.MaxMemories)
        {
            var weakest = memory.Memories
                .OrderBy(m => m.Importance)
                .ThenBy(m => m.Timestamp)
                .First();
            memory.Memories.Remove(weakest);
        }

        return entry;
    }

    /// <summary>
    /// Most recent memories that involve the given other entity, newest first.
    /// </summary>
    public IReadOnlyList<AiMemory> GetMemoriesAbout(EntityUid uid, EntityUid other, int max = 10, MemoryComponent? memory = null)
    {
        if (!Resolve(uid, ref memory, false))
            return Array.Empty<AiMemory>();

        return memory.Memories
            .Where(m => m.Participants.Contains(other))
            .OrderByDescending(m => m.Timestamp)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// The most important memories overall, highest effective (decayed) importance first.
    /// </summary>
    public IReadOnlyList<AiMemory> GetMostImportant(EntityUid uid, int max = 10, MemoryComponent? memory = null)
    {
        if (!Resolve(uid, ref memory, false))
            return Array.Empty<AiMemory>();

        var now = _timing.CurTime;
        return memory.Memories
            .OrderByDescending(m => GetEffectiveImportance(m, now))
            .ThenByDescending(m => m.Timestamp)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// AI Players 0.4 Milestone 9: <paramref name="memory"/>'s importance decayed by how long ago it was
    /// formed, using a simple exponential half-life (spec: "the exact formula should be simple and
    /// configurable"). Never mutates <see cref="AiMemory.Importance"/> itself - decay only ever affects
    /// ordering/relevance at read time, so a memory that later gets reinforced (re-added with the same
    /// subject) isn't fighting a permanently-lowered stored value.
    /// </summary>
    public float GetEffectiveImportance(AiMemory memory, TimeSpan now)
    {
        if (_decayHalfLifeSeconds <= 0f)
            return memory.Importance;

        var ageSeconds = (now - memory.Timestamp).TotalSeconds;
        if (ageSeconds <= 0)
            return memory.Importance;

        var decay = MathF.Pow(0.5f, (float)(ageSeconds / _decayHalfLifeSeconds));
        return memory.Importance * decay;
    }

    /// <summary>
    /// The AI Navigation Controller's only way to "know" a location (spec: never a fresh omniscient query) -
    /// searches this entity's own <c>"landmark"</c>-sourced memories (written by
    /// <see cref="LandmarkPerceptionSystem"/> when it actually perceives a station beacon) or
    /// <c>"search-result"</c>-sourced memories (written by <see cref="Actions.SearchAreaAction"/> when it
    /// actively looks for something and finds it - AI Players 0.3's AI Search slice) for one whose
    /// <see cref="AiMemory.Subject"/> matches <paramref name="nameHint"/>, and returns its remembered
    /// <see cref="AiMemory.Location"/>. Matching is a loose case-insensitive substring check in either
    /// direction, so a slightly-off LLM-proposed hint ("kitchen" vs "Kitchen area") still resolves. Returns
    /// null if this AI has never perceived/found anywhere by that name.
    /// </summary>
    public EntityCoordinates? FindKnownLocation(EntityUid uid, string nameHint, MemoryComponent? memory = null)
    {
        if (!Resolve(uid, ref memory, false) || string.IsNullOrWhiteSpace(nameHint))
            return null;

        // AI Players 0.6: "social-location" (where a known person was last seen, written by
        // ContextBuilderSystem.BuildCognitiveState) resolves through this exact same lookup - the entire
        // mechanism behind "Sarah is often in Medbay" -> GoToKnownLocation("Сара") needing no new action or
        // resolver path (spec section 32).
        return memory.Memories
            .Where(m => (m.Source == "landmark" || m.Source == "search-result" || m.Source == "social-location") &&
                m.Location is not null && m.Subject is { } subject &&
                (subject.Contains(nameHint, StringComparison.OrdinalIgnoreCase) ||
                 nameHint.Contains(subject, StringComparison.OrdinalIgnoreCase)))
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.Timestamp)
            .Select(m => m.Location)
            .FirstOrDefault();
    }

    /// <summary>
    /// Every distinct place/thing name this AI currently remembers (landmarks it has perceived, or things a
    /// previous <see cref="Actions.SearchAreaAction"/> found), strongest/most-recent first - surfaced to the
    /// LLM (see <see cref="Systems.ContextBuilderSystem.BuildCognitiveState"/>) so it only ever proposes a
    /// "GoToKnownLocation" hint that <see cref="FindKnownLocation"/> can actually resolve.
    /// </summary>
    public IReadOnlyList<string> GetKnownLocationNames(EntityUid uid, int max = 5, MemoryComponent? memory = null)
    {
        if (!Resolve(uid, ref memory, false))
            return Array.Empty<string>();

        return memory.Memories
            .Where(m => (m.Source == "landmark" || m.Source == "search-result") && m.Subject is not null)
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.Timestamp)
            .Select(m => m.Subject!)
            .Distinct()
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// AI Players 0.6: whether <c>GoToKnownLocationAction</c> has any legal destination to offer right
    /// now - a named place (<see cref="GetKnownLocationNames"/>'s sources) or a known person's last-seen
    /// location, which resolves through the exact same <see cref="FindKnownLocation"/> rails. Kept separate
    /// from <see cref="GetKnownLocationNames"/> itself (which stays place-only - it also feeds
    /// <see cref="Systems.ContextBuilderSystem"/>'s place-specific familiarity phrasing, and mixing person
    /// names into it would duplicate what <see cref="GetKnownPersonLocations"/> already surfaces more
    /// naturally) so eligibility doesn't need to guess a location-hint string upfront - it only needs to know
    /// whether <em>something</em> would resolve.
    /// </summary>
    public bool HasAnyKnownDestination(EntityUid uid, MemoryComponent? memory = null)
    {
        if (!Resolve(uid, ref memory, false))
            return false;

        return memory.Memories.Any(m => m.Subject is not null &&
            (m.Source == "landmark" || m.Source == "search-result" || m.Source == "social-location"));
    }

    /// <summary>
    /// AI Players 0.6: every distinct person this AI has a "where I usually see them" memory about
    /// (<c>"social-location"</c>, written by <see cref="Systems.ContextBuilderSystem.BuildCognitiveState"/>),
    /// most-relevant first, one already-human-readable line per person - the destination side of spec section
    /// 32's Sarah/Medbay scenario surfaced to the LLM (see <see cref="Systems.ContextBuilderSystem"/>, which
    /// appends this into the same <c>KnownLocations</c> list <see cref="GetKnownLocationNames"/> already feeds,
    /// since <see cref="FindKnownLocation"/> now resolves a person's name exactly like a place's).
    /// </summary>
    public IReadOnlyList<string> GetKnownPersonLocations(EntityUid uid, int max = 3, MemoryComponent? memory = null)
    {
        if (!Resolve(uid, ref memory, false))
            return Array.Empty<string>();

        var now = _timing.CurTime;
        return memory.Memories
            .Where(m => m.Source == "social-location" && m.Subject is not null)
            .OrderByDescending(m => GetEffectiveImportance(m, now))
            .ThenByDescending(m => m.Timestamp)
            .GroupBy(m => m.Subject)
            .Select(g => g.First().Content)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// AI Players 0.6: how many times this AI has actually visited <paramref name="placeName"/> and how
    /// familiar it is as a result (0 if never visited, even if the name is already known - see
    /// <see cref="Components.LocationKnowledge"/>'s own doc comment for the "known by name" vs "familiar"
    /// distinction this whole milestone hinges on). Returns <c>(0, 0f)</c> for a legacy AI player (no
    /// <see cref="Components.LocationKnowledgeComponent"/>) or a place never actually visited.
    /// </summary>
    public (int VisitCount, float Familiarity) GetLocationFamiliarity(EntityUid uid, string placeName)
    {
        if (!TryComp<LocationKnowledgeComponent>(uid, out var knowledge) ||
            !knowledge.Places.TryGetValue(placeName, out var place))
        {
            return (0, 0f);
        }

        return (place.VisitCount, place.Familiarity);
    }

    /// <summary>
    /// AI Players 0.6.1: known-by-name place candidates ranked for exploration rather than "important/recent
    /// first" - <see cref="GetKnownLocationNames"/> orders by <see cref="AiMemory.Importance"/> then
    /// <see cref="AiMemory.Timestamp"/>, but <see cref="Systems.LandmarkPerceptionSystem.SeedKnownBeacons"/>
    /// writes every station beacon with the exact same importance and (within one spawn) near-identical
    /// timestamp, so that ordering is a tie broken by stable enumeration order in practice - every AI player
    /// sees the same fixed handful of names forever, regardless of what it's actually visited (spec section
    /// 6/22 - destination diversity). This ranks by <see cref="LocationKnowledge.Familiarity"/> instead (never
    /// visited scores highest, matching spec section 6's own "known=true, familiarity=low" example), with a
    /// flat penalty for anywhere visited within <see cref="RecentVisitPenaltyWindow"/> so a just-left room
    /// doesn't immediately win again - randomness only breaks ties between otherwise equally-scored places
    /// (spec section 7), it's never the primary mechanism.
    /// </summary>
    public IReadOnlyList<string> GetExplorationCandidates(EntityUid uid, int max = 6, MemoryComponent? memory = null)
    {
        if (!Resolve(uid, ref memory, false))
            return Array.Empty<string>();

        TryComp<LocationKnowledgeComponent>(uid, out var knowledge);
        var now = _timing.CurTime;

        return memory.Memories
            .Where(m => (m.Source == "landmark" || m.Source == "search-result") && m.Subject is not null)
            .Select(m => m.Subject!)
            .Distinct()
            .Select(name =>
            {
                var familiarity = 0f;
                var recentlyVisited = false;

                if (knowledge is not null && knowledge.Places.TryGetValue(name, out var place))
                {
                    familiarity = place.Familiarity;
                    recentlyVisited = now - place.LastVisitedAt < RecentVisitPenaltyWindow;
                }

                var score = (1f - familiarity) - (recentlyVisited ? 1f : 0f) + _random.NextFloat(-0.01f, 0.01f);
                return (name, score);
            })
            .OrderByDescending(x => x.score)
            .Select(x => x.name)
            .Take(max)
            .ToList();
    }

    /// <summary>
    /// AI Players 0.6, spec section 21's "habits": the average <see cref="AiMemory.EmotionalWeight"/> of every
    /// memory whose <see cref="AiMemory.Subject"/> exactly matches <paramref name="placeName"/> (visit/landmark/
    /// social-location memories all carry a subject, but only visit memories currently stamp a real emotional
    /// weight - see <see cref="Systems.LandmarkPerceptionSystem.RecordVisit"/>) - positive means fond memories
    /// of the place, negative means bad ones, 0 (the default for a never-visited place) means neutral/unknown.
    /// Purely a query over existing memory state - no new stored value.
    /// </summary>
    public float GetLocationSentiment(EntityUid uid, string placeName, MemoryComponent? memory = null)
    {
        if (!Resolve(uid, ref memory, false))
            return 0f;

        var matching = memory.Memories.Where(m => m.Subject == placeName).ToList();
        return matching.Count == 0 ? 0f : matching.Average(m => m.EmotionalWeight);
    }

    IReadOnlyList<AiMemory> IMemoryRetriever.Recall(EntityUid uid, RecallQuery query, int max) => Recall(uid, query, max);

    /// <summary>
    /// AI Players 0.4 Milestone 10: memories relevant to <paramref name="query"/> rather than only "most
    /// important overall" - each memory's decayed importance (<see cref="GetEffectiveImportance"/>) plus a
    /// bonus for each part of the query it actually matches, highest total first. A deterministic scorer
    /// (spec: no vector database yet) - every memory is still included and ranked, just reordered, so an
    /// under-specified query degrades gracefully to importance order instead of returning nothing.
    /// </summary>
    public IReadOnlyList<AiMemory> Recall(EntityUid uid, RecallQuery query, int max = 10, MemoryComponent? memory = null)
    {
        if (!Resolve(uid, ref memory, false))
            return Array.Empty<AiMemory>();

        var now = _timing.CurTime;
        return memory.Memories
            .OrderByDescending(m => RelevanceScore(m, query, now))
            .ThenByDescending(m => m.Timestamp)
            .Take(max)
            .ToList();
    }

    private float RelevanceScore(AiMemory memory, RecallQuery query, TimeSpan now)
    {
        var score = GetEffectiveImportance(memory, now);

        if (query.Subject is { } subject && memory.Subject is { } memorySubject &&
            (memorySubject.Contains(subject, StringComparison.OrdinalIgnoreCase) ||
             subject.Contains(memorySubject, StringComparison.OrdinalIgnoreCase)))
        {
            score += 0.5f;
        }

        if (query.AboutEntity is { } about && memory.Participants.Contains(about))
            score += 0.5f;

        if (query.NearLocation is { } near && memory.Location is { } location &&
            location.TryDistance(_entManager, near, out var distance))
        {
            score += MathF.Max(0f, 0.3f - distance * 0.01f);
        }

        if (query.Keyword is { } keyword &&
            (memory.Content.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
             (memory.Subject?.Contains(keyword, StringComparison.OrdinalIgnoreCase) ?? false)))
        {
            score += 0.4f;
        }

        return score;
    }
}
