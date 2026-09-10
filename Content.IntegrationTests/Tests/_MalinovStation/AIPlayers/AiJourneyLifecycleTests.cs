#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.HTN.Operators;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC;
using Content.Server.NPC.Components;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Server.NPC.Systems;
using Content.Server.Power.Components;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared._MalinovStation.AIPlayers;
using Content.Shared.CCVar;
using Content.Shared.NPC;
using Content.Shared.Doors.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Content.Shared.Station.Components;
using Robust.Shared.CPUJob.JobQueues;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>Real HTN and physics execution on a floored corridor, without external model requests.</summary>
[TestFixture]
public sealed partial class AiJourneyLifecycleTests : GameTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: gameMap
  id: AiJourneyLifecycleMap
  mapName: Journey tests
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
      - type: StationNameSetup
        mapNameTemplate: Empty
      - type: StationJobs
        availableJobs:
          Passenger: [-1, -1]
- type: entity
  id: TestJourneyAirlock
  parent: Airlock
  components:
  - type: AccessReader
    containerAccessProvider: null
- type: htnCompound
  id: TestRoutineDoorMove
  branches:
  - tasks:
    - !type:HTNPrimitiveTask
      preconditions:
      - !type:KeyExistsPrecondition
        key: TargetCoordinates
      operator: !type:MoveToOperator
        pathfindInPlanning: false
