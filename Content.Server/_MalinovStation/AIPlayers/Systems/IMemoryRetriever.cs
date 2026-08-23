using Content.Server._MalinovStation.AIPlayers.Components;
using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// AI Players 0.4 Milestone 10: what the current situation is about, for <see cref="IMemoryRetriever.Recall"/>
/// to score relevance against - deliberately loose/partial (every field optional) since callers rarely know
/// all of them at once. A deterministic scorer for now (spec: "no vector database yet"), but the interface
/// itself doesn't assume that - a future implementation could swap in embeddings-based similarity without
/// changing this query shape or any caller.
/// </summary>
/// <param name="Subject">A structured name to match against <see cref="AiMemory.Subject"/> (e.g. a landmark or
/// item name), if known.</param>
/// <param name="AboutEntity">A specific entity the situation concerns, matched against
/// <see cref="AiMemory.Participants"/>.</param>
/// <param name="NearLocation">Weights memories whose own <see cref="AiMemory.Location"/> is close to this.</param>
/// <param name="Keyword">Free-text overlap against <see cref="AiMemory.Content"/>/<see cref="AiMemory.Subject"/>.</param>
public sealed record RecallQuery(
    string? Subject = null,
    EntityUid? AboutEntity = null,
    EntityCoordinates? NearLocation = null,
    string? Keyword = null);

/// <summary>
/// AI Players 0.4 Milestone 10: the read side of <see cref="MemorySystem"/> that answers "give me the memories
/// relevant to the current situation" rather than only "give me the most important ones" - additive, not a
/// replacement for <see cref="MemorySystem.GetMostImportant"/>/<see cref="MemorySystem.GetMemoriesAbout"/>/
/// <see cref="MemorySystem.FindKnownLocation"/>, which stay exactly as they are for their own existing callers.
/// </summary>
public interface IMemoryRetriever
{
    /// <summary>Memories scored by relevance to <paramref name="query"/> (see <see cref="MemorySystem.Recall"/>
    /// for the actual scoring formula), highest first, capped at <paramref name="max"/>.</summary>
    IReadOnlyList<AiMemory> Recall(EntityUid uid, RecallQuery query, int max = 10);
}
