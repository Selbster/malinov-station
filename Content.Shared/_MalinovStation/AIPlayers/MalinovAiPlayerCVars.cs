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

    /// <summary>
    /// AI Players 0.4 Milestone 4: per-<see cref="LLM.LlmRole"/> model overrides. Each defaults to empty,
    /// meaning "fall back to <see cref="AiPlayersLlmModel"/>" - so an existing deployment that never sets these
    /// keeps using one model for everything, exactly as before, while a deployment that wants
    /// CognitiveDecision on a larger model and ActionSelection/Dialogue on smaller/faster ones can do so
    /// without any code change. See <c>HttpLlmClient</c>'s role-to-model resolution.
    /// </summary>
    public static readonly CVarDef<string> AiPlayersLlmModelCognitiveDecision =
        CVarDef.Create("ai_players.llm.model.cognitive_decision", string.Empty, CVar.SERVERONLY);

    public static readonly CVarDef<string> AiPlayersLlmModelActionSelection =
        CVarDef.Create("ai_players.llm.model.action_selection", string.Empty, CVar.SERVERONLY);

    public static readonly CVarDef<string> AiPlayersLlmModelMemoryEvaluation =
        CVarDef.Create("ai_players.llm.model.memory_evaluation", string.Empty, CVar.SERVERONLY);

    public static readonly CVarDef<string> AiPlayersLlmModelDialogue =
        CVarDef.Create("ai_players.llm.model.dialogue", string.Empty, CVar.SERVERONLY);

    /// <summary>
    /// Raised from an original 8s: a live test against a local qwen3:1.7b (thinking enabled by default even
    /// at that size) via Ollama saw every single cognitive-decision request - the heaviest prompt built, with
    /// Desires/Beliefs/Memory/nearby items all included - hit this timeout and get cancelled, so the AI never
    /// got a real decision the entire run. Still trivially overridable per-deployment for a fast remote API.
    /// </summary>
    public static readonly CVarDef<float> AiPlayersLlmTimeoutSeconds =
        CVarDef.Create("ai_players.llm.timeout_seconds", 30f, CVar.SERVERONLY);

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
    /// AI Players 2.0 Milestone 1 master switch for the LLM Cognitive Layer. Independent of
    /// <see cref="AiPlayersLlmEnabled"/> (which gates the LLM gateway entirely) - even an AI player spawned
    /// with cognitive mode does nothing extra while this is false. Belt-and-suspenders alongside cognitive
    /// mode being opt-in per entity (see <c>aiplayer_spawn_cognitive</c>): both must be true for any of the
    /// new cognitive behaviour to run.
    /// </summary>
    public static readonly CVarDef<bool> AiPlayersCognitiveEnabled =
        CVarDef.Create("ai_players.cognitive.enabled", false, CVar.SERVERONLY);

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

    /// <summary>
    /// AI Players 0.4 Milestone 9: half-life (in seconds) for a memory's effective importance to decay by
    /// half, computed at retrieval time only - the stored <c>AiMemory.Importance</c> itself is never mutated
    /// (spec: "do not simply delete old memories immediately"). 1800s (30 minutes) by default - long enough
    /// that a memory stays meaningfully influential for a good chunk of a round, short enough that something
    /// from hours ago genuinely fades relative to something fresh.
    /// </summary>
    public static readonly CVarDef<float> AiPlayersMemoryDecayHalfLifeSeconds =
        CVarDef.Create("ai_players.memory.decay_half_life_seconds", 1800f, CVar.SERVERONLY);
}
