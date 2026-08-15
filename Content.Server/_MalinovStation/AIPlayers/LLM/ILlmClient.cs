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
    Task<LlmDecision?> DecideAsync(AiContext context, CancellationToken cancellationToken);

    Task<string?> GenerateLineAsync(DialogueContext context, CancellationToken cancellationToken);
}
