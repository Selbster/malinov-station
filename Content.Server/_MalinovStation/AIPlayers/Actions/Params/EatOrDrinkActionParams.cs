namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Parameters for the "EatOrDrink" action - none; the target is always whatever's in the AI's active hand
/// right now, never LLM-chosen, but still an explicit params type so it flows through
/// <see cref="Systems.AiActionRegistrySystem"/> like every other action.
/// </summary>
public sealed record EatOrDrinkActionParams : IAiActionParams;
