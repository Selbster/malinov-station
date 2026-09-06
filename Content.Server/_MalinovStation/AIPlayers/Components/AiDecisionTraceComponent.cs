namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// AI Players 0.6.3 (spec section 3): one cognitive decision, recorded end to end.
///
/// <see cref="Systems.AiTraceSystem"/> already logged every stage of a decision - but as separate lines, each
/// stamped only with the entity, and interleaved with every other AI player's lines. Following a single
/// decision from "this passenger is bored" to "it physically moved" was therefore not actually possible, which
/// is precisely why "where does the behaviour stop?" kept being answered by reasoning rather than by evidence.
/// This carries one decision's stages on the entity as they happen, so the whole chain can be emitted as a
/// single readable block and read back later from <c>aiplayer_debug</c>.
///
/// Deliberately a plain record of what happened, not a state machine: nothing here influences behaviour, and
/// a stage that never arrives simply stays null - which is the interesting part, because the first null is the
/// broken link (spec section 24).
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
}

/// <summary>
/// AI Players 0.6.3: the stages of a single decision, in the order spec section 3 lists them. Every field is
/// nullable and filled in as its stage is reached; whichever is still null when the decision ends is where the
/// runtime chain actually stopped.
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

    // Stage 4 - what happened in the world as a result.
    public bool MovementStarted;
    public string? Discovery;
    public bool LocationKnowledgeUpdated;

    // Stage 5 - how it ended, and whether cognition was told.
    public string? Feedback;
    public bool Reevaluation;

    /// <summary>Set once the decision has reached a terminal state, so the block is only emitted once.</summary>
    public bool Finished;
}
