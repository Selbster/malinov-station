using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

public sealed partial class AiTraceSystem
{
    /// <summary>Called once after a decision claims its terminal result. Cancellation is feedback, not failure.</summary>
    private bool PublishActionResult(EntityUid uid, string action, AiActionResult result)
    {
        if (!TryComp<CognitiveModeComponent>(uid, out var cognitive))
            return false;

        if (result.Outcome is not (AiActionOutcome.Completed or AiActionOutcome.Cancelled))
            return ActionFailed(uid, action, result.Reason);

        var content = $"Действие {action}: {result.Reason}";
        var memory = _memory.AddMemory(uid, content: content,
            importance: MemoryImportanceScorer.Score("outcome", content: content), source: "outcome");
        cognitive.ReflectionAccumulator = 0f;
        return memory is not null;
    }
}
