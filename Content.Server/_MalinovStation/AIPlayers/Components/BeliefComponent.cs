namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// Something a cognitive-mode AI player believes but hasn't directly witnessed - distinct from
/// <see cref="AiMemory"/>, which is always treated as ground truth by every existing reader. A belief can be
/// wrong, incomplete, or based on a secondhand rumor; <see cref="Confidence"/> is how sure the AI is. See
/// <see cref="Systems.SocialSystem.ShareRumor"/> for the one place today that creates these (a rumor lands
/// here for a cognitive listener instead of being copied verbatim into <see cref="MemoryComponent"/>).
/// </summary>
public sealed class AiBelief
{
    public Guid Id = Guid.NewGuid();
    public TimeSpan Timestamp;

    /// <summary>Who or what this belief is about, e.g. a character's name.</summary>
    public string Subject = string.Empty;

    public string Content = string.Empty;

    /// <summary>0 (barely credited) to 1 (as good as certain).</summary>
    public float Confidence;

    /// <summary>What produced this belief, e.g. "rumor", "inference". Plain string, same convention as <see cref="AiMemory.Source"/>.</summary>
    public string Source = string.Empty;

    public List<EntityUid> Participants = new();
}

/// <summary>
/// A cognitive-mode AI player's beliefs. Only ever added to AI players spawned in cognitive mode - a legacy
/// AI player never has this component, and every write to it is a safe no-op for them.
/// </summary>
[RegisterComponent]
public sealed partial class BeliefComponent : Component
{
    [ViewVariables]
    public List<AiBelief> Beliefs = new();

    /// <summary>Once exceeded, the least confident (then oldest) belief is dropped to make room.</summary>
    [DataField]
    public int MaxBeliefs = 50;
}
