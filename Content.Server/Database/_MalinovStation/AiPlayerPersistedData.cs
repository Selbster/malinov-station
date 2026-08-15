namespace Content.Server.Database;

/// <summary>
/// Gameplay-facing shape of a saved AI player, decoupled from the EF entity types
/// (<c>AiPlayerPersonality</c>/<c>AiPlayerMemoryRecord</c>) so <c>Content.Server._MalinovStation.AIPlayers</c>
/// code doesn't need to depend on the database model directly.
/// </summary>
public sealed record AiPlayerPersistedData(
    string PersistentId,
    float Sociability,
    float Courage,
    float Curiosity,
    float Laziness,
    float Greed,
    float Aggression,
    float Loyalty,
    float RiskTolerance,
    float AuthorityRespect,
    float Professionalism,
    float Empathy,
    float Honesty,
    float Impulsiveness,
    IReadOnlyList<AiPlayerPersistedMemory> Memories);

public sealed record AiPlayerPersistedMemory(
    DateTime Timestamp,
    float Importance,
    float EmotionalWeight,
    string Source,
    string Content);
