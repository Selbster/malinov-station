using Robust.Shared;
using Robust.Shared.Configuration;

namespace Content.Shared._MalinovStation.AIPlayers;

/// <summary>
/// CVars for the AI Players subsystem's LLM gateway (Milestone 4). Kept in a separate [CVarDefs] class per
/// the "NOTICE FOR FORKS" comment on <c>CCVars</c> instead of touching the vanilla file.
/// </summary>
[CVarDefs]
public sealed class MalinovAiPlayerCVars : CVars
{
    /// <summary>
    /// Master switch. When false, AI players run on the HTN/Goal System alone and never call out to an LLM.
    /// </summary>
    public static readonly CVarDef<bool> AiPlayersLlmEnabled =
        CVarDef.Create("ai_players.llm.enabled", false, CVar.SERVERONLY);

    /// <summary>
    /// OpenAI-compatible chat completions endpoint (works for OpenAI itself, Ollama's compatible API, etc.).
    /// </summary>
    public static readonly CVarDef<string> AiPlayersLlmEndpoint =
        CVarDef.Create("ai_players.llm.endpoint", string.Empty, CVar.SERVERONLY);

    /// <summary>
    /// Never committed to the repo - set via server config/environment. Hidden from cvar completions.
    /// </summary>
    public static readonly CVarDef<string> AiPlayersLlmApiKey =
        CVarDef.Create("ai_players.llm.api_key", string.Empty, CVar.SERVERONLY | CVar.CONFIDENTIAL);

    public static readonly CVarDef<string> AiPlayersLlmModel =
        CVarDef.Create("ai_players.llm.model", string.Empty, CVar.SERVERONLY);

    public static readonly CVarDef<float> AiPlayersLlmTimeoutSeconds =
        CVarDef.Create("ai_players.llm.timeout_seconds", 8f, CVar.SERVERONLY);

    /// <summary>
    /// Caps how many LLM requests may be in flight across all AI players at once.
    /// </summary>
    public static readonly CVarDef<int> AiPlayersLlmMaxConcurrentRequests =
        CVarDef.Create("ai_players.llm.max_concurrent_requests", 2, CVar.SERVERONLY);

    /// <summary>
    /// Minimum time between LLM requests for the same AI player.
    /// </summary>
    public static readonly CVarDef<float> AiPlayersLlmDecisionCooldownSeconds =
        CVarDef.Create("ai_players.llm.decision_cooldown_seconds", 30f, CVar.SERVERONLY);

    /// <summary>
    /// How long an LLM-provided goal stays in effect before the Goal System resumes normal control.
    /// </summary>
    public static readonly CVarDef<float> AiPlayersLlmOverrideDurationSeconds =
        CVarDef.Create("ai_players.llm.override_duration_seconds", 60f, CVar.SERVERONLY);

    /// <summary>
    /// Master switch for Milestone 9's cross-round persistence. When false, AI players never read or write
    /// the database, even if spawned with a PersistentId - matches spec section 26's "don't make persistence
    /// mandatory for the MVP."
    /// </summary>
    public static readonly CVarDef<bool> AiPlayersPersistenceEnabled =
        CVarDef.Create("ai_players.persistence.enabled", false, CVar.SERVERONLY);

    /// <summary>
    /// How many of an AI player's most important memories get saved (and restored) per PersistentId.
    /// </summary>
    public static readonly CVarDef<int> AiPlayersPersistenceMaxMemories =
        CVarDef.Create("ai_players.persistence.max_memories", 20, CVar.SERVERONLY);
}
