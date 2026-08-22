namespace Content.Server._MalinovStation.AIPlayers.Actions;

public sealed record TalkToActionParams(string Target, string Reason) : IAiActionParams;
