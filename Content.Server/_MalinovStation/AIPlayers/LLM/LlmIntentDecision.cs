namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// AI Players 0.4 Milestone 3: the Cognitive LLM role's response - "what matters to me, what do I want, which
/// broad category of action makes sense" (spec section 3's own phrasing). The first of the hierarchical
/// decision's two stages; unlike the old single-call <see cref="LlmCognitiveDecision"/>, this deliberately
/// never asks the LLM to enumerate a concrete action or its parameters - <see cref="Category"/> is one of
/// <see cref="Actions.AiActionCategories"/>, filtered up front to only categories with at least one currently
/// eligible action (see <see cref="Systems.AiActionRegistrySystem.GetEligibleCategories"/>). See
/// <see cref="LlmActionSelectionDecision"/> for the second stage, and
/// <see cref="Systems.LlmGatewaySystem.TryApplyCognitiveDecision"/> for where both stages' results get
/// synthesized back into a single <see cref="LlmCognitiveDecision"/> so every downstream consumer (Intent
/// write, ActionProposal resolution, action execution, trace/feedback) stays exactly as it was.
/// </summary>
/// <param name="Desire">Which felt desire (see <see cref="Components.Desire"/>) this decision commits to acting on.</param>
/// <param name="Intention">Free-form, illustrative only (e.g. "find_food", "meet_person") - never validated
/// against a closed vocabulary.</param>
/// <param name="Priority">0.0-1.0.</param>
/// <param name="Confidence">0.0-1.0: how sure the LLM is this is the right call.</param>
/// <param name="Reason">Short in-character justification for the intention/category, used for logging/debugging
/// and as the fallback reason if no second-stage action-selection call ends up being needed.</param>
/// <param name="Category">One of the categories offered - must be a currently-eligible
/// <see cref="Actions.AiActionCategories"/> value.</param>
public sealed record LlmIntentDecision(
    string Desire,
    string Intention,
    float Priority,
    float Confidence,
    string Reason,
    string Category);
