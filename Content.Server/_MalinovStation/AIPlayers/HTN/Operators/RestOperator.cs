using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>
/// Recovers the owner's Fatigue (and a little Stress) while standing still, until it drops below
/// <see cref="StopBelow"/>.
/// </summary>
public sealed partial class RestOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private NeedsSystem _needs = default!;

    [DataField]
    public float RecoveryPerSecond = 0.05f;

    [DataField]
    public float StopBelow = 0.3f;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _needs = sysManager.GetEntitySystem<NeedsSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<NeedsComponent>(owner, out var needs))
            return HTNOperatorStatus.Finished;

        _needs.ModifyFatigue(owner, -RecoveryPerSecond * frameTime, needs);
        needs.Stress = Math.Clamp(needs.Stress - RecoveryPerSecond * 0.5f * frameTime, 0f, 1f);

        return needs.Fatigue <= StopBelow ? HTNOperatorStatus.Finished : HTNOperatorStatus.Continuing;
    }
}
