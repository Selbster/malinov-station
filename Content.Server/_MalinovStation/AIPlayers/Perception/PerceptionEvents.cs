namespace Content.Server._MalinovStation.AIPlayers.Perception;

/// <summary>
/// Raised on an AI player the first time it ever notices a given other character (not on routine
/// re-sightings). This is the one genuinely "novel social situation" trigger available before Milestone 7's
/// dynamic events exist, and is what the LLM gateway listens for (spec section 23: event-driven, not
/// every-tick, LLM calls).
/// </summary>
[ByRefEvent]
public readonly record struct AiPlayerMetNewCharacterEvent(EntityUid AiPlayer, EntityUid Other);