";

    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false, Connected = true, InLobby = true, Dirty = true,
    };

    private EntityUid _actor;
    private EntityCoordinates _origin;
    private AiBusyStateComponent Busy => Server.EntMan.GetComponent<AiBusyStateComponent>(_actor);
    private HTNComponent Htn => Server.EntMan.GetComponent<HTNComponent>(_actor);
    private AiBusyStateSystem Journeys => Server.System<AiBusyStateSystem>();
    private AiTraceSystem Trace => Server.System<AiTraceSystem>();

    private async Task Prepare(bool cognitive = true, bool human = false)
    {
        await Server.WaitPost(() =>
        {
            Server.CfgMan.SetCVar(CCVars.GameMap, "AiJourneyLifecycleMap");
            Server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersLlmEnabled, false);
            var ticker = Server.System<GameTicker>();
            ticker.ToggleReadyAll(true);
            ticker.StartRound();
        });
        await Pair.RunTicksSync(10);
        await Server.WaitPost(() =>
        {
            var stations = Server.EntMan.EntityQueryEnumerator<StationDataComponent>();
            Assert.That(stations.MoveNext(out var station, out _), Is.True);
            _actor = Server.System<AIPlayerSystem>().SpawnAiPlayer("Passenger", station, profile: human ? Content.Shared.Preferences.HumanoidCharacterProfile.RandomWithSpecies("Human") : null, cognitiveMode: cognitive)!.Value;
            _origin = Server.EntMan.GetComponent<TransformComponent>(_actor).Coordinates;
            var xform = Server.EntMan.GetComponent<TransformComponent>(_actor);
            var gridUid = xform.GridUid!.Value;
            var grid = Server.EntMan.GetComponent<MapGridComponent>(gridUid);
            // Building the corridor tile by tile must not split an intermediate disconnected island.
            grid.CanSplit = false;
            var maps = Server.System<SharedMapSystem>();
            var originTile = maps.CoordinatesToTile(gridUid, grid, _origin);
            var tile = new Tile(Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            for (var x = -26; x <= 2; x++)
            for (var y = -1; y <= 1; y++)
                maps.SetTile(gridUid, grid, originTile + new Vector2i(x, y), tile);

            // Disable autonomous requests and unrelated survival arbitration for this movement fixture.
            if (cognitive)
                Server.EntMan.GetComponent<CognitiveModeComponent>(_actor).ReflectionAccumulator = 10000f;
            var goal = Server.EntMan.GetComponent<GoalComponent>(_actor);
            goal.CurrentGoal = "Idle";
            goal.ReconsiderAccumulator = 10000f;
            Htn.RootTask = new HTNCompoundTask { Task = "ForcedMoveCompound" };
        });
        // Allow navmesh updates to observe the newly laid floor.
        await Pair.RunTicksSync(15);
    }

    private long Move(float west, string? place = null)
    {
        if (place is null)
        {
            Assert.That(Server.System<AiActionRegistrySystem>().TryDoAction(_actor, "MoveTo",
                new MoveToActionParams(_origin.Offset(new Vector2(-west, 0))), out var reason), Is.True, reason);
        }
        else
        {
            Server.System<MemorySystem>().AddMemory(_actor, content: place, importance: 0.5f,
                source: "landmark", subject: place, location: _origin.Offset(new Vector2(-west, 0)));
            Assert.That(Server.System<AiActionRegistrySystem>().TryDoAction(_actor, GoToKnownLocationAction.ActionName,
                new GoToKnownLocationActionParams(place), out var reason), Is.True, reason);
        }
        return Busy.ExecutionId;
    }

    private async Task Until(System.Func<bool> condition, string failure, int iterations = 100)
    {
        for (var i = 0; i < iterations; i++)
        {
            var done = false;
            await Server.WaitPost(() => done = condition());
            if (done)
                return;
            await Pair.RunTicksSync(5);
        }
        Assert.Fail(failure);
    }

    private void AssertStopped()
    {
        Assert.Multiple(() =>
        {
            Assert.That(Busy.CurrentAction, Is.Null);
            Assert.That(Busy.JourneyTarget, Is.Null);
            Assert.That(Htn.Plan, Is.Null);
            Assert.That(Htn.PlanningJob, Is.Null);
            Assert.That(Htn.Blackboard.ContainsKey(MoveToAction.ForcedDestinationKey), Is.False);
            Assert.That(Server.EntMan.HasComponent<NPCSteeringComponent>(_actor), Is.False);
            Assert.That(Server.EntMan.GetComponent<DoorApproachComponent>(_actor).ActiveDoor, Is.Null);
        });
    }

    [Test]
    public async Task MovingThenAccessDenied_RecordsOriginalResult_AndCanTravelAgain()
    {
        await Prepare();
        EntityUid door = default;
        long id = 0;
        await Server.WaitPost(() =>
        {
            var coords = _origin.Offset(new Vector2(-14, 0));
            door = Server.EntMan.SpawnEntity("TestJourneyAirlock", coords);
            Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(Vector2.UnitY));
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(-Vector2.UnitY));
        });

        await Pair.RunTicksSync(15);
        await Server.WaitPost(() => id = Move(22, "Дальний отсек"));

        await Until(() => Busy.ProgressConfirmed &&
            Server.EntMan.TryGetComponent<NPCSteeringComponent>(_actor, out var steering) && steering.CurrentPath.Count > 0,
            "The passenger never physically advanced along a resolved path.");
        await Server.WaitPost(() =>
        {
            // A fresh reflection while travelling must not acquire the old journey's eventual failure.
            Trace.DecisionStarted(_actor, 0.5f, 0.5f, "Restlessness");
            Server.System<AccessReaderSystem>().TrySetAccesses(
                (door, Server.EntMan.GetComponent<AccessReaderComponent>(door)),
                new List<ProtoId<AccessLevelPrototype>> { "Engineering" });
        });
        await Until(() => Busy.CurrentAction is null, "A denied journey remained active.");
        await Server.WaitAssertion(() =>
        {
            var trace = Trace.GetExecution(_actor, id)!;
            Assert.Multiple(() =>
            {
                Assert.That(trace.MovementStarted, Is.True);
                Assert.That(trace.NavigationStart, Is.EqualTo(_origin.ToString()));
                Assert.That(trace.NavigationEnd, Is.Not.Null.And.Not.EqualTo(trace.NavigationStart));
                Assert.That(trace.Result?.Outcome, Is.EqualTo(AiActionOutcome.AccessDenied));
                Assert.That(trace.Finished, Is.True);
                Assert.That(trace.CognitiveFeedbackDelivered, Is.True);
                Assert.That(Trace.GetDecision(_actor, Trace.GetCurrentDecisionId(_actor))!.Result, Is.Null);
                Assert.That(Server.EntMan.GetComponent<ExplorationComponent>(_actor).UnreachablePlaces,
                    Does.ContainKey("Дальний отсек"));
            });
        });

        await Server.WaitPost(() => Move(1));
        await Until(() => Busy.CurrentAction is null, "The new reachable journey did not finish.");
        await Server.WaitAssertion(() => Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed)));
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task CancelledPlanning_LateResultCannotRestartOrDamageNewJourney(bool finishBeforeCancellation)
    {
        await Prepare();
        var gate = new TaskCompletionSource<bool>();
        var delayed = new DelayedJourneyTestOperator { Gate = gate.Task };
        HTNPlanJob job = default!;
        var token = new CancellationTokenSource();
        long old = 0;
        await Server.WaitPost(() =>
        {
            old = Move(24);
            job = new HTNPlanJob(0, Server.ProtoMan, new HTNPrimitiveTask { Operator = delayed },
                Htn.Blackboard.ShallowClone(), null, token.Token);
            Htn.PlanningToken = token;
            Htn.PlanningJob = job;
            job.Run();
        });
        await Server.WaitAssertion(() => Assert.That(job.Status, Is.EqualTo(JobStatus.Waiting)));

        if (finishBeforeCancellation)
        {
            await Server.WaitPost(() =>
            {
                gate.SetResult(true);
                job.Run();
            });
            await Server.WaitAssertion(() => Assert.That(job.Result, Is.Not.Null));
        }

        await Server.WaitPost(() =>
        {
            Assert.That(Journeys.FinishJourney(_actor, old, AiActionResult.Cancelled("Смена цели")), Is.True);
            AssertStopped();
            Move(10);
            if (!finishBeforeCancellation)
            {
                gate.SetResult(true);
                job.Run();
            }
            // Both repeated terminal notifications and late observations carry the old execution ID.
            Assert.That(Journeys.FinishJourney(_actor, old, AiActionResult.NoPath("Старый ответ")), Is.False);
            Journeys.QueueJourneyResult(_actor, old, AiActionResult.Failed("Поздний callback"));
            Trace.ExecutionFinished(_actor, old, AiActionResult.Failed("Поздняя ошибка"));
        });
        await Until(() => Busy.ProgressConfirmed, "A new journey could not move after cancelled planning.");
        await Server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(token.IsCancellationRequested, Is.True);
                Assert.That(job.Exception, Is.Null);
                Assert.That(delayed.Startups, Is.Zero);
                Assert.That(Busy.ExecutionId, Is.GreaterThan(old));
                Assert.That(Trace.GetExecution(_actor, old)!.Result?.Outcome, Is.EqualTo(AiActionOutcome.Cancelled));
                Assert.That(Trace.GetExecution(_actor, Busy.ExecutionId)!.Result, Is.Null);
                Assert.That(Server.EntMan.GetComponent<MemoryComponent>(_actor).Memories.Count(m =>
                    m.Source == "outcome" && m.Content.Contains("Смена цели")), Is.EqualTo(1));
            });
        });
    }

    [Test]
    public async Task StationarySteering_DoesNotConfirmProgress_AndTimeoutStopsExecution()
    {
        await Prepare();
        await Server.WaitPost(() =>
        {
            Move(24);
            Htn.Enabled = false;
            Server.System<NPCSteeringSystem>().Register(_actor, Busy.JourneyTarget!.Value);
            Journeys.JourneySteeringStarted(_actor, Busy.ExecutionId);
            // Steering exists, but the physics/HTN loop is paused for this actor.
            Server.EntMan.RemoveComponent<ActiveNPCComponent>(_actor);
            Busy.MaxBusyDurationSeconds = 2f;
        });
        await Pair.RunTicksSync(35);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Busy.ProgressConfirmed, Is.False);
            Assert.That(Trace.GetExecution(_actor, Busy.ExecutionId)!.SteeringStarted, Is.True);
            Assert.That(Trace.GetExecution(_actor, Busy.ExecutionId)!.MovementStarted, Is.False);
        });
        await Until(() => Busy.CurrentAction is null, "Timeout did not release the commitment.");
        await Server.WaitAssertion(() =>
        {
            AssertStopped();
            Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Timeout));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task ArrivalSucceeds_ExternalRelocationCancelsWithoutDenyingPlace(bool cognitive)
    {
        await Prepare(cognitive);
        await Server.WaitPost(() => Move(8));
        await Until(() => Busy.CurrentAction is null, "The reachable destination was not reached.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed));
            Assert.That(Busy.ProgressConfirmed, Is.True);
        });
        await Server.WaitPost(() =>
        {
            Move(22, "Другое место");
            Server.System<SharedTransformSystem>().SetCoordinates(_actor, _origin);
        });
        await Until(() => Busy.CurrentAction is null, "Relocation did not cancel the old journey.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Cancelled));
            Assert.That(Busy.ProgressConfirmed, Is.False);
            if (Server.EntMan.TryGetComponent<ExplorationComponent>(_actor, out var exploration))
                Assert.That(exploration.UnreachablePlaces, Does.Not.ContainKey("Другое место"));
        });
    }

    [Test]
    public async Task SolidWall_ReportsNoPath_AndRemembersUnreachablePlace()
    {
        await Prepare();
        await Server.WaitPost(() =>
        {
            for (var y = -1; y <= 1; y++)
                Server.EntMan.SpawnEntity("WallSolid", _origin.Offset(new Vector2(-12, y)));
        });
        await Pair.RunTicksSync(15);
        await Server.WaitPost(() => Move(22, "За стеной"));
        await Until(() => Busy.CurrentAction is null, "An impossible route remained active.");
        await Server.WaitAssertion(() =>
        {
            AssertStopped();
            Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.NoPath));
            Assert.That(Server.EntMan.GetComponent<ExplorationComponent>(_actor).UnreachablePlaces,
                Does.ContainKey("За стеной"));
        });
    }

    [Test]
    public async Task OrdinaryRoute_AccessRevoked_DropsWanderCommitmentAndStopsReturningToDoor()
    {
        await Prepare(false);
        EntityUid door = default;
        await Server.WaitPost(() =>
        {
            var coords = _origin.Offset(new Vector2(-14, 0));
            door = Server.EntMan.SpawnEntity("TestJourneyAirlock", coords);
            Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(Vector2.UnitY));
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(-Vector2.UnitY));
        });
        await Pair.RunTicksSync(15);
        await Server.WaitPost(() =>
        {
            Htn.RootTask = new HTNCompoundTask { Task = "TestRoutineDoorMove" };
            var target = _origin.Offset(new Vector2(-22, 0));
            Htn.Blackboard.SetValue("TargetCoordinates", target);
            Htn.Blackboard.SetValue(PickRememberedOrAccessibleOperator.WanderTargetKey, target);
            Htn.Blackboard.SetValue(PickRememberedOrAccessibleOperator.WanderExpiryKey, SGameTiming.CurTime + System.TimeSpan.FromMinutes(1));
        });
        await Until(() => Server.EntMan.TryGetComponent<NPCSteeringComponent>(_actor, out var steering) &&
            steering.CurrentPath.Count > 0, "The ordinary route never resolved.");
        await Server.WaitPost(() => Server.System<AccessReaderSystem>().TrySetAccesses(
            (door, Server.EntMan.GetComponent<AccessReaderComponent>(door)),
            new List<ProtoId<AccessLevelPrototype>> { "Engineering" }));
        await Until(() => !Server.EntMan.HasComponent<NPCSteeringComponent>(_actor), "The ordinary route kept steering into a denied door.");
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            AssertStopped();
            Assert.That(Htn.Blackboard.ContainsKey(PickRememberedOrAccessibleOperator.WanderTargetKey), Is.False);
            Assert.That(Server.EntMan.GetComponent<DoorApproachComponent>(_actor).DeniedDoors, Does.ContainKey(door));
        });
    }

    [TestCase(true, false, false)]
    [TestCase(false, false, false)]
    [TestCase(true, true, false)]
    [TestCase(true, true, true)]
    public async Task UnpoweredDoor_WithCrowbar_IsPriedOpenAndJourneyCompletes(bool cognitive, bool locked, bool stored)
    {
        await Prepare(cognitive);
        EntityUid door = default;
        await Server.WaitPost(() =>
        {
            var coords = _origin.Offset(new Vector2(-6, 0));
            door = Server.EntMan.SpawnEntity("TestJourneyAirlock", coords);
            if (locked)
                Server.System<AccessReaderSystem>().TrySetAccesses(
                    (door, Server.EntMan.GetComponent<AccessReaderComponent>(door)),
                    new List<ProtoId<AccessLevelPrototype>> { "Engineering" });
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(Vector2.UnitY));
            Server.EntMan.SpawnEntity("WallSolid", coords.Offset(-Vector2.UnitY));
            if (stored)
                Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = false;
        });
        if (stored)
        {
            await Until(() => Server.EntMan.GetComponent<DoorApproachComponent>(_actor).DeniedDoors.ContainsKey(door),
                "The powered, access-locked door was never recorded as inaccessible.");
        }
        await Server.WaitPost(() =>
        {
            Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).NeedsPower = true;
            var crowbar = Server.EntMan.SpawnEntity("Crowbar", _origin);
            if (stored)
            {
                Assert.That(Server.System<InventorySystem>().TryGetSlotEntity(_actor, "back", out var backpack), Is.True);
                var storage = Server.EntMan.GetComponent<StorageComponent>(backpack!.Value);
                Assert.That(Server.System<SharedContainerSystem>().Insert(crowbar, storage.Container), Is.True);
            }
            else
                Assert.That(Server.System<SharedHandsSystem>().TryPickupAnyHand(_actor, crowbar), Is.True);
        });
        await Pair.RunTicksSync(15);
        await Until(() => !Server.EntMan.GetComponent<DoorApproachComponent>(_actor).DeniedDoors.ContainsKey(door),
            "A valid prying tool did not release the obsolete access denial.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.EntMan.GetComponent<ApcPowerReceiverComponent>(door).Powered, Is.False);
            Assert.That(Server.System<AiDoorApproachSystem>().JudgeDoor(_actor, door,
                Server.EntMan.GetComponent<DoorComponent>(door), true).Passability, Is.EqualTo(DoorPassability.Pry));
        });
        await Server.WaitPost(() => Move(12));
        await Until(() => Server.EntMan.GetComponent<DoorApproachComponent>(_actor).PryDoAfter is not null,
            "The passenger never started prying the unpowered door.");
        await Until(() => Busy.CurrentAction is null, "Prying never released the journey.", iterations: 200);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.EntMan.GetComponent<DoorComponent>(door).State, Is.EqualTo(DoorState.Open));
            Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed));
            Assert.That(Server.EntMan.GetComponent<TransformComponent>(_actor).Coordinates.X,
                Is.LessThan(_origin.X - 6));
        });
    }

    [Test]
    public async Task ReplacingMovingJourney_CancelsOldExecution_AndReachesNewTarget()
    {
        await Prepare();
        long old = 0;
        await Server.WaitPost(() => old = Move(24, "Прежняя цель"));
        await Until(() => Busy.ProgressConfirmed, "The first journey did not move.");
        await Server.WaitPost(() => Move(1));
        await Until(() => Busy.CurrentAction is null, "The replacement journey did not finish.");
        await Server.WaitAssertion(() =>
        {
            AssertStopped();
            Assert.That(Trace.GetExecution(_actor, old)!.Result?.Outcome, Is.EqualTo(AiActionOutcome.Cancelled));
            Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed));
            Assert.That(Server.EntMan.GetComponent<ExplorationComponent>(_actor).UnreachablePlaces,
                Does.Not.ContainKey("Прежняя цель"));
        });
    }
}

/// <summary>A planner held at an actual asynchronous boundary; Startup must never run after cancellation.</summary>
public sealed partial class DelayedJourneyTestOperator : HTNOperator
{
    public Task<bool> Gate = default!;
    public int Startups;

    public override async Task<(bool Valid, Dictionary<string, object>? Effects)> Plan(NPCBlackboard blackboard,
        CancellationToken cancelToken)
    {
        await Gate;
        return (true, null);
    }

    public override void Startup(NPCBlackboard blackboard) => Startups++;
}
