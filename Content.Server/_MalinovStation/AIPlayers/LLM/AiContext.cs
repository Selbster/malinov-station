namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// One other character the AI player can currently see, plus what the AI player itself knows/feels about
/// them (relationship values default to neutral zero if never recorded). Never includes information the
/// character couldn't plausibly know.
/// </summary>
public sealed record PerceivedCharacterContext(
    string Name,
    float Trust,
    float Respect,
    float Fear,
    float Friendship,
    string? RelevantMemory);

/// <summary>
/// Exactly what gets sent to the LLM for one decision: character identity, personality, needs, current
/// goal, and who/what it can currently perceive. Deliberately excludes the wider ECS/map state, other
/// characters' private data, and round history (see spec section 16).
/// </summary>
public sealed record AiContext(
    string Name,
    string Job,
    string PersonalitySummary,
    float Fatigue,
    float Stress,
    float SocialNeed,
    string CurrentGoal,
    float CurrentGoalPriority,
    string CurrentGoalReason,
    IReadOnlyList<PerceivedCharacterContext> VisibleCharacters);
