using Content.Server._MalinovStation.AIPlayers.Components;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>Physiological pressure - <see cref="NeedsComponent"/> plus vanilla hunger/thirst (which live on
/// SatiationComponent, not NeedsComponent - see NeedsComponent's own doc comment), projected to the same
/// booleans GoalSystem itself reacts to rather than raw satiation values.</summary>
public sealed record CognitiveNeeds(float Fatigue, float Stress, float Safety, float SocialNeed, bool IsHungry, bool IsThirsty);

/// <summary>An AI player's current mood - see <see cref="EmotionComponent"/> for what drives each field.</summary>
public sealed record CognitiveEmotion(float Fear, float Anger, float Sadness, float Anxiety, float Joy, float Confidence);

/// <summary>One other character the AI player can currently see, plus everything it knows/feels about them -
/// the same information <see cref="PerceivedCharacterContext"/> carries, extended with Anger/Loyalty (computed
/// today but never forwarded to the legacy context).</summary>
public sealed record CognitivePerceivedCharacter(
    string Name, float Trust, float Respect, float Fear, float Friendship, float Anger, float Loyalty, string? RelevantMemory);

/// <summary>One belief, as surfaced to the LLM - see <see cref="AiBelief"/> for the full record this is summarized from.</summary>
public sealed record BeliefSummary(string Content, float Confidence, string Source);

/// <summary>
/// A cognitive-mode AI player's compact view of itself and its situation - the LLM Cognitive Layer's input
/// (spec section 4/8). Extends what <see cref="AiContext"/> already assembles (identity, personality, needs,
/// current goal, visible characters) with Emotion, plural Desires, a richer Intent, and Beliefs distinct from
/// ground-truth Memory. Built by <see cref="Systems.ContextBuilderSystem.BuildCognitiveState"/>, which is
/// still the single "don't leak hidden information" enforcement point this and <see cref="AiContext"/> share -
/// everything here traces back to <see cref="PerceptionComponent.LastObservation"/> plus
/// <see cref="Systems.MemorySystem"/>/<see cref="Systems.BeliefSystem"/>/<see cref="Systems.RelationshipSystem"/>
/// lookups, never a fresh omniscient query.
///
/// There is deliberately no "UnknownInformation" field: enumerating what the AI doesn't know would itself
/// require an omniscient query, which is exactly what this type exists to avoid. The cognitive system prompt
/// states outright that anything not listed here is unknown to the character; that boundary is proven by test
/// (the AI never reacting to an unperceived event), not by a data field.
/// </summary>
public sealed record CognitiveState(
    string Name,
    string Job,
    string PersonalitySummary,
    CognitiveNeeds Needs,
    CognitiveEmotion Emotion,
    string CurrentActivity,
    IReadOnlyList<Desire> CurrentDesires,
    Desire CurrentIntent,
    float IntentConfidence,
    IReadOnlyList<CognitivePerceivedCharacter> VisibleWorld,
    IReadOnlyList<string> RelevantMemories,
    IReadOnlyList<string> KnownFacts,
    IReadOnlyList<BeliefSummary> Beliefs,
    IReadOnlyList<string> KnownLocations,
    IReadOnlyList<string> NearbyInteractables,
    IReadOnlyList<string> NearbyItems);
