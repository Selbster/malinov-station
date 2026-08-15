#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// Milestone 6 acceptance checks: two AI players wanting to socialize exchange exactly one bounded
/// greeting/reply (never an open-ended loop), which always happens (via the fallback line dataset when the
/// LLM is disabled/unavailable) and leaves both with a memory and a small relationship nudge.
/// </summary>
[TestFixture]
public sealed class SocialSystemTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "AIPlayerSocialTestMap";

    [TestPrototypes]
    private static readonly string AIPlayerSocialTestMap = @$"
- type: gameMap
  id: {Map}
  mapName: {Map}
  mapPath: /Maps/Test/empty.yml
  minPlayers: 0
  stations:
    Empty:
      stationProto: StandardNanotrasenStation
      components:
        - type: StationNameSetup
          mapNameTemplate: ""Empty""
        - type: StationJobs
          availableJobs:
            {Passenger}: [ -1, -1 ]
";

    public override PoolSettings PoolSettings => new()
    {
        DummyTicker = false,
        Connected = true,
        InLobby = true,
        Dirty = true,
    };

    private async Task<EntityUid> StartRoundAndGetStation(TestPair pair)
    {
        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.GameMap, Map);
        var ticker = server.System<GameTicker>();

        ticker.ToggleReadyAll(true);
        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        var station = server.EntMan.EntityQuery<StationDataComponent>().Select(x => x.Owner).FirstOrDefault();
        Assert.That(server.EntMan.EntityExists(station), "Expected a station to exist after round start.");
        return station;
    }

    private async Task<(EntityUid A, EntityUid B)> SpawnCoLocatedPair(TestPair pair, EntityUid station)
    {
        var server = pair.Server;
        EntityUid a = default;
        EntityUid b = default;

        await server.WaitPost(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            a = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;
            b = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;

            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            var aCoords = server.EntMan.GetComponent<TransformComponent>(a).Coordinates;
            xformSystem.SetCoordinates(b, aCoords);
        });

        return (a, b);
    }

    /// <summary>
    /// With the LLM disabled (the default in tests), two co-located AI players both wanting to socialize
    /// still complete a full greeting exchange via the fallback line dataset - proving conversations never
    /// depend on the LLM to function, and that the exchange is bounded (both end up idle again afterward).
    /// </summary>
    [Test]
    public async Task TwoSocializingAiPlayers_ExchangeGreetingViaFallback_AndRecordMemoryAndRelationship()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (a, b) = await SpawnCoLocatedPair(pair, station);

        await server.WaitPost(() =>
        {
            foreach (var uid in new[] { a, b })
            {
                var goal = server.EntMan.GetComponent<GoalComponent>(uid);
                goal.CurrentGoal = AIGoals.Socialize;
                goal.ReconsiderAccumulator = 999f; // don't let GoalSystem overwrite this mid-test

                var perception = server.EntMan.GetComponent<PerceptionComponent>(uid);
                perception.PerceiveAccumulator = 0f;
            }
        });

        // A perception scan (to see the partner) + the social exchange itself, both driven by Update().
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            foreach (var uid in new[] { a, b })
            {
                var conversation = server.EntMan.GetComponent<ConversationComponent>(uid);
                Assert.That(conversation.State, Is.EqualTo(ConversationState.None),
                    $"{uid} should have finished its bounded exchange, not be left mid-conversation.");
                Assert.That(conversation.Partner, Is.Null);
                Assert.That(conversation.CooldownUntil, Is.GreaterThan(TimeSpan.Zero),
                    "A cooldown should be set after a conversation, preventing an immediate repeat.");

                var memory = server.EntMan.GetComponent<MemoryComponent>(uid);
                Assert.That(memory.Memories.Any(m => m.Source == "conversation"), Is.True,
                    $"{uid} should have a memory of the conversation.");
            }

            var relationships = server.EntMan.GetComponent<RelationshipComponent>(a);
            Assert.That(relationships.Relationships.TryGetValue(b, out var data), Is.True);
            Assert.That(data!.Friendship, Is.GreaterThan(0f), "A small talk exchange should nudge friendship up.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// If neither AI player's current goal is Socialize, SocialSystem must not start a conversation between
    /// them even if they can see each other - it should never override what the Goal System decided.
    /// </summary>
    [Test]
    public async Task DoesNotStartConversation_WhenGoalIsNotSocialize()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (a, b) = await SpawnCoLocatedPair(pair, station);

        await server.WaitPost(() =>
        {
            foreach (var uid in new[] { a, b })
            {
                var goal = server.EntMan.GetComponent<GoalComponent>(uid);
                goal.CurrentGoal = AIGoals.Idle;
                goal.ReconsiderAccumulator = 999f;

                var perception = server.EntMan.GetComponent<PerceptionComponent>(uid);
                perception.PerceiveAccumulator = 0f;
            }
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            foreach (var uid in new[] { a, b })
            {
                var conversation = server.EntMan.GetComponent<ConversationComponent>(uid);
                Assert.That(conversation.State, Is.EqualTo(ConversationState.None));
                Assert.That(conversation.Partner, Is.Null);

                var memory = server.EntMan.GetComponent<MemoryComponent>(uid);
                Assert.That(memory.Memories.Any(m => m.Source == "conversation"), Is.False);
            }
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Milestone 7's rumor spreading: if the initiator has an important, unshared firsthand memory (e.g.
    /// "was attacked by C"), its opening line relays that instead of small talk, and the listener ends up
    /// with a secondhand memory about C plus a small relationship nudge toward them - without ever having
    /// met C itself.
    /// </summary>
    [Test]
    public async Task OpeningLine_SharesImportantUnsharedMemory_AsRumor_WithListener()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (a, b) = await SpawnCoLocatedPair(pair, station);

        EntityUid c = default;

        await server.WaitPost(() =>
        {
            c = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station)!.Value;

            // Move C far out of vision range of A/B: this test is about the rumor mechanism specifically, so
            // B must not be able to learn about C by directly perceiving it (the test map is small enough
            // that C's random spawn point could otherwise land within default vision range of A/B).
            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            var aCoords = server.EntMan.GetComponent<TransformComponent>(a).Coordinates;
            xformSystem.SetCoordinates(c, aCoords.Offset(new System.Numerics.Vector2(500, 500)));

            // Give A a firsthand "danger" memory about C, above the rumor-sharing importance threshold -
            // the same shape DangerSystem.OnDamaged would produce for a real attack.
            server.System<MemorySystem>().AddMemory(
                a,
                content: "Was attacked by C!",
                importance: 0.7f,
                source: "danger",
                participants: new[] { c },
                emotionalWeight: -0.8f);

            // Only A wants to socialize - since FindPartner doesn't require the partner to also be seeking a
            // conversation, this deterministically makes A the initiator (and thus the one who might share
            // the rumor), without needing to guess entity iteration order.
            var goalA = server.EntMan.GetComponent<GoalComponent>(a);
            goalA.CurrentGoal = AIGoals.Socialize;
            goalA.ReconsiderAccumulator = 999f;
            server.EntMan.GetComponent<GoalComponent>(b).ReconsiderAccumulator = 999f;

            server.EntMan.GetComponent<PerceptionComponent>(a).PerceiveAccumulator = 0f;
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var listenerMemory = server.EntMan.GetComponent<MemoryComponent>(b);
            Assert.That(listenerMemory.Memories.Any(m => m.Source == "rumor" && m.Participants.Contains(c)), Is.True,
                "B should have received a secondhand memory about C from A.");

            var listenerRelationships = server.EntMan.GetComponent<RelationshipComponent>(b);
            Assert.That(listenerRelationships.Relationships.TryGetValue(c, out var data), Is.True);
            Assert.That(data!.Fear, Is.GreaterThan(0f), "Hearing a rumor should still nudge feelings toward who it's about.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
            server.EntMan.DeleteEntity(c);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
