using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Parameters for the "MoveTo" action: where to walk to.
/// </summary>
public sealed record MoveToActionParams(EntityCoordinates Destination) : IAiActionParams;
