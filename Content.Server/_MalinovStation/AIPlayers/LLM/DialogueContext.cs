namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Exactly what gets sent to the LLM to generate one line of an AI player's side of a short conversation
/// (see <see cref="Systems.SocialSystem"/>). Distinct from <see cref="AiContext"/> (which is for goal
/// decisions): needs/current-goal aren't relevant to what someone says in a two-line greeting, but who
/// they're talking to and how they feel about them are.
/// </summary>
/// <param name="LinePartnerJustSaid">Null if the speaker is opening the conversation.</param>
/// <param name="RumorToShare">
/// Non-null when the speaker has something important and unshared to relay instead of small talk (Milestone
/// 7's rumor spreading - see <see cref="Systems.SocialSystem"/>).
/// </param>
public sealed record DialogueContext(
    string SpeakerName,
    string SpeakerPersonalitySummary,
    string PartnerName,
    float Trust,
    float Respect,
    float Friendship,
    float Fear,
    string? RelevantMemory,
    string? LinePartnerJustSaid,
    string? RumorToShare = null);
