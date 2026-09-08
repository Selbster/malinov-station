using Content.Server._MalinovStation.AIPlayers.Actions;

namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Bounded diagnostic history connecting decisions to their execution and terminal result.
/// New cognitive decisions may coexist with an older journey; these records never control execution.
/// </summary>
[RegisterComponent]
public sealed partial class AiDecisionTraceComponent : Component
{
    /// <summary>Monotonic per-entity counter. Small and human-readable on purpose - it exists to correlate the
    /// stages of one decision in a log shared by several AI players, not to be globally unique.</summary>
    [ViewVariables]
    public int DecisionId;

    /// <summary>The decision currently being recorded, if one is in flight.</summary>
    [ViewVariables]
    public AiDecisionTrace? Current;

    /// <summary>The last decision that finished, kept for inspection after the fact.</summary>
    [ViewVariables]
    public AiDecisionTrace? Last;

    /// <summary>Recent records, including <see cref="Current"/>, retained for events carrying an older ID.</summary>
    [ViewVariables]
    public List<AiDecisionTrace> History = new();
}

/// <summary>
/// Observed stages of one decision. A missing stage is distinct from a failed or cancelled execution.
/// </summary>
public sealed class AiDecisionTrace
{
    public int Id;
    public TimeSpan StartedAt;

    // Stage 1 - why the AI is deciding anything at all.
    public float Boredom;
    public float Curiosity;
    public string? TopDesire;

    // Stage 2 - what the cognitive model made of that.
    public string? Intent;
    public string? Category;
    public string? EligibleActions;

    // Stage 3 - what was actually chosen and dispatched.
    public string? SelectedAction;
    public string? ExplorationTarget;
    public string? NavigationTarget;

    /// <summary>The execution that owns these observations, independent of later cognitive decisions.</summary>
    public long? ExecutionId;

    /// <summary>Diagnostic state only; the busy-state system owns the actual journey lifecycle.</summary>
    public string ExecutionState = "Deciding";

    // Stage 4 - what happened in the world as a result.
    public bool SteeringStarted;

    /// <summary>True only after observed physical progress, never merely from steering registration.</summary>
    public bool MovementStarted;
    public string? Discovery;
    public bool LocationKnowledgeUpdated;

    // Stage 5 - how it ended, and whether cognition was told.
    public string? Feedback;
    public bool Reevaluation;

    public AiActionResult? Result;
    public bool CognitiveFeedbackDelivered;

    /// <summary>Set once the decision has reached a terminal state, so the block is only emitted once.</summary>
    public bool Finished;
}
