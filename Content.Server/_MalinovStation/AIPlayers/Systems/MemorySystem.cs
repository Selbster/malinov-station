using System.Linq;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Shared._MalinovStation.AIPlayers;
using Robust.Shared.Configuration;
using Robust.Shared.Map;
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

        return memory.Memories
            .Where(m => (m.Source == "landmark" || m.Source == "search-result") && m.Location is not null && m.Subject is { } subject &&
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
