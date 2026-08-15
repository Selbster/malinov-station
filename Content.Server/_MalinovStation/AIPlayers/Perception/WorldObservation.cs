using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.Perception;

/// <summary>
/// A snapshot of what an AI player could actually perceive at a point in time: only entities within vision
/// range that pass an unobstructed line-of-sight check. Never built from omniscient world state.
/// </summary>
public sealed record WorldObservation(EntityCoordinates Location, IReadOnlyList<EntityUid> VisibleCharacters, TimeSpan Timestamp);
