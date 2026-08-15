using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Preconditions;

/// <summary>
/// IsMet when the owner has a <see cref="NeedsComponent"/> and its Fatigue is above <see cref="Above"/>.
/// Mirrors the vanilla SatiationPrecondition pattern, just reading our own Needs data instead.
/// </summary>
public sealed partial class FatiguePrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;

    [DataField]
    public float Above = 0.75f;

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager) ||
            !_entManager.TryGetComponent<NeedsComponent>(owner, out var needs))
        {
            return false;
        }

        return needs.Fatigue > Above;
    }
}
