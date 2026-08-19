namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// A cognitive-mode AI player's own self-narrated intent - AI Players 0.3's "Independent Intent"
/// (spec section 3): free-form (<see cref="Name"/> is deliberately NOT validated against
/// <see cref="AIGoals.All"/> or any other closed vocabulary), and deliberately independent of
/// <see cref="GoalComponent.CurrentGoal"/> in both directions. <see cref="GoalComponent"/>/HTN stays a pure
/// reflex layer underneath - <see cref="Systems.GoalSystem.Reconsider"/> never writes this component, so an
/// intent like "find_food" survives untouched regardless of what reflex goal happens to be active. Written
/// exclusively by <see cref="Systems.LlmGatewaySystem.TryApplyCognitiveDecision"/>, unconditionally (even if
/// the accompanying action proposal then fails - the AI still "wanted" it). Only ever added to AI players
/// spawned in cognitive mode.
/// </summary>
[RegisterComponent]
public sealed partial class IntentComponent : Component
{
    [ViewVariables]
    public string Name = AIGoals.Idle;

    /// <summary>How strongly the AI wants this, 0-1 - the LLM's own reported priority for this intent. No
    /// longer borrowed from <see cref="GoalComponent.CurrentPriority"/> now that Intent is independent.</summary>
    [ViewVariables]
    public float Priority;

    /// <summary>The LLM's own reported confidence, 0-1, that this is the right call.</summary>
    [ViewVariables]
    public float Confidence = 1f;

    /// <summary>Which <see cref="Desire.Name"/> this intent commits to acting on.</summary>
    [ViewVariables]
    public string DesireServed = string.Empty;

    [ViewVariables]
    public TimeSpan ChosenAt;
}
