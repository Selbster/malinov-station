using System.Threading;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.NPC.Components;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Content.Shared.DoAfter;
using Robust.Shared.Map;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>
/// Hands a forced journey to vanilla steering and reports its actual terminal status.
/// Planning effects retain the execution identity; stale plans cannot start or stop a newer journey.
/// </summary>
public sealed partial class AiJourneyOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private NPCSteeringSystem _steering = default!;
    private AiBusyStateSystem _busy = default!;
    private SharedDoAfterSystem _doAfter = default!;

    public const string JourneyIdKey = "AiJourneyId";
    public const string PlannedIdKey = "AiJourneyPlanId";
    public const string SteeringKey = "AiJourneySteering";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _steering = sysManager.GetEntitySystem<NPCSteeringSystem>();
        _busy = sysManager.GetEntitySystem<AiBusyStateSystem>();
        _doAfter = sysManager.GetEntitySystem<SharedDoAfterSystem>();
    }

    public override Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        if (cancelToken.IsCancellationRequested ||
            !blackboard.TryGetValue<long>(JourneyIdKey, out var id, _entManager) ||
            !blackboard.ContainsKey(MoveToAction.ForcedDestinationKey))
        {
            return Task.FromResult<(bool, Dictionary<string, object>?)>((false, null));
        }

        return Task.FromResult<(bool, Dictionary<string, object>?)>((true,
            new Dictionary<string, object> { { PlannedIdKey, id } }));
    }

    public override void Startup(NPCBlackboard blackboard)
    {
        if (!TryGetJourney(blackboard, out var uid, out var id) ||
            !_entManager.TryGetComponent<AiBusyStateComponent>(uid, out var busy) ||
            busy.JourneyTarget is not { } target || busy.PendingResult is not null || busy.ExternallyRelocated)
            return;

        if (!target.IsValid(_entManager))
        {
            _busy.QueueJourneyResult(uid, id, AiActionResult.NoPath("Цель больше не существует."));
            return;
        }

        var steering = _steering.Register(uid, target);
        steering.Range = blackboard.GetValueOrDefault<float>("MovementRange", _entManager);
        blackboard.SetValue(SteeringKey, steering);
        _busy.JourneySteeringStarted(uid, id);
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        if (!TryGetJourney(blackboard, out var uid, out var id))
            return HTNOperatorStatus.Failed;

        if (_entManager.GetComponent<AiBusyStateComponent>(uid).PendingResult is not null)
            return HTNOperatorStatus.Failed;

        if (!_entManager.TryGetComponent<NPCSteeringComponent>(uid, out var steering) ||
            !blackboard.TryGetValue<NPCSteeringComponent>(SteeringKey, out var owned, _entManager) || steering != owned)
        {
            _busy.QueueJourneyResult(uid, id, AiActionResult.Cancelled("Управление движением было прервано."));
            return HTNOperatorStatus.Failed;
        }

        switch (steering.Status)
        {
            case SteeringStatus.InRange:
                _busy.QueueJourneyResult(uid, id, AiActionResult.Completed("Ты добрался (добралась) до цели."));
                return HTNOperatorStatus.Finished;
            case SteeringStatus.NoPath:
                _busy.QueueJourneyResult(uid, id, AiActionResult.NoPath("Не удалось найти путь к цели."));
                return HTNOperatorStatus.Failed;
            default:
                return HTNOperatorStatus.Continuing;
        }
    }

    public override void TaskShutdown(NPCBlackboard blackboard, HTNOperatorStatus status)
    {
        if (!blackboard.TryGetValue<long>(PlannedIdKey, out var id, _entManager))
            return;
        var uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        if (!_entManager.TryGetComponent<AiBusyStateComponent>(uid, out var busy) || busy.ExecutionId != id)
            return;

        if (blackboard.TryGetValue<NPCSteeringComponent>(SteeringKey, out var owned, _entManager) &&
            _entManager.TryGetComponent<NPCSteeringComponent>(uid, out var steering) && owned == steering)
        {
            _doAfter.Cancel(steering.DoAfterId);
            _steering.Unregister(uid, steering);
        }
        blackboard.Remove<NPCSteeringComponent>(SteeringKey);
        _busy.QueueJourneyResult(uid, id, AiActionResult.Cancelled("Путешествие прервано другим планом."));
    }

    private bool TryGetJourney(NPCBlackboard blackboard, out EntityUid uid, out long id)
    {
        uid = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);
        return blackboard.TryGetValue<long>(PlannedIdKey, out id, _entManager) && _busy.IsCurrentJourney(uid, id);
    }
}
