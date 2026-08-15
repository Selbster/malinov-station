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
        MemoryComponent? memory = null)
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
}
