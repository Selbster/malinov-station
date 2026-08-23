namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// AI Players 0.4 Milestone 1: the coarse action groups the Cognitive LLM role picks between (see
/// <see cref="LLM.PromptBuilder.BuildCognitiveSystemPrompt"/>) before any concrete action is chosen. Kept to a
/// handful deliberately (spec: "do not create dozens of categories") - one more than the spec's own minimal
/// Movement/Work/General example, because <see cref="TalkToAction"/>/<see cref="TalkAction"/> don't map
/// naturally onto any of those three and "Social" is one of the spec's own illustrative category names.
/// </summary>
public static class AiActionCategories
{
    /// <summary><see cref="GoToKnownLocationAction"/>, <see cref="MoveToAction"/>.</summary>
    public const string Movement = "Movement";

    /// <summary><see cref="PursueGoalAction"/>, <see cref="UseInteractableAction"/>,
    /// <see cref="PickUpItemAction"/>, <see cref="SearchAreaAction"/>.</summary>
    public const string Work = "Work";

    /// <summary><see cref="TalkToAction"/>, <see cref="TalkAction"/>.</summary>
    public const string Social = "Social";

    /// <summary><see cref="ContinueActivityAction"/> - deliberately confirming no change.</summary>
    public const string General = "General";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Movement,
        Work,
        Social,
        General,
    };
}
