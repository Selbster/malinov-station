namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Tracks when an entity last used the "Talk" action, so <see cref="TalkAction"/>'s cooldown lives on the
/// entity itself rather than in a dictionary on the (long-lived, shared-across-entities) action instance -
/// a dictionary entry could otherwise outlive the entity it was keyed by and spuriously block a different,
/// later entity that happens to reuse the same EntityUid.
/// </summary>
[RegisterComponent]
public sealed partial class TalkCooldownComponent : Component
{
    [ViewVariables]
    public TimeSpan LastTalkAt;
}
