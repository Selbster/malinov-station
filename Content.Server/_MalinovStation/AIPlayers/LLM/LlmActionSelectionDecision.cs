namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// AI Players 0.4 Milestone 3: the Action Selection LLM role's response - "given these eligible actions, which
/// one best accomplishes the intent" (spec section 3's own phrasing), the second of the hierarchical
/// decision's two stages. Only requested when <see cref="LlmIntentDecision.Category"/>'s eligible-action set
/// has a genuine choice to make (more than one possibility) - see
/// <see cref="Systems.LlmGatewaySystem.TryApplyCognitiveDecision"/> for the single-eligible-action skip case
/// that avoids this call entirely. <see cref="Action"/> must be one of the specific eligible actions this
/// stage was offered, re-checked by the gateway (not just trusted) before anything executes.
/// </summary>
/// <param name="Action">Name of one of the eligible actions offered for this request.</param>
/// <param name="ActionParameters">Raw string parameters for <see cref="Action"/> - e.g. <c>{"target": "toolbox"}</c>
/// for "PickUpItem". Validated/converted into a strongly-typed
/// <see cref="Actions.IAiActionParams"/> by <see cref="ActionProposalResolver"/>, never passed straight into an
/// action.</param>
/// <param name="Reason">Short in-character justification for this specific pick.</param>
public sealed record LlmActionSelectionDecision(
    string Action,
    IReadOnlyDictionary<string, string> ActionParameters,
    string Reason);
