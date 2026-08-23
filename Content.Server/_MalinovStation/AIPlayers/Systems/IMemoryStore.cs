using Content.Server._MalinovStation.AIPlayers.Components;
using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// AI Players 0.4 Milestone 7: the write side of <see cref="MemorySystem"/>, extracted behind an interface so
/// a future alternative implementation (e.g. backed by something other than <see cref="MemoryComponent"/>) is
/// swappable without touching every call site - additive only, <see cref="MemorySystem"/> itself is still the
/// only implementation and every existing call site keeps calling it directly, unchanged. Deliberately does
/// NOT add a "give me everything" read method - <see cref="MemorySystem"/>'s own doc comment is explicit that
/// retrieval stays narrow/queried (see <see cref="IMemoryRetriever"/> instead), and this milestone has no
/// reason to relax that.
/// </summary>
public interface IMemoryStore
{
    /// <summary>Records a new memory. No-ops (returns null) if the entity has no <see cref="MemoryComponent"/>.
    /// Mirrors <see cref="MemorySystem.AddMemory"/>'s contract exactly, minus the internal
    /// <c>MemoryComponent?</c> resolve-caching parameter that's an ECS-lookup optimization for callers that
    /// already have the component in hand, not part of the abstract contract.</summary>
    AiMemory? AddMemory(
        EntityUid uid,
        string content,
        float importance,
        string source,
        IReadOnlyList<EntityUid>? participants = null,
        EntityCoordinates? location = null,
        float emotionalWeight = 0f,
        string? subject = null);
}
