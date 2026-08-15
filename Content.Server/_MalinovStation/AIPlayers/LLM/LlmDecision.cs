namespace Content.Server._MalinovStation.AIPlayers.LLM;

/// <summary>
/// A validated, structured decision from an LLM: which goal/intent the character should pursue and why.
/// This intentionally mirrors the same vocabulary <see cref="Components.GoalComponent"/> already uses
/// (see <see cref="Components.AIGoals"/>) - the LLM answers "what goal should this character have," it does
/// not execute anything directly (see spec section 2's architecture split).
/// </summary>
/// <param name="Intent">Must be one of <see cref="Components.AIGoals.All"/>; anything else is rejected.</param>
/// <param name="Priority">0.0-1.0.</param>
/// <param name="Reason">Short in-character justification, used for logging/debugging.</param>
public sealed record LlmDecision(string Intent, float Priority, string Reason);
