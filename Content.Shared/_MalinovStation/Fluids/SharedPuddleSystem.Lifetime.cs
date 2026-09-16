using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;

#pragma warning disable IDE0130 // Namespace does not match folder structure.
namespace Content.Shared.Fluids;

public abstract partial class SharedPuddleSystem
{
    /// <summary>
    /// Rechecks a pending deletion after PVS changes and prediction rollback may have restored the puddle.
    /// </summary>
    private bool CanDeleteQueuedPuddle(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || (MetaData(uid).Flags & MetaDataFlags.Detached) != 0)
            return false;

        return !_puddleQuery.TryComp(uid, out var puddle) ||
               !_solutionContainerSystem.ResolveSolution(uid, puddle.SolutionName, ref puddle.Solution, out var solution) ||
               solution.Volume <= FixedPoint2.Zero;
    }
}
