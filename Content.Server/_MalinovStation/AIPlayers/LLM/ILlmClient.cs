using System.Threading;
using System.Threading.Tasks;

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

    /// <summary>AI Players 2.0 Milestone 1: the LLM Cognitive Layer's decision, from a full
    /// <see cref="CognitiveState"/> rather than the narrower <see cref="AiContext"/>.</summary>
    Task<LlmCognitiveDecision?> DecideCognitiveAsync(CognitiveState context, IReadOnlyCollection<string> allowedIntents, CancellationToken cancellationToken);
}
