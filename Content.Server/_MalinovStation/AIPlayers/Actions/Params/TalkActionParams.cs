namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Parameters for the "Talk" action: exactly the line to say.
/// </summary>
public sealed record TalkActionParams(string Text) : IAiActionParams;
