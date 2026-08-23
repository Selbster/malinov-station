using System.Threading;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Actions;

namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// Backend-agnostic interface for asking an LLM to make a decision or generate a line of dialogue for an AI
/// player. Implementations must never throw: on any failure (network, timeout, malformed response) return
/// null so callers can fall back to non-LLM behaviour (the ordinary HTN/Goal System, or a canned line
/// dataset), per spec section 17/30 - AI players must keep working without the LLM.
/// </summary>
public interface ILlmClient
{
    /// <param name="allowedIntents">Every intent name the caller will actually accept (see
    /// <see cref="Systems.LlmGatewaySystem.TryApplyDecision"/>'s whitelist) - told to the LLM so it never
    /// proposes something outside it, including data-driven professional goals the fixed
    /// <see cref="Components.AIGoals.All"/> vocabulary alone doesn't cover.</param>
    Task<LlmDecision?> DecideAsync(AiContext context, IReadOnlyCollection<string> allowedIntents, CancellationToken cancellationToken);

    Task<string?> GenerateLineAsync(DialogueContext context, CancellationToken cancellationToken);

    /// <summary>AI Players 2.0 Milestone 1's original single-call combined decision. No production caller left
    /// as of AI Players 0.4 (see <see cref="CognitiveResponseParser"/>'s own doc comment for why it's kept
    /// anyway) - <see cref="DecideIntentAsync"/>/<see cref="SelectActionAsync"/> are what
    /// <see cref="Systems.LlmGatewaySystem"/> actually drives now.</summary>
    Task<LlmCognitiveDecision?> DecideCognitiveAsync(CognitiveState context, IReadOnlyCollection<string> allowedIntents, CancellationToken cancellationToken);

    /// <summary>AI Players 0.4 Milestone 3: the hierarchical decision's first stage - "what do I want, which
    /// category of action makes sense." <paramref name="eligibleCategories"/> is the deterministic pre-filter
    /// (see <see cref="Systems.AiActionRegistrySystem.GetEligibleCategories"/>) - the LLM only ever sees
    /// categories with at least one currently-possible action.</summary>
    Task<LlmIntentDecision?> DecideIntentAsync(CognitiveState context, IReadOnlyCollection<string> allowedIntents, IReadOnlyCollection<string> eligibleCategories, CancellationToken cancellationToken);

    /// <summary>AI Players 0.4 Milestone 3: the hierarchical decision's second stage - "given these eligible
    /// actions, which one best accomplishes the intent." Only ever called when there's a genuine choice among
    /// <paramref name="eligibleActions"/> (see <see cref="Systems.LlmGatewaySystem.TryApplyCognitiveDecision"/>
    /// for the single-eligible-action skip case).</summary>
    Task<LlmActionSelectionDecision?> SelectActionAsync(CognitiveState context, LlmIntentDecision intent, IReadOnlyList<IAiAction> eligibleActions, CancellationToken cancellationToken);
}
