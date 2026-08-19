namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Parameters for the "ContinueActivity" action - none; a true no-op, but still an explicit params type so it
/// flows through <see cref="Systems.AiActionRegistrySystem"/> like every other action.
/// </summary>
public sealed record ContinueActivityActionParams : IAiActionParams;
