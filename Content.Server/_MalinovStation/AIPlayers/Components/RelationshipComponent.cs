namespace Content.Server._MalinovStation.AIPlayers.Components;

/// <summary>
/// How an AI player feels about one other entity. Trust/Respect/Friendship/Loyalty range -1 (actively
/// distrusted/disrespected/disliked/disloyal-feeling) to 1; Fear/Anger range 0 (none) to 1 (extreme), since
/// "negative fear" isn't a meaningful concept.
/// </summary>
public sealed class RelationshipData
{
    public float Trust;
    public float Respect;
    public float Fear;
    public float Friendship;
    public float Anger;
    public float Loyalty;
}

/// <summary>
/// Per-other-entity relationships for an AI player. Entries are only created through
/// <see cref="Systems.RelationshipSystem"/>, normally in response to something the AI player actually
/// perceived or experienced (see <see cref="Systems.PerceptionSystem"/>), never bulk-populated.
/// </summary>
[RegisterComponent]
public sealed partial class RelationshipComponent : Component
{
    [ViewVariables]
    public Dictionary<EntityUid, RelationshipData> Relationships = new();
}
