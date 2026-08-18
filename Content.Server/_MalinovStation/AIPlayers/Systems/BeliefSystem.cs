using System.Linq;
using Content.Server._MalinovStation.AIPlayers.Components;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Creates and retrieves <see cref="AiBelief"/> entries on <see cref="BeliefComponent"/>. Same capped-eviction,
/// narrow-retrieval shape as <see cref="MemorySystem"/> - a belief is data that could be wrong, so it's kept
/// separate from (and never promoted into) <see cref="MemoryComponent"/>, which every existing reader treats
/// as ground truth.
/// </summary>
public sealed partial class BeliefSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>No-ops (returns null) if the entity has no <see cref="BeliefComponent"/> (i.e. every legacy AI player).</summary>
    public AiBelief? AddBelief(
        EntityUid uid,
        string subject,
        string content,
        float confidence,
        string source,
        IReadOnlyList<EntityUid>? participants = null,
        BeliefComponent? belief = null)
    {
        if (!Resolve(uid, ref belief, false))
            return null;

        var entry = new AiBelief
        {
            Timestamp = _timing.CurTime,
            Subject = subject,
            Content = content,
            Confidence = Math.Clamp(confidence, 0f, 1f),
            Source = source,
            Participants = participants?.ToList() ?? new List<EntityUid>(),
        };

        belief.Beliefs.Add(entry);

        if (belief.Beliefs.Count > belief.MaxBeliefs)
        {
            var weakest = belief.Beliefs
                .OrderBy(b => b.Confidence)
                .ThenBy(b => b.Timestamp)
                .First();
            belief.Beliefs.Remove(weakest);
        }

        return entry;
    }

    /// <summary>Most recent beliefs that involve the given other entity, newest first.</summary>
    public IReadOnlyList<AiBelief> GetBeliefsAbout(EntityUid uid, EntityUid other, int max = 10, BeliefComponent? belief = null)
    {
        if (!Resolve(uid, ref belief, false))
            return Array.Empty<AiBelief>();

        return belief.Beliefs
            .Where(b => b.Participants.Contains(other))
            .OrderByDescending(b => b.Timestamp)
            .Take(max)
            .ToList();
    }
}
