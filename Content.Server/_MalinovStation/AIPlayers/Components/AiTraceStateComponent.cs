using Content.Server.NPC.HTN;

namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Last-observed HTN plan/task state for an AI player, used purely by
/// <see cref="Systems.AiTraceSystem"/> to detect plan/action-level transitions (a new plan, a completed
/// step, a step that failed mid-plan) by diffing against <see cref="HTNComponent"/> each tick, without
/// touching vanilla HTN dispatch code (<c>Content.Server/NPC/HTN/HTNSystem.cs</c> raises no events of its
/// own to hook into, and it's the hottest shared code path for every NPC in the game - safer to observe
/// state externally than to instrument it).
/// </summary>
[RegisterComponent]
public sealed partial class AiTraceStateComponent : Component
{
    /// <summary>Reference to the last HTN plan seen, to detect when a new one is generated.</summary>
    [ViewVariables]
    public HTNPlan? LastPlan;

    [ViewVariables]
    public int LastPlanIndex = -1;

    /// <summary>Type name of the operator that was current last tick, for ActionStarted/Completed/Failed.</summary>
    [ViewVariables]
    public string? LastOperatorName;

    /// <summary>The goal the last-seen plan was generated for, to tell PlanStarted from Replanned.</summary>
    [ViewVariables]
    public string? LastPlanGoal;

    /// <summary>
    /// The most recently preempted goal (its plan disappeared while a different goal became current), so a
    /// later plan for that same goal can be traced as PlanResumed instead of a fresh PlanStarted.
    /// </summary>
    [ViewVariables]
    public string? LastInterruptedGoal;
}
