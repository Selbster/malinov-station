namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// A validated, structured decision from the LLM Cognitive Layer (AI Players 2.0 Milestone 1) - richer than
/// <see cref="LlmDecision"/>, but still ultimately resolves to the same goal vocabulary
/// (<see cref="Components.AIGoals.All"/>/<see cref="Prototypes.AiProfessionalGoalPrototype"/>) HTN execution
/// reads, since HTN stays the executor this milestone (spec section 22). Runs parallel to
/// <see cref="LlmDecision"/>, not instead of it - see <see cref="Systems.LlmGatewaySystem.TryApplyCognitiveDecision"/>.
/// </summary>
/// <param name="Desire">Which felt desire (see <see cref="Components.Desire"/>) this decision commits to acting on.</param>
/// <param name="Intention">Must be one of <see cref="Components.AIGoals.All"/> or a loaded
/// <see cref="Prototypes.AiProfessionalGoalPrototype"/> id; anything else is rejected.</param>
/// <param name="Priority">0.0-1.0.</param>
/// <param name="Confidence">0.0-1.0: how sure the LLM is this is the right call.</param>
/// <param name="Reason">Short in-character justification, used for logging/debugging.</param>
public sealed record LlmCognitiveDecision(string Desire, string Intention, float Priority, float Confidence, string Reason);
