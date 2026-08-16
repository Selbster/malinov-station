namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Tracks the AI player's currently selected goal/intent, as computed by
/// <see cref="Content.Server._MalinovStation.AIPlayers.Systems.GoalSystem"/> from needs, danger and
/// personality (or overridden by an LLM decision - see <see cref="Systems.LlmGatewaySystem"/>). This is the
/// single unambiguous "current intent" the spec asks for (Milestone 1): Flee/HelpInjured/RepairMachine/Rest's
/// HTN branches all require <c>CurrentGoalPrecondition</c> to match <see cref="CurrentGoal"/> before they can
/// run, on top of their own independent real-world fact precondition. SatisfyHunger/SatisfyThirst stay
/// observability-only - the vanilla FoodCompound HTN branch reacts to <see cref="Content.Shared.Nutrition.Components.SatiationComponent"/>
/// directly, and this component's owner mirrors its exact thresholds so the two never disagree. Socialize is
/// also a direct consumer (see <see cref="Systems.SocialSystem"/>).
/// </summary>
[RegisterComponent]
public sealed partial class GoalComponent : Component
{
    [ViewVariables]
    public string CurrentGoal = AIGoals.Idle;

    [ViewVariables]
    public float CurrentPriority;

    [ViewVariables]
    public string Reason = string.Empty;

    /// <summary>
    /// How often (in seconds) goals are reconsidered.
    /// </summary>
    [DataField]
    public float ReconsiderCooldown = 4f;

    [ViewVariables]
    public float ReconsiderAccumulator;

    /// <summary>
    /// True while an LLM decision (see Milestone 4's LLM gateway) is in effect. While set,
    /// <see cref="Systems.GoalSystem"/> leaves CurrentGoal alone instead of recomputing it, until
    /// <see cref="LlmOverrideExpiresAt"/> passes.
    /// </summary>
    [ViewVariables]
    public bool IsLlmOverride;

    [ViewVariables]
    public TimeSpan LlmOverrideExpiresAt;

    /// <summary>
    /// When <see cref="CurrentGoal"/> was last (re)selected. Lets <see cref="Systems.GoalSystem"/> notice a
    /// goal that keeps getting reselected without ever resolving (spec Milestone 1 section 19: an AI should
    /// never silently loop on one action) and warn about it instead of staying quiet forever.
    /// </summary>
    [ViewVariables]
    public TimeSpan CurrentGoalSince;

    /// <summary>
    /// When <see cref="Systems.GoalSystem"/> last logged a "stuck on this goal" warning for the current goal,
    /// so repeats are throttled to once per <see cref="Systems.GoalSystem.StuckWarningThreshold"/> instead of
    /// spamming every reconsider tick.
    /// </summary>
    [ViewVariables]
    public TimeSpan LastStuckWarningAt;

    /// <summary>
    /// The most recent LLM decision applied to this AI player (intent + reason), or null if none ever was.
    /// Kept for debug visibility (spec Milestone 1 section 16) even after <see cref="IsLlmOverride"/> expires
    /// and <see cref="CurrentGoal"/>/<see cref="Reason"/> move on to something else.
    /// </summary>
    [ViewVariables]
    public string? LastLlmDecision;

    [ViewVariables]
    public TimeSpan LastLlmDecisionAt;
}

/// <summary>
/// Milestone 2's small fixed goal vocabulary. This will grow into data-driven <c>GoalPrototype</c>s once
/// professional/situational goals need to be authored per-job instead of hardcoded. Also doubles as the
/// whitelist the LLM gateway validates decisions against (spec section 19: LLM never gets to say anything
/// that isn't a pre-validated option).
/// </summary>
public static class AIGoals
{
    public const string Idle = "Idle";
    public const string Rest = "Rest";
    public const string Socialize = "Socialize";
    public const string SatisfyHunger = "SatisfyHunger";
    public const string SatisfyThirst = "SatisfyThirst";

    /// <summary>Milestone 7: react to being attacked. See DangerComponent/DangerSystem/FleeCompound.</summary>
    public const string Flee = "Flee";

    /// <summary>Milestone 7: react to seeing another character incapacitated nearby.</summary>
    public const string HelpInjured = "HelpInjured";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Idle,
        Rest,
        Socialize,
        SatisfyHunger,
        SatisfyThirst,
        Flee,
        HelpInjured,
    };
}
