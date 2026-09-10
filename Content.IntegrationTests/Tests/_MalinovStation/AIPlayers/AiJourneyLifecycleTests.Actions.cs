#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.Damage.Systems;
using Content.Server.NPC.HTN;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Physics.Systems;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

public sealed partial class AiJourneyLifecycleTests
{
    private async Task PrepareActions()
    {
        await Prepare(human: true);
        // The connected test player also spawns at the job marker and can occlude the actor's view.
        await Server.WaitPost(() =>
        {
            _origin = _origin.Offset(new Vector2(-8, 0));
            Server.System<SharedTransformSystem>().SetCoordinates(_actor, _origin);
            var grid = Server.EntMan.GetComponent<TransformComponent>(_actor).GridUid!.Value;
            Server.System<Content.Server.Gravity.GravitySystem>().EnableGravity(grid);
            Server.EntMan.GetComponent<Content.Shared.Gravity.GravityComponent>(grid).Inherent = true;
        });
        await Pair.RunTicksSync(5);
    }

    [Test]
    public async Task SpeechDuringCognitiveRequest_DoesNotConsumeThePendingDecision()
    {
        await Prepare(human: true);
        await Server.WaitAssertion(() =>
        {
            var pending = Trace.DecisionStarted(_actor, 0.5f, 0.5f, "Restlessness");
            Trace.MarkCognitiveDecision(_actor, pending);
            Assert.That(Server.System<AiActionRegistrySystem>().TryDoAction(_actor, "Talk",
                new TalkActionParams("Проверка связи."), out var reason), Is.True, reason);
            Assert.That(Trace.GetDecision(_actor, pending)!.Finished, Is.False);
            Assert.That(Trace.GetDecision(_actor, pending)!.SelectedAction, Is.Null);
            Assert.That(Trace.IsCurrentCognitiveDecision(_actor, pending), Is.True);
            var speech = Trace.GetCurrentDecisionId(_actor);
            Assert.That(speech, Is.Not.EqualTo(pending));
            Assert.That(Trace.GetDecision(_actor, speech)!.SelectedAction, Is.EqualTo("Talk"));
            Assert.That(Server.System<LlmGatewaySystem>().TryApplyCognitiveDecision(_actor,
                new LlmCognitiveDecision("Restlessness", "продолжить", 0.5f, 0.5f, "test", "ContinueActivity",
                    new Dictionary<string, string>()), pending), Is.True);
            Assert.That(Trace.GetDecision(_actor, pending)!.SelectedAction, Is.EqualTo("ContinueActivity"));
            Assert.That(Trace.GetDecision(_actor, speech)!.SelectedAction, Is.EqualTo("Talk"));
        });
    }

    [Test]
    public async Task PickingUpAPerson_IsRejectedAsAModelTargetError()
    {
        await Prepare(human: true);
        await Server.WaitAssertion(() =>
        {
            var item = Server.EntMan.SpawnEntity("Crowbar", _origin);
            Server.EntMan.GetComponent<ItemOpportunityComponent>(_actor).NearbyItems.Add(item);
            var memories = Server.EntMan.GetComponent<MemoryComponent>(_actor).Memories;
            var before = memories.Count(m => m.Source == "outcome");
            Assert.That(Server.System<LlmGatewaySystem>().TryApplyCognitiveDecision(_actor,
                new LlmCognitiveDecision("Socialize", "поговорить", 0.5f, 0.5f, "test", "PickUpItem",
                    new Dictionary<string, string> { ["target"] = "Цветок Слабости" })), Is.False);
            Assert.That(memories.Count(m => m.Source == "outcome"), Is.EqualTo(before));
            Assert.That(Trace.GetDecision(_actor, Trace.GetCurrentDecisionId(_actor))!.CognitiveFeedbackDelivered, Is.False);
        });
    }

