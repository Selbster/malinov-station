namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Parameters for the "ExploreStation" action - none by design. Where to actually go is decided by
/// <see cref="Systems.ExplorationControllerSystem"/> from what this AI genuinely knows, never named by the
/// LLM: spec section 11 keeps coordinates, tiles and route choices out of the model's hands entirely, and
/// section 9 forbids exposing them even indirectly.
/// </summary>
public sealed record ExploreStationActionParams : IAiActionParams;
