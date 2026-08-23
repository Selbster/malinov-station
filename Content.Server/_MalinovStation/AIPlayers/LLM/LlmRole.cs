namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// AI Players 0.4 Milestone 4: logical LLM roles - architectural separation only, not a requirement for
/// distinct physical models. Every role currently falls back to the single <c>ai_players.llm.model</c> CVar
/// unless its own <c>ai_players.llm.model.*</c> override is set (see <see cref="Content.Shared._MalinovStation.AIPlayers.MalinovAiPlayerCVars"/>),
/// so nothing breaks for an existing deployment that never configures per-role models. Later, a real deployment
/// can point <see cref="CognitiveDecision"/> at a larger model and <see cref="ActionSelection"/>/
/// <see cref="MemoryEvaluation"/> at smaller/faster ones without any code change.
/// </summary>
public enum LlmRole
{
    /// <summary>Stage 1 of the hierarchical decision (<see cref="LlmIntentDecision"/>) and the legacy
    /// single-call <see cref="LlmCognitiveDecision"/>/<see cref="LlmDecision"/> paths.</summary>
    CognitiveDecision,

    /// <summary>Stage 2 of the hierarchical decision (<see cref="LlmActionSelectionDecision"/>).</summary>
    ActionSelection,

    /// <summary>Reserved for future ambiguous-event memory-importance scoring (spec Milestone 8) - the role
    /// exists end-to-end (CVar, model resolution) but has no caller yet; deterministic scoring covers every
    /// case handled so far, so nothing currently requests this role.</summary>
    MemoryEvaluation,

    /// <summary>Conversation line generation (<see cref="ILlmClient.GenerateLineAsync"/>).</summary>
    Dialogue,
}
