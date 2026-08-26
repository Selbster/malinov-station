namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// AI Players 0.6.2 (spec section 15): what actually became of an <see cref="IAiAction.Do"/> attempt.
/// Before this milestone every action returned <c>void</c> and the only failure channel was
/// <see cref="IAiAction.CanDo"/>'s <c>failReason</c> string - so an action that passed CanDo but then found
/// nothing to do at execution time (the normal case for exploration: "I decided to go somewhere new, but
/// there is nowhere sensible to go") had no way to say so, and silently looked like a success.
///
/// Deliberately a small closed enum rather than free text: <see cref="Systems.AiTraceSystem"/> turns it into
/// the Russian outcome memory the LLM reads next cycle, and it needs to distinguish "there was no target" from
/// "the route is blocked" to let cognition choose a genuinely different next move (spec section 15's own
/// list).
/// </summary>
public enum AiActionOutcome : byte
{
    /// <summary>Handed off to extended multi-tick execution (HTN). Only this outcome arms
    /// <see cref="Components.AiBusyStateComponent"/>.</summary>
    Started,

    /// <summary>Finished within this call.</summary>
    Completed,

    /// <summary>Nothing suitable to act on - e.g. exploration found no place worth going.</summary>
    NoTarget,

    /// <summary>A target existed but no route to it does.</summary>
    NoPath,

    /// <summary>The route requires a door/area this actor is not authorised for.</summary>
    AccessDenied,

    /// <summary>Physically prevented right now (obstructed, restrained, held).</summary>
    Blocked,

    /// <summary>Abandoned before completing, by this actor or something else.</summary>
    Cancelled,

    /// <summary>Ran out of its allotted time.</summary>
    Timeout,

    /// <summary>Anything else that went wrong.</summary>
    Failed,
}

/// <summary>
/// AI Players 0.6.2: the outcome of one <see cref="IAiAction.Do"/> call plus a human-readable Russian
/// explanation. <see cref="Reason"/> is what reaches the LLM (via
/// <see cref="Systems.AiTraceSystem.ActionFailed"/>'s outcome memory), so it is written in the same
/// second-person Russian voice as every other memory in this subsystem, never as a technical error string.
/// </summary>
public sealed record AiActionResult(AiActionOutcome Outcome, string Reason)
{
    /// <summary>Whether the action did what it was asked - either finishing outright, or genuinely starting
    /// extended execution. Anything else is real feedback the cognitive layer should react to.</summary>
    public bool IsSuccess => Outcome is AiActionOutcome.Started or AiActionOutcome.Completed;

    public static AiActionResult Started(string reason = "") => new(AiActionOutcome.Started, reason);
    public static AiActionResult Completed(string reason = "") => new(AiActionOutcome.Completed, reason);
    public static AiActionResult NoTarget(string reason) => new(AiActionOutcome.NoTarget, reason);
    public static AiActionResult NoPath(string reason) => new(AiActionOutcome.NoPath, reason);
    public static AiActionResult AccessDenied(string reason) => new(AiActionOutcome.AccessDenied, reason);
    public static AiActionResult Blocked(string reason) => new(AiActionOutcome.Blocked, reason);
    public static AiActionResult Cancelled(string reason) => new(AiActionOutcome.Cancelled, reason);
    public static AiActionResult Timeout(string reason) => new(AiActionOutcome.Timeout, reason);
    public static AiActionResult Failed(string reason) => new(AiActionOutcome.Failed, reason);
}
