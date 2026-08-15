using System.Diagnostics.CodeAnalysis;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Marker for a validated parameter payload for one <see cref="IAiAction"/>. Deliberately not a raw
/// dictionary/string blob: every action defines its own strongly-typed params record, so there is no path
/// from "text the LLM produced" straight into arbitrary behaviour (spec section 19 - all parameters must be
/// validated).
/// </summary>
public interface IAiActionParams;

/// <summary>
/// One entry in the AI Players action registry (spec section 18). Each action owns its own
/// preconditions ("CanDo") and execution ("Do") - see <see cref="Systems.AiActionRegistrySystem"/> for the
/// TryDo wrapper that ties them together and is the actual entry point callers use.
/// </summary>
public interface IAiAction
{
    /// <summary>
    /// Unique, stable identifier used for lookup/whitelisting - e.g. by a future LLM action-suggestion path.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable summary, e.g. for a future action catalog shown to the LLM.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Pure feasibility check. Must not mutate game state (mirrors the CanDo half of this repo's
    /// OnEvent-&gt;TryDo-&gt;CanDo-&gt;Do rule).
    /// </summary>
    bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason);

    /// <summary>
    /// Performs the action. Only ever called after <see cref="CanDo"/> has returned true for the same
    /// parameters.
    /// </summary>
    void Do(EntityUid uid, IAiActionParams parameters);
}
