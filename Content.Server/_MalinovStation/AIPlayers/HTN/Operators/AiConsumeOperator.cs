using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.DoAfter;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.Physics.Components;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>Consumes the selected food through ingestion and waits for its own do-after.</summary>
public sealed partial class AiConsumeOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private const string DoAfterKey = "AiConsumeDoAfter";

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        var doAfterSystem = _entManager.System<SharedDoAfterSystem>();
        if (blackboard.TryGetValue<DoAfterId>(DoAfterKey, out var pending, _entManager))
        {
            return doAfterSystem.GetStatus(pending) switch
            {
                DoAfterStatus.Running => HTNOperatorStatus.Continuing,
                DoAfterStatus.Cancelled => HTNOperatorStatus.Failed,
                _ => HTNOperatorStatus.Finished,
            };
        }

        if (!blackboard.TryGetValue<EntityUid>("Target", out var target, _entManager) || _entManager.Deleted(target))
            return HTNOperatorStatus.Failed;
        if (!_entManager.System<SharedInteractionSystem>().InRangeUnobstructed(owner, target))
            return HTNOperatorStatus.Failed;
        if (_entManager.TryGetComponent<PhysicsComponent>(owner, out var body) && body.LinearVelocity.LengthSquared() > 0.01f)
            return HTNOperatorStatus.Continuing;

        var doAfters = _entManager.EnsureComponent<DoAfterComponent>(owner);
        var next = doAfters.NextId;
        if (!_entManager.System<IngestionSystem>().TryIngest(owner, target))
            return HTNOperatorStatus.Failed;
        var started = new DoAfterId(owner, next);
        if (doAfterSystem.GetStatus(started) == DoAfterStatus.Invalid)
            return HTNOperatorStatus.Finished;

        blackboard.SetValue(DoAfterKey, started);
        return HTNOperatorStatus.Continuing;
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        if (blackboard.TryGetValue<DoAfterId>(DoAfterKey, out var pending, _entManager))
        {
            var system = _entManager.System<SharedDoAfterSystem>();
            if (system.IsRunning(pending))
                system.Cancel(pending);
        }
        blackboard.Remove<DoAfterId>(DoAfterKey);
    }
}
