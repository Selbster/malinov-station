using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Preconditions;

/// <summary>
/// IsMet when the owner currently sees another character incapacitated (populated by the Danger system's
/// perception scan). Gates HelpInjuredCompound.
/// </summary>
public sealed partial class HelpInjuredPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager) ||
            !_entManager.TryGetComponent<DangerComponent>(owner, out var danger) ||
            danger.NearbyInjured is not { } injured)
        {
            return false;
        }

        return !_entManager.Deleted(injured);
    }
}
