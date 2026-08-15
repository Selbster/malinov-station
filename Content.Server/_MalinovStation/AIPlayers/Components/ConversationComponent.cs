namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Tracks an AI player's participation in the bounded two-line greeting exchange run by
/// <see cref="Systems.SocialSystem"/> - not a general chat/turn system, just enough state to run one
/// conversation at a time and cool down afterward, per spec section 20's "a conversation must have a
/// purpose, not be an endless chatbot loop."
/// </summary>
[RegisterComponent]
public sealed partial class ConversationComponent : Component
{
    [ViewVariables]
    public EntityUid? Partner;

    [ViewVariables]
    public bool IsInitiator;

    [ViewVariables]
    public ConversationState State = ConversationState.None;

    /// <summary>
    /// How long after a conversation ends before this AI player will start (or be picked as a partner for)
    /// another one.
    /// </summary>
    [DataField]
    public float PostConversationCooldownSeconds = 45f;

    [ViewVariables]
    public TimeSpan CooldownUntil;
}

public enum ConversationState : byte
{
    /// <summary>Not talking, not reserved as anyone's partner.</summary>
    None,

    /// <summary>Waiting on this entity's opening line (LLM or fallback) to be generated.</summary>
    AwaitingOpeningLine,

    /// <summary>Waiting on this entity's reply line (LLM or fallback) to be generated.</summary>
    AwaitingReplyLine,
}
