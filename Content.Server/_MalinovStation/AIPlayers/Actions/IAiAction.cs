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
///
/// AI Players 0.4's hierarchical action framework adds three more members (<see cref="Category"/>,
/// <see cref="IsEligible"/>, <see cref="IsExtended"/>) on top of the original CanDo/Do pair. They answer a
/// different question than CanDo does: CanDo validates one *specific already-chosen* parameter set (e.g. "is
/// this exact known location reachable"); <see cref="IsEligible"/> answers "is at least one instance of this
/// action possible right now at all", with no parameters in hand yet - the deterministic pre-filter that
/// decides whether this action is even offered to the LLM in the first place (see
/// <see cref="Systems.AiActionRegistrySystem.GetEligibleActions"/>), instead of the LLM finding out post-hoc
/// via a CanDo rejection.
/// </summary>
public interface IAiAction
{
    /// <summary>
    /// Unique, stable identifier used for lookup/whitelisting - e.g. by a future LLM action-suggestion path.
    /// </summary>
    string Name { get; }

    /// <summary>
    /// Human-readable summary, shown to the Action Selection LLM role alongside this action's name once it's
    /// eligible - see <see cref="LLM.PromptBuilder.BuildActionSelectionUserPrompt"/>.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// One of <see cref="AiActionCategories"/> - the coarse group the Cognitive LLM role picks between before
    /// any concrete action is chosen (AI Players 0.4 Milestone 1).
    /// </summary>
    string Category { get; }

    /// <summary>
    /// Whether performing this action hands the actor off to extended, multi-tick execution (HTN movement/goal
    /// pursuit) rather than resolving in one tick - see <see cref="Components.AiBusyStateComponent"/>, which
    /// only ever gets populated for an <c>IsExtended</c> action.
    /// </summary>
    bool IsExtended { get; }

    /// <summary>
    /// AI Players 0.6.2: whether the LLM is allowed to pick this action by name. Defaults to true; an action
    /// opts out when its parameters cannot meaningfully be produced by a language model - today only
    /// <see cref="MoveToAction"/>, which needs raw <see cref="EntityCoordinates"/> and is driven
    /// programmatically. Such an action stays fully usable through
    /// <see cref="Systems.AiActionRegistrySystem.TryDoAction"/>; it simply stops being advertised as a choice.
    /// Without this it was still listed to the model, still counted toward its category being offered, and
    /// then rejected by <see cref="LLM.ActionProposalResolver"/> whenever the model actually picked it -
    /// burning a whole decision cycle on an option that could never execute.
    /// </summary>
    bool IsLlmSelectable => true;

    /// <summary>
    /// Deterministic pre-filter: "could this actor perform *some* instance of this action right now", with no
    /// specific target/parameters chosen yet - reusing whatever passive candidate data (opportunity components,
    /// known-location memory, etc.) this action's own <see cref="CanDo"/> already consults. Must not mutate
    /// game state, and must be cheap enough to run for every registered action, every eligibility pass (spec
    /// Milestone 2: "use code for entity existence/component state/distance/... - use LLM reasoning only when
    /// the question is genuinely cognitive"). An ineligible action is simply omitted from what the LLM sees -
    /// unlike a CanDo rejection, this is never reported back as a failure, since it was never proposed.
    /// </summary>
    bool IsEligible(EntityUid uid);

    /// <summary>
    /// Pure feasibility check for one specific, already-chosen parameter set. Must not mutate game state
    /// (mirrors the CanDo half of this repo's OnEvent-&gt;TryDo-&gt;CanDo-&gt;Do rule).
    /// </summary>
    bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason);

    /// <summary>
    /// Performs the action. Only ever called after <see cref="CanDo"/> has returned true for the same
    /// parameters.
    ///
    /// AI Players 0.6.2: returns an <see cref="AiActionResult"/> rather than <c>void</c>. CanDo answers "are
    /// these parameters feasible" before anything happens; this answers "what actually became of it" - the
    /// distinction matters for any action whose target is only chosen during execution (exploration picks its
    /// own destination), where passing CanDo is no guarantee there was anything to do. A non-success result is
    /// real feedback: <see cref="Systems.AiActionRegistrySystem.TryDoAction"/> refuses to mark the actor busy,
    /// and <see cref="Systems.AiTraceSystem.ActionFailed"/> turns it into the outcome memory the LLM reads on
    /// its next reflection.
    /// </summary>
    AiActionResult Do(EntityUid uid, IAiActionParams parameters);
}
