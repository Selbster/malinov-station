namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Parameters for the "GoToKnownLocation" action: a free-text name hint resolved against the AI's own
/// memory (see <see cref="Systems.MemorySystem.FindKnownLocation"/>) - never literal coordinates.
/// </summary>
public sealed record GoToKnownLocationActionParams(string LocationHint) : IAiActionParams;
