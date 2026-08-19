using Content.Server._MalinovStation.AIPlayers.Actions;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// A validated action+params pair a cognitive decision has committed to (AI Players 0.3, spec section 4) -
/// the output of checking an <see cref="LlmCognitiveDecision"/>'s raw <see cref="LlmCognitiveDecision.Action"/>/
/// <see cref="LlmCognitiveDecision.ActionParameters"/> against the small, closed set of LLM-selectable actions
/// (see <see cref="ActionProposalResolver"/>), before <see cref="Systems.AiActionRegistrySystem"/>'s own
/// CanDo/Do get a chance to run.
/// </summary>
public sealed record ActionProposal(string ActionName, IAiActionParams Parameters);
