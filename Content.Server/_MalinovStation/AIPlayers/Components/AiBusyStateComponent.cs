namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// AI Players 0.4 Milestone 5: a lightweight "am I currently committed to something extended" flag. Added
/// unconditionally at spawn (like <see cref="DoorApproachComponent"/>) so both legacy and cognitive AI players
/// have it, but only ever populated by <see cref="Systems.AiActionRegistrySystem.TryDoAction"/> for an
/// <see cref="Actions.IAiAction"/> flagged <see cref="Actions.IAiAction.IsExtended"/> - <c>PursueGoal</c> and
/// <c>GoToKnownLocation</c> are the only two actions that hand the actor off to HTN execution over many ticks
/// today; everything else resolves in one tick and never touches this component.
///
/// Deliberately does NOT duplicate <see cref="GoalComponent.IsLlmOverride"/> or the HTN blackboard's
/// <c>ForcedDestination</c> key as a second source of truth - <see cref="Systems.AiBusyStateSystem"/> clears
/// this by polling those same existing signals (see its own doc comment), so this component is a readable
/// summary for cognition/eligibility to check ("am I busy, with what, why, can it be interrupted"), not an
/// independently-tracked state machine that could drift out of sync with what's actually happening.
/// </summary>
[RegisterComponent]
public sealed partial class AiBusyStateComponent : Component
{
    /// <summary>The <see cref="Actions.IAiAction.Name"/> currently committed to, or null if not busy.</summary>
    [ViewVariables]
    public string? CurrentAction;

    [ViewVariables]
    public TimeSpan StartedAt;

    /// <summary>Short human/LLM-readable reason, surfaced in <see cref="LLM.CognitiveState"/> so the AI can
    /// reason "I am currently doing X because Y" (spec's repair-generator example).</summary>
    [ViewVariables]
    public string Reason = string.Empty;

    /// <summary>
    /// Whether <see cref="Systems.AiBusyStateSystem"/> is allowed to interrupt this commitment on a
    /// high-severity danger event (Milestone 6). True for both actions that populate this component today -
    /// there's no current action whose commitment should survive e.g. being attacked mid-task.
    /// </summary>
    [ViewVariables]
    public bool CancellationAllowed = true;

    /// <summary>
    /// Hard backstop so busy-state can never wedge open forever even if neither action's own natural-
    /// completion signal (see <see cref="Systems.AiBusyStateSystem"/>) ever fires - "the framework must
    /// support... continue current action or interrupt current action", never neither.
    /// </summary>
    [DataField]
    public float MaxBusyDurationSeconds = 300f;

    /// <summary>
    /// Milestone 6: whether <see cref="Systems.AiBusyStateSystem"/> already forced a fast cognitive
    /// re-reflection for the danger currently active during this commitment - reset whenever a fresh
    /// commitment starts (<see cref="Systems.AiActionRegistrySystem.TryDoAction"/>), so one ongoing threat
    /// only triggers one forced reflection rather than re-triggering it every scan tick it stays active.
    /// </summary>
    [ViewVariables]
    public bool InterruptionNoticed;
}
