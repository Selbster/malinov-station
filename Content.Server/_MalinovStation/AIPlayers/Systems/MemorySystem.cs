using System.Linq;
using Content.Server._MalinovStation.AIPlayers.Components;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Creates and retrieves <see cref="AiMemory"/> entries on <see cref="MemoryComponent"/>. Retrieval is
/// deliberately narrow (about one entity, or the N most important) rather than "give me everything" so
/// callers (HTN operators today, LLM context building later) can't accidentally dump the whole store.
/// </summary>
public sealed partial class MemorySystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

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
    /// The most important memories overall, highest first.
    /// </summary>
    public IReadOnlyList<AiMemory> GetMostImportant(EntityUid uid, int max = 10, MemoryComponent? memory = null)
    {
        if (!Resolve(uid, ref memory, false))
            return Array.Empty<AiMemory>();

        return memory.Memories
            .OrderByDescending(m => m.Importance)
            .ThenByDescending(m => m.Timestamp)
            .Take(max)
            .ToList();
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
}
