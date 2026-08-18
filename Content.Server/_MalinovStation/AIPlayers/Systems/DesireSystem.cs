using System.Linq;
using Content.Server._MalinovStation.AIPlayers.Components;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Materializes <see cref="GoalSystem.Reconsider"/>'s candidate list onto <see cref="DesireComponent"/> for a
/// cognitive-mode AI player. Deliberately has no <see cref="Update"/> of its own - it's driven by
/// <see cref="GoalSystem"/>'s own reconsider cadence (see <see cref="GoalSystem.ComputeCandidates"/>), so
/// Desire and the eventual Intent are always computed from the exact same reconsideration pass and can never
/// drift apart in timing.
/// </summary>
public sealed partial class DesireSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;

    /// <summary>No-ops for an entity with no <see cref="DesireComponent"/> (i.e. every legacy AI player).</summary>
    public void Record(EntityUid uid, IReadOnlyList<Desire> candidates, DesireComponent? desire = null)
    {
        if (!Resolve(uid, ref desire, false))
            return;

        desire.Current = candidates.OrderByDescending(c => c.Priority).ToList();
        desire.LastComputedAt = _timing.CurTime;
    }
}