    [TestCase("food", "FoodSnackBoritos")]
    [TestCase("еда", "FoodSnackBoritos")]
    [TestCase("water", "DrinkWaterJug")]
    public async Task SearchCategory_FindsConsumablesWithoutMatchingTheirNames(string keyword, string prototype)
    {
        await PrepareActions();
        EntityUid item = default;
        await Server.WaitPost(() => item = Server.EntMan.SpawnEntity(prototype, _origin.Offset(new Vector2(-4, 0))));
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            Assert.That(Server.System<AiActionRegistrySystem>().TryDoAction(_actor, "SearchArea",
                new SearchAreaActionParams(keyword), out var reason), Is.True, reason);
            Assert.That(Server.EntMan.GetComponent<MemoryComponent>(_actor).Memories.Any(m =>
                m.Source == "search-result" && m.Participants.Contains(item)), Is.True);
        });
    }

    [Test]
    public async Task SearchPeople_RequiresAnUnobstructedView()
    {
        await PrepareActions();
        EntityUid person = default;
        var walls = new List<EntityUid>();
        await Server.WaitPost(() =>
        {
            person = Server.EntMan.SpawnEntity("MobHuman", _origin.Offset(new Vector2(-6, 0)));
            for (var y = -1; y <= 1; y++)
                walls.Add(Server.EntMan.SpawnEntity("WallSolid", _origin.Offset(new Vector2(-3, y))));
        });
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            // Named lookup excludes the incidental player spawned by the test round.
            var name = Server.EntMan.GetComponent<MetaDataComponent>(person).EntityName;
            Assert.That(Server.System<AiActionRegistrySystem>().TryDoAction(_actor, "SearchArea",
                new SearchAreaActionParams(name), out _), Is.False);
            foreach (var wall in walls)
                Server.System<SharedPhysicsSystem>().SetCanCollide(wall, false);
        });
        await Pair.RunTicksSync(60);
        await Server.WaitAssertion(() =>
        {
            var name = Server.EntMan.GetComponent<MetaDataComponent>(person).EntityName;
            Assert.That(Server.System<AiActionRegistrySystem>().TryDoAction(_actor, "SearchArea",
                new SearchAreaActionParams(name), out var reason), Is.True, reason);
            Assert.That(Server.EntMan.GetComponent<MemoryComponent>(_actor).Memories.Any(m =>
                m.Source == "search-result" && m.Participants.Contains(person)), Is.True);
        });
    }

    [Test]
    public async Task ModerateFatigue_SelectedRestActuallyRecovers()
    {
        await Prepare(human: true);
        await Server.WaitPost(() =>
        {
            Server.System<GodmodeSystem>().EnableGodmode(_actor);
            var needs = Server.EntMan.GetComponent<NeedsComponent>(_actor);
            needs.Fatigue = 0.5f;
            needs.SocialNeed = 0;
            var goal = Server.EntMan.GetComponent<GoalComponent>(_actor);
            goal.CurrentGoal = "Rest";
            Htn.RootTask = new HTNCompoundTask { Task = "RestCompound" };
        });
        await Until(() => Server.EntMan.GetComponent<NeedsComponent>(_actor).Fatigue <= 0.3f,
            "The selected Rest goal did not recover moderate fatigue.", iterations: 250);
    }

    [TestCase(false)]
    [TestCase(true)]
    public async Task NutritionGoal_WalksToConsumableAndSatiationIncreases(bool thirsty)
    {
        await PrepareActions();
        EntityUid food = default;
        await Server.WaitPost(() =>
        {
            Server.System<GodmodeSystem>().EnableGodmode(_actor);
            food = Server.EntMan.SpawnEntity(thirsty ? "DrinkWaterJug" : "FoodSnackBoritos", _origin.Offset(new Vector2(-5, 0)));
            var satiation = Server.EntMan.GetComponent<SatiationComponent>(_actor);
            var system = Server.System<SatiationSystem>();
            system.SetValue((_actor, satiation), SatiationSystem.Hunger, thirsty ? 300f : 20f);
            system.SetValue((_actor, satiation), SatiationSystem.Thirst, thirsty ? 20f : 300f);
            Htn.RootTask = new HTNCompoundTask { Task = "AiFoodCompound" };
        });
        await Until(() => Server.System<SatiationSystem>().GetValueOrNull(
                (_actor, Server.EntMan.GetComponent<SatiationComponent>(_actor)),
                thirsty ? SatiationSystem.Thirst : SatiationSystem.Hunger) > 25f,
            "The AI did not consume the reachable item.", iterations: 300);
        await Server.WaitAssertion(() => Assert.That(
            Server.EntMan.GetComponent<TransformComponent>(_actor).Coordinates.Position.X,
            Is.LessThan(_origin.Position.X - 2f)));
    }
}
