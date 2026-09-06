using Robust.Shared.Map;
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

    /// <summary>Where that target actually is, so arrival can be told apart from giving up. Without this a
    /// journey that failed looks exactly like one that succeeded: in both cases the destination key is simply
    /// gone from the blackboard.</summary>
    [ViewVariables]
    public EntityCoordinates? CurrentTargetCoordinates;

    /// <summary>
    /// Places this AI set out for and never reached, and when that judgement lapses.
    ///
    /// Live play showed the gap this closes: an AI would pick somewhere behind a door it cannot pass, fail to
    /// get there, and - having learned nothing about the *destination*, only about the door - pick the very
    /// same place again on its next reflection, indefinitely. Remembering the door was never enough, because
    /// choosing where to go happens before any route exists.
    /// </summary>
    [ViewVariables]
    public Dictionary<string, TimeSpan> UnreachablePlaces = new();

    /// <summary>How long a place stays written off after a failed journey. Long enough to stop the loop,
    /// short enough that a door being unbolted or a route reopening is eventually noticed.</summary>
    [DataField]
    public float UnreachableMemorySeconds = 300f;
}
