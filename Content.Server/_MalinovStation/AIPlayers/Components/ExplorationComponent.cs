namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// AI Players 0.6.2: per-AI exploration bookkeeping for <see cref="Actions.ExploreStationAction"/>. Exists for
/// exactly one reason - spec section 16's "no exploration loop": an AI that decides to explore and finds
/// nowhere to go must not simply decide the same thing again on its very next reflection. The existing failure
/// machinery already reacts (<see cref="Systems.AiTraceSystem.ActionFailed"/> writes an outcome memory, forces
/// a fast re-reflection and promotes a repeated failure to a belief), but none of that makes the action stop
/// being *offered*; this does, for a while.
///
/// Added only to cognitive AI players, matching the same convention every other 0.6-era overlay component
/// follows (see <see cref="LocationKnowledgeComponent"/>).
/// </summary>
[RegisterComponent]
public sealed partial class ExplorationComponent : Component
{
    /// <summary>
    /// Before this time, <c>ExploreStation</c> reports itself ineligible. Set when an attempt finds no target,
    /// cleared implicitly by simply elapsing - a later attempt is always allowed once the world has had a
    /// chance to change (someone opened a door, the AI got moved, a new place got discovered).
    /// </summary>
    [ViewVariables]
    public TimeSpan NoTargetCooldownUntil;

    /// <summary>How long a fruitless exploration attempt suppresses further ones. Long enough to break the
    /// reflect-fail-reflect cycle, short enough that the AI is not stuck indoors for the rest of the round if
    /// circumstances change.</summary>
    [DataField]
    public float NoTargetCooldownSeconds = 120f;

    /// <summary>The place name this AI is currently exploring toward, if it had one - purely informational,
    /// surfaced for tracing and tests. Null for a nameless frontier trip.</summary>
    [ViewVariables]
    public string? CurrentTargetName;
}
