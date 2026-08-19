namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Parameters for the "PursueGoal" action: which existing reflex goal (see
/// <see cref="Systems.GoalSystem.IsKnownGoalName"/>) to actively commit to right now, and why.
/// </summary>
public sealed record PursueGoalActionParams(string GoalName, float Priority, string Reason) : IAiActionParams;
