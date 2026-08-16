using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Preconditions;

/// <summary>
/// IsMet when the owner has an active threat: was recently attacked, or is near an active fire (both
/// populated by the Danger system). Gates FleeCompound.
/// </summary>
public sealed partial class DangerPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager) ||
            !_entManager.TryGetComponent<DangerComponent>(owner, out var danger))
        {
            return false;
        }

        return danger.ThreatSource is not null || danger.FireHazardLocation is not null;
    }
}
