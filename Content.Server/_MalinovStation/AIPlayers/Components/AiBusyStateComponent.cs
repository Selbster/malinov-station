using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Current extended action and the identity of its journey. Added to cognitive and legacy AI players.
/// <see cref="Systems.AiBusyStateSystem"/> owns transitions and consumes terminal observations from HTN.
/// The target snapshot survives HTN cleanup so completion can still be attributed to the original action.
/// </summary>
[RegisterComponent]
public sealed partial class AiBusyStateComponent : Component
{
    /// <summary>The <see cref="Actions.IAiAction.Name"/> currently committed to, or null if not busy.</summary>
    [ViewVariables]
    public string? CurrentAction;

    [ViewVariables]
    public TimeSpan StartedAt;

    /// <summary>Reason for the commitment, surfaced in <see cref="LLM.CognitiveState"/>.</summary>
    [ViewVariables]
    public string Reason = string.Empty;

    /// <summary>
    /// Whether danger may interrupt this commitment.
    /// </summary>
    [ViewVariables]
    public bool CancellationAllowed = true;

    /// <summary>
    /// Maximum duration when execution never reports a terminal result.
    /// </summary>
    [DataField]
    public float MaxBusyDurationSeconds = 300f;

    /// <summary>Monotonic identity of the currently executing extended action.</summary>
    [ViewVariables]
    public long ExecutionId;

    /// <summary>Destination snapshot retained until the owning execution finishes.</summary>
    [ViewVariables]
    public EntityCoordinates? JourneyTarget;

    /// <summary>Remembered place owned by this execution, if the target has a name.</summary>
    [ViewVariables]
    public string? JourneyPlace;

    /// <summary>Position from which self-propelled progress is measured.</summary>
    [ViewVariables]
    public EntityCoordinates? JourneyStart;

    [ViewVariables]
    public bool SteeringStarted;

    [ViewVariables]
    public bool ProgressConfirmed;

    /// <summary>Last terminal result, retained for diagnostics and idempotency tests.</summary>
    [ViewVariables]
    public Actions.AiActionResult? LastResult;

    [ViewVariables]
    public long LastFinishedExecutionId;

    /// <summary>Terminal observation from HTN, consumed outside its callbacks.</summary>
    [ViewVariables]
    public Actions.AiActionResult? PendingResult;

    /// <summary>A discontinuous transform change occurred during this execution.</summary>
    [ViewVariables]
    public bool ExternallyRelocated;

    /// <summary>Decision owning an extended action, including non-travel commitments.</summary>
    [ViewVariables]
    public int DecisionId;
}
