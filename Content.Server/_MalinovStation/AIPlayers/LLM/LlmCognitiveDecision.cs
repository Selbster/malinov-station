namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// A validated, structured decision from the LLM Cognitive Layer - richer than <see cref="LlmDecision"/>.
/// Runs parallel to <see cref="LlmDecision"/>, not instead of it - see
/// <see cref="Systems.LlmGatewaySystem.TryApplyCognitiveDecision"/>. AI Players 0.3 splits this into two
/// independent halves: <see cref="Intention"/> (the AI's own free-form self-narration, never validated
/// against a closed vocabulary - see <see cref="Components.IntentComponent"/>) and <see cref="Action"/>/
/// <see cref="ActionParameters"/> (what concrete, whitelisted thing to actually do right now - see
/// <see cref="ActionProposalResolver"/>, the schema-validation boundary between this raw LLM output and a
/// real <see cref="ActionProposal"/>).
/// </summary>
/// <param name="Desire">Which felt desire (see <see cref="Components.Desire"/>) this decision commits to acting on.</param>
/// <param name="Intention">Free-form, illustrative only (e.g. "find_food", "meet_person") - never validated
/// against a closed vocabulary. Only <see cref="Action"/>/<see cref="ActionParameters"/> are schema-checked,
/// since those are what actually executes something.</param>
/// <param name="Priority">0.0-1.0.</param>
/// <param name="Confidence">0.0-1.0: how sure the LLM is this is the right call.</param>
/// <param name="Reason">Short in-character justification, used for logging/debugging.</param>
/// <param name="Action">Name of one of the small set of LLM-selectable actions (see
/// <see cref="ActionProposalResolver"/>) - e.g. "PursueGoal", "ContinueActivity".</param>
/// <param name="ActionParameters">Raw string parameters for <see cref="Action"/> - e.g. <c>{"goal": "Rest"}</c>
/// for "PursueGoal". Validated/converted into a strongly-typed
/// <see cref="Content.Server._MalinovStation.AIPlayers.Actions.IAiActionParams"/> by
/// <see cref="ActionProposalResolver"/>, never passed straight into an action.</param>
public sealed record LlmCognitiveDecision(
    string Desire,
    string Intention,
    float Priority,
    float Confidence,
    string Reason,
    string Action,
    IReadOnlyDictionary<string, string> ActionParameters);
