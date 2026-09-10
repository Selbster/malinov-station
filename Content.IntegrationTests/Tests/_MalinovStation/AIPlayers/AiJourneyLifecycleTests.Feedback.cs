#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

public sealed partial class AiJourneyLifecycleTests
{
    [Test]
    public async Task UnreachableKnownPlace_AliasesRespectCooldown_AndExpiryAllowsTravelAgain()
    {
        await Prepare();
        var walls = new List<EntityUid>();
        await Server.WaitPost(() =>
        {
            Server.EntMan.GetComponent<MemoryComponent>(_actor).Memories.Clear();
            Server.System<MemorySystem>().AddMemory(_actor, "Зал прибытия", 0.8f, "landmark",
                location: _origin.Offset(new Vector2(-22, 0)), subject: "Зал прибытия");
            for (var y = -1; y <= 1; y++)
                walls.Add(Server.EntMan.SpawnEntity("WallSolid", _origin.Offset(new Vector2(-12, y))));
        });
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() => Assert.That(Server.System<AiActionRegistrySystem>().TryDoAction(_actor,
            "GoToKnownLocation", new GoToKnownLocationActionParams("прибытия"), out var reason), Is.True, reason));
        await Until(() => Busy.CurrentAction is null, "The unreachable journey did not finish.");
        await Server.WaitAssertion(() =>
        {
            Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.NoPath));
            var exploration = Server.EntMan.GetComponent<ExplorationComponent>(_actor);
            Assert.That(exploration.UnreachablePlaces, Does.ContainKey("Зал прибытия"));
            var registry = Server.System<AiActionRegistrySystem>();
            Assert.That(registry.GetEligibleActions(_actor).Any(a => a.Name == "GoToKnownLocation"), Is.False);
            var newExecution = Move(4);
            foreach (var hint in new[] { "Зал прибытия", "ПРИБЫТИЯ" })
            {
                Assert.That(registry.TryDoAction(_actor, "GoToKnownLocation", new GoToKnownLocationActionParams(hint),
                    out var reason), Is.False);
                Assert.That(reason, Does.Contain("выбрать другое место"));
                Assert.That(Busy.ExecutionId, Is.EqualTo(newExecution));
                Assert.That(Busy.CurrentAction, Is.EqualTo("MoveTo"));
            }
            Journeys.CancelCurrentAction(_actor, "Test cleanup");
            exploration.UnreachablePlaces["Зал прибытия"] = Server.Timing.CurTime;
            foreach (var wall in walls)
            {
                // Notify navigation before deleting the fixture that supplied its collision bounds.
                Server.System<SharedPhysicsSystem>().SetCanCollide(wall, false);
                Server.EntMan.DeleteEntity(wall);
            }
        });
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() => Assert.That(Server.System<AiActionRegistrySystem>().TryDoAction(_actor,
            "GoToKnownLocation", new GoToKnownLocationActionParams("ПРИБЫТИЯ"), out var reason), Is.True, reason));
        await Until(() => Busy.CurrentAction is null, "The restored route did not complete after expiry.");
        await Server.WaitAssertion(() => Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.Completed)));
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RejectedAction_PublishesOnce_AndLateFeedbackDoesNotChangeNewDecision(bool throughGateway)
    {
        await Prepare();
        await Server.WaitAssertion(() =>
        {
            var memories = Server.EntMan.GetComponent<MemoryComponent>(_actor).Memories;
            var before = memories.Count(m => m.Source == "outcome");
            var cognitive = Server.EntMan.GetComponent<CognitiveModeComponent>(_actor);
            cognitive.ReflectionAccumulator = 30f;
            var gateway = Server.System<LlmGatewaySystem>();
            var registry = Server.System<AiActionRegistrySystem>();
            var rejected = throughGateway
                ? gateway.TryApplyCognitiveDecision(_actor, new LlmCognitiveDecision(
                    "curiosity", "try_work", 0.5f, 0.8f, "test", "PursueGoal",
                    new Dictionary<string, string> { ["goal"] = "NotARealGoal" }))
                : registry.TryDoAction(_actor, "PursueGoal", new PursueGoalActionParams("NotARealGoal", 0.5f, "test"), out _);
            Assert.That(rejected, Is.False);
            var old = Trace.GetDecision(_actor, Trace.GetCurrentDecisionId(_actor))!;
            Assert.That(old.Result?.Outcome, Is.EqualTo(AiActionOutcome.Failed));
            Assert.That(old.CognitiveFeedbackDelivered, Is.True);
            Assert.That(memories.Count(m => m.Source == "outcome"), Is.EqualTo(before + 1));
            Assert.That(cognitive.ReflectionAccumulator, Is.Zero);

            cognitive.ReflectionAccumulator = 30f;
            Assert.That(gateway.TryApplyCognitiveDecision(_actor, new LlmCognitiveDecision(
                "curiosity", "keep_going", 0.5f, 0.8f, "test", "ContinueActivity",
                new Dictionary<string, string>())), Is.True);
            var current = Trace.GetDecision(_actor, Trace.GetCurrentDecisionId(_actor))!;
            Trace.DecisionResult(_actor, old.Id, AiActionResult.Failed("Late result"), publishFeedback: true);
            Assert.That(current.Result?.Outcome, Is.EqualTo(AiActionOutcome.Completed));
            Assert.That(memories.Count(m => m.Source == "outcome"), Is.EqualTo(before + 1));
            Assert.That(cognitive.ReflectionAccumulator, Is.EqualTo(30f));
            Assert.That(Trace.DescribeLastCognitiveDecision(_actor), Does.Contain($"#{current.Id}"));
            Assert.That(Trace.DescribeLastCognitiveDecision(_actor), Does.Contain("ContinueActivity"));
            Assert.That(Server.EntMan.GetComponent<GoalComponent>(_actor).LastLlmDecision, Is.Null);

            registry.TryDoAction(_actor, "ContinueActivity", new ContinueActivityActionParams(), out _);
            Assert.That(Trace.DescribeLastCognitiveDecision(_actor), Does.Contain($"#{current.Id},"),
                "Direct dispatch must not replace the last cognitive decision.");
        });
    }

    [Test]
    public async Task InvalidModelProposal_IsNotRememberedAsAnExecutionFailure()
    {
        await Prepare();
        await Server.WaitAssertion(() =>
        {
            var memories = Server.EntMan.GetComponent<MemoryComponent>(_actor).Memories;
            var before = memories.Count(m => m.Source == "outcome");
            var cognitive = Server.EntMan.GetComponent<CognitiveModeComponent>(_actor);
            cognitive.ReflectionAccumulator = 30f;
            Assert.That(Server.System<LlmGatewaySystem>().TryApplyCognitiveDecision(_actor,
                new LlmCognitiveDecision("curiosity", "invalid", 0.5f, 0.8f, "test", "NotAnAction",
                    new Dictionary<string, string>())), Is.False);
            var trace = Trace.GetDecision(_actor, Trace.GetCurrentDecisionId(_actor))!;
            Assert.That(trace.Finished, Is.True);
            Assert.That(trace.CognitiveFeedbackDelivered, Is.False);
            Assert.That(memories.Count(m => m.Source == "outcome"), Is.EqualTo(before));
            Assert.That(cognitive.ReflectionAccumulator, Is.EqualTo(30f));
        });
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task RepeatedJourneyCancellation_DoesNotCreateFailureBeliefs(bool cognitive)
    {
        await Prepare(cognitive);
        await Server.WaitAssertion(() =>
        {
            var memories = Server.EntMan.GetComponent<MemoryComponent>(_actor).Memories;
            var before = memories.Count(m => m.Source == "outcome");
            for (var i = 0; i < AiTraceSystem.ConsecutiveFailuresBeforeBelief; i++)
            {
                var execution = Move(12);
                Journeys.CancelCurrentAction(_actor, "Changed decision");
                Trace.ExecutionFinished(_actor, execution, AiActionResult.Failed("Late failure"));
                if (cognitive)
                    Assert.That(Trace.GetExecution(_actor, execution)!.CognitiveFeedbackDelivered, Is.True);
                else
                    Assert.That(Trace.GetExecution(_actor, execution), Is.Null);
            }
            Assert.That(Server.EntMan.GetComponent<AiTraceStateComponent>(_actor).ConsecutiveActionFailures, Is.Empty);
            Assert.That(memories.Count(m => m.Source == "outcome"),
                Is.EqualTo(before + (cognitive ? AiTraceSystem.ConsecutiveFailuresBeforeBelief : 0)));
        });
    }

    [Test]
    public async Task RepeatedUnreachableJourneys_PublishOneOutcomeEach_AndCreateFailureBelief()
    {
        await Prepare();
        var before = 0;
        await Server.WaitPost(() =>
        {
            before = Server.EntMan.GetComponent<MemoryComponent>(_actor).Memories.Count(m => m.Source == "outcome");
            for (var y = -1; y <= 1; y++)
                Server.EntMan.SpawnEntity("WallSolid", _origin.Offset(new Vector2(-12, y)));
        });
        await Pair.RunTicksSync(15);
        for (var i = 0; i < AiTraceSystem.ConsecutiveFailuresBeforeBelief; i++)
        {
            await Server.WaitPost(() => Move(22));
            await Until(() => Busy.CurrentAction is null, "An unreachable journey did not finish.");
            await Server.WaitAssertion(() => Assert.That(Busy.LastResult?.Outcome, Is.EqualTo(AiActionOutcome.NoPath)));
        }
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.EntMan.GetComponent<MemoryComponent>(_actor).Memories.Count(m => m.Source == "outcome"),
                Is.EqualTo(before + AiTraceSystem.ConsecutiveFailuresBeforeBelief));
            Assert.That(Server.EntMan.GetComponent<BeliefComponent>(_actor).Beliefs.Count(
                b => b.Source == "repeated-failure" && b.Subject == "MoveTo"), Is.EqualTo(1));
        });
    }
}
