using System.Diagnostics.CodeAnalysis;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Explicitly confirms the current activity, with no change (AI Players 0.3) - lets a cognitive decision that
/// concludes "keep doing what I'm already doing" flow through the same ActionProposal/ActionRegistry pipeline
/// as every other action, instead of being a special case the LLM has no way to express.
/// </summary>
public sealed class ContinueActivityAction : IAiAction
{
    public const string ActionName = "ContinueActivity";

    public string Name => ActionName;
    public string Description => "Confirm the current activity - no change.";

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not ContinueActivityActionParams)
        {
            failReason = $"{Name} requires {nameof(ContinueActivityActionParams)}.";
            return false;
        }

        failReason = null;
        return true;
    }

    public void Do(EntityUid uid, IAiActionParams parameters)
    {
        // Deliberate no-op.
    }
}
