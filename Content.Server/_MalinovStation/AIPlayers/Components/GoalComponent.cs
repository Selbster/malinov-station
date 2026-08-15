namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Tracks the AI player's currently selected goal, as computed by
/// <see cref="Content.Server._MalinovStation.AIPlayers.Systems.GoalSystem"/> from needs and personality.
/// This is an observability/intent layer: most goals here (SatisfyHunger/SatisfyThirst) are already handled
/// by existing HTN branches that gate on their own vanilla components, so this does not (yet) drive them
/// directly. Rest is the one goal in Milestone 2 with a dedicated HTN branch reading <see cref="NeedsComponent"/>.
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
