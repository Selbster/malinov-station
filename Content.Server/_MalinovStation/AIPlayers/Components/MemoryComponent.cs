using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// A single episodic/semantic/social memory entry. See <see cref="Systems.MemorySystem"/> for how these get
/// created, capped and retrieved.
/// </summary>
public sealed class AiMemory
{
    public Guid Id = Guid.NewGuid();
    public TimeSpan Timestamp;

    /// <summary>
    /// Normalized 0 (trivial, e.g. walked past someone) to 1 (life-altering, e.g. someone saved my life).
    /// </summary>
    public float Importance;

    /// <summary>
    /// What produced this memory, e.g. "perception", "conversation". Kept as a plain string for now; may
    /// become an enum once enough sources exist to enumerate.
    /// </summary>
    public string Source = string.Empty;

    public List<EntityUid> Participants = new();
    public EntityCoordinates? Location;
    public string Content = string.Empty;

    /// <summary>
    /// Structured name this memory is about, e.g. a landmark's own display text ("Kitchen") - distinct from
    /// <see cref="Content"/>'s natural-language prose so a consumer (see
    /// <see cref="Systems.MemorySystem.FindKnownLocation"/>) doesn't need to parse a name back out of a
    /// sentence. Null for memories with no single structured subject.
    /// </summary>
    public string? Subject;

    /// <summary>
    /// -1 (very negative) to 1 (very positive). Distinct from Importance: a memory can be important and
    /// emotionally neutral, or trivial and either good or bad.
    /// </summary>
    public float EmotionalWeight;
}

/// <summary>
/// An AI player's long-term memory store. Deliberately not sent to the LLM wholesale (see
/// <see cref="Systems.MemorySystem"/> retrieval helpers) once LLM integration exists.
/// </summary>
[RegisterComponent]
public sealed partial class MemoryComponent : Component
{
    [ViewVariables]
    public List<AiMemory> Memories = new();

    /// <summary>
    /// Once exceeded, the least important (then oldest) memory is dropped to make room. Raised from the
    /// original 200 - pre-seeding every station beacon as a known landmark at spawn
    /// (LandmarkPerceptionSystem.SeedKnownBeacons) alone can use a real chunk of a small cap, before any
    /// actual play-derived memories accumulate on top.
    /// </summary>
    [DataField]
    public int MaxMemories = 1500;
}
