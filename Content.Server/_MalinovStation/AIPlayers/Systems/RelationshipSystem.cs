using Content.Server._MalinovStation.AIPlayers.Components;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Reads/writes <see cref="RelationshipComponent"/> entries. This is a plain clamped accumulator: callers
/// (perception, conversation, events) decide how much a given happening should move the numbers, including
/// scaling by personality if relevant - this system just stores and clamps the result.
/// </summary>
public sealed class RelationshipSystem : EntitySystem
{
    /// <summary>
    /// Gets the relationship entry for <paramref name="other"/>, creating a neutral one if it doesn't exist
    /// yet. Returns null if the entity has no <see cref="RelationshipComponent"/> at all.
    /// </summary>
    public RelationshipData? EnsureRelationship(EntityUid uid, EntityUid other, RelationshipComponent? relationships = null)
    {
        if (!Resolve(uid, ref relationships, false))
            return null;

        if (!relationships.Relationships.TryGetValue(other, out var data))
        {
            data = new RelationshipData();
            relationships.Relationships[other] = data;
        }

        return data;
    }

    /// <summary>
    /// Read-only lookup. Returns a neutral (all-zero) relationship if none has been recorded yet, without
    /// creating an entry.
    /// </summary>
    public RelationshipData GetRelationship(EntityUid uid, EntityUid other, RelationshipComponent? relationships = null)
    {
        if (Resolve(uid, ref relationships, false) && relationships.Relationships.TryGetValue(other, out var data))
            return data;

        return new RelationshipData();
    }

    public void ModifyRelationship(
        EntityUid uid,
        EntityUid other,
        float trustDelta = 0f,
        float respectDelta = 0f,
        float fearDelta = 0f,
        float friendshipDelta = 0f,
        float angerDelta = 0f,
        float loyaltyDelta = 0f,
        RelationshipComponent? relationships = null)
    {
        var data = EnsureRelationship(uid, other, relationships);
        if (data is null)
            return;

        data.Trust = Math.Clamp(data.Trust + trustDelta, -1f, 1f);
        data.Respect = Math.Clamp(data.Respect + respectDelta, -1f, 1f);
        data.Fear = Math.Clamp(data.Fear + fearDelta, 0f, 1f);
        data.Friendship = Math.Clamp(data.Friendship + friendshipDelta, -1f, 1f);
        data.Anger = Math.Clamp(data.Anger + angerDelta, 0f, 1f);
        data.Loyalty = Math.Clamp(data.Loyalty + loyaltyDelta, -1f, 1f);
    }
}
