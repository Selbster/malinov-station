namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// A cognitive-mode AI player's richer view of its own current intent - <see cref="Name"/> is always kept
/// equal to <see cref="GoalComponent.CurrentGoal"/> (HTN still only ever reads that field, via
/// <c>CurrentGoalPrecondition</c> - this component never becomes a second thing HTN branches gate on), but
/// carries the reasoning/confidence <see cref="GoalComponent"/> has no room for. Written by the same two
/// places that write <see cref="GoalComponent.CurrentGoal"/> (<see cref="Systems.GoalSystem.Reconsider"/> for
/// the formula-driven fallback, <see cref="Systems.LlmGatewaySystem.TryApplyCognitiveDecision"/> for an actual
/// cognitive decision). Only ever added to AI players spawned in cognitive mode.
/// </summary>
[RegisterComponent]
public sealed partial class IntentComponent : Component
{
    [ViewVariables]
    public string Name = AIGoals.Idle;

    /// <summary>1.0 for a formula-derived pick (it's not a "confidence" in the LLM sense, just the default);
    /// the LLM's own reported confidence for a cognitive pick.</summary>
    [ViewVariables]
    public float Confidence = 1f;

    /// <summary>Which <see cref="Desire.Name"/> this intent commits to acting on.</summary>
    [ViewVariables]
    public string DesireServed = string.Empty;

    [ViewVariables]
    public TimeSpan ChosenAt;
}
