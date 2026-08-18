namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// One candidate motivation and its computed strength - the same shape <see cref="GoalComponent.CurrentGoal"/>
/// itself takes, but <see cref="DesireComponent"/> keeps every candidate <see cref="Systems.GoalSystem"/>
/// considered on its last reconsideration, not just the winner.
/// </summary>
public readonly record struct Desire(string Name, float Priority, string Reason);

/// <summary>
/// A cognitive-mode AI player's full set of currently-felt desires, materialized from the same
/// candidate-scoring <see cref="Systems.GoalSystem.Reconsider"/> already does for every AI player - only a
/// cognitive AI keeps the intermediate list around instead of throwing it away once the winner is picked. Only
/// ever added to AI players spawned in cognitive mode - a legacy AI player never has this component, and
/// <see cref="Systems.DesireSystem.Record"/> is a safe no-op for them.
/// </summary>
[RegisterComponent]
public sealed partial class DesireComponent : Component
{
    [ViewVariables]
    public List<Desire> Current = new();

    [ViewVariables]
    public TimeSpan LastComputedAt;
}
