using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.Systems;
using Content.Shared.DoAfter;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Owns extended action completion. HTN reports terminal observations; teardown runs outside HTN callbacks.
/// </summary>
public sealed partial class AiBusyStateSystem : EntitySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private HTNSystem _htn = default!;
    [Dependency] private NPCSteeringSystem _steering = default!;
    [Dependency] private AiDoorApproachSystem _doors = default!;
    [Dependency] private AiTraceSystem _trace = default!;
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;

    private const float ScanCooldown = 1f;
    private const float MaxPlausibleWalkSpeed = 10f;
    private const float ProgressDistance = 0.5f;
    private float _accumulator;

    public override void Initialize()
    {
        base.Initialize();
        UpdatesBefore.Add(typeof(NPCSystem));
        UpdatesBefore.Add(typeof(NPCSteeringSystem));
        SubscribeLocalEvent<AiBusyStateComponent, MoveEvent>(OnMove);
    }

    private void OnMove(Entity<AiBusyStateComponent> ent, ref MoveEvent args)
    {
        if (ent.Comp.CurrentAction is null || args.OnlyRotation)
            return;

        // Catch discontinuities between sparse scans, including a teleport onto the destination.
        if (!args.OldPosition.TryDistance(EntityManager, args.NewPosition, out var distance) ||
            distance > Math.Max(ProgressDistance, MaxPlausibleWalkSpeed * _timing.TickPeriod.TotalSeconds))
        {
            ent.Comp.ExternallyRelocated = true;
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        _accumulator -= frameTime;
        var scan = _accumulator <= 0f;
        if (scan)
            _accumulator = ScanCooldown;

        var query = EntityQueryEnumerator<AiBusyStateComponent>();
        while (query.MoveNext(out var uid, out var busy))
        {
            if (busy.CurrentAction is null)
                continue;

            if (busy.ExternallyRelocated)
            {
                FinishAction(uid, busy, AiActionResult.Cancelled("Тебя переместили; прежнее действие отменено."));
                continue;
            }

            if (busy.PendingResult is { } result)
            {
                ObserveProgress(uid, busy);
                FinishJourney(uid, busy.ExecutionId, result);
                continue;
            }

            if (busy.JourneyTarget is not null && !busy.SteeringStarted &&
                TryComp<HTNComponent>(uid, out var planning) && planning.Planning)
                _trace.ExecutionPlanning(uid, busy.ExecutionId);

            if (scan)
                CheckBusyState(uid, busy);
        }
    }

    private void CheckBusyState(EntityUid uid, AiBusyStateComponent busy)
    {
        if (_timing.CurTime - busy.StartedAt > TimeSpan.FromSeconds(busy.MaxBusyDurationSeconds))
        {
            FinishAction(uid, busy, AiActionResult.Timeout("Время на выполнение действия истекло."));
            return;
        }

        if (busy.JourneyTarget is { } target)
        {
            ObserveProgress(uid, busy);
            if (!TryComp<HTNComponent>(uid, out var htn) ||
                !htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out var current, EntityManager))
            {
                // Missing data alone is not evidence that a place is unreachable.
                var arrived = TryComp(uid, out TransformComponent? xform) &&
                    xform.Coordinates.TryDistance(EntityManager, target, out var distance) && distance <= 1.5f;
                FinishJourney(uid, busy.ExecutionId, arrived
                    ? AiActionResult.Completed("Ты добрался (добралась) до цели.")
                    : AiActionResult.Cancelled("Маршрут был прерван."));
                return;
            }

            if (current != target)
            {
                var action = busy.CurrentAction!;
                FinishJourney(uid, busy.ExecutionId, AiActionResult.Cancelled("Выбрана другая цель."));
                StartJourney(uid, action, current);
                return;
            }
        }

        if (busy.CancellationAllowed && IsDangerActive(uid))
        {
            FinishAction(uid, busy, AiActionResult.Cancelled("Опасность прервала текущее действие."));
            return;
        }

        if (busy.JourneyTarget is null &&
            (busy.CurrentAction != PursueGoalAction.ActionName ||
             !TryComp<GoalComponent>(uid, out var goal) || !goal.IsLlmOverride))
        {
            FinishAction(uid, busy, AiActionResult.Completed("Текущее занятие завершено."), stopExecution: false);
        }
    }

    private void ObserveProgress(EntityUid uid, AiBusyStateComponent busy)
    {
        if (busy.ExternallyRelocated || !busy.SteeringStarted || busy.ProgressConfirmed ||
            busy.JourneyStart is not { } start || !TryComp(uid, out TransformComponent? xform) ||
            !start.TryDistance(EntityManager, xform.Coordinates, out var distance) || distance < ProgressDistance)
        {
            return;
        }

        busy.ProgressConfirmed = true;
        _trace.DecisionMovementStarted(uid, busy.ExecutionId);
    }

    private bool IsDangerActive(EntityUid uid) =>
        TryComp<DangerComponent>(uid, out var danger) &&
        ((danger.ThreatSource is not null && _timing.CurTime < danger.ThreatExpiresAt) || danger.FireHazardLocation is not null);

    private static void Clear(AiBusyStateComponent busy)
    {
        busy.CurrentAction = null;
        busy.Reason = string.Empty;
        busy.JourneyTarget = null;
        busy.JourneyPlace = null;
        busy.PendingResult = null;
    }
}
