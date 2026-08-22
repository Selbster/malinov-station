#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Social architectural-preparation acceptance checks (spec section 15's first slice): a cognitive AI can
/// deliberately start a conversation with someone it can currently see via the "TalkTo" action, reusing the
/// exact same <see cref="SocialSystem"/> pipeline its own reactive small-talk trigger already uses (line
/// generation, localized fallback, memory/relationship write-back). The test-fixture LLM stays disabled (no
/// test in this tree ever sets ai_players.llm.enabled), so every conversation here resolves synchronously via
/// SocialSystem's localized-dataset fallback line - no real LLM/async plumbing needed.
/// </summary>
[TestFixture]
public sealed class AiSocialTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "AiSocialTestMap";

    [TestPrototypes]
    private static readonly string AiSocialTestMap = @$"
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

    /// <summary>Forces PerceptionSystem to scan on the very next tick, then gives it a couple of ticks to
    /// actually run - same shape as every other slice's ForceScan helper.</summary>
    private async Task ForceScan(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        await server.WaitPost(() => server.EntMan.GetComponent<PerceptionComponent>(uid).PerceiveAccumulator = 0f);
        await pair.RunTicksSync(2);
    }

    /// <summary>Mirrors every other slice's ClearOtherMobsNearby - the shared spawn area on this test map
    /// isn't actually empty (ambient wildlife etc. can land nearby), and a mob's own collision fixture counts
    /// as an LOS obstruction the same way a wall does.</summary>
    private async Task ClearOtherMobsNearby(TestPair pair, IReadOnlyCollection<EntityUid> keep, EntityCoordinates center, float radius)
    {
        var server = pair.Server;
        await server.WaitPost(() =>
        {
            var lookup = server.EntMan.System<EntityLookupSystem>();
            var nearby = new HashSet<Entity<Content.Shared.Mobs.Components.MobStateComponent>>();
            lookup.GetEntitiesInRange(center, radius, nearby);
            foreach (var mob in nearby)
            {
                if (!keep.Contains(mob.Owner))
                    server.EntMan.DeleteEntity(mob.Owner);
            }
        });
    }

    private async Task<(EntityUid A, EntityUid B)> SpawnTwoNearbyAiPlayers(TestPair pair, EntityUid station, bool cognitiveMode = true)
    {
        var server = pair.Server;
        EntityUid a = default, b = default;

        await server.WaitPost(() =>
        {
            var players = server.System<AIPlayerSystem>();
            a = players.SpawnAiPlayer(Passenger, station, cognitiveMode: cognitiveMode)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(a).Coordinates;
            b = players.SpawnAiPlayer(Passenger, station, cognitiveMode: cognitiveMode)!.Value;
            server.EntMan.GetComponent<TransformComponent>(b).Coordinates = coords.Offset(new System.Numerics.Vector2(1, 0));
        });

        await ClearOtherMobsNearby(pair, new[] { a, b }, server.EntMan.GetComponent<TransformComponent>(a).Coordinates, 10f);

        await ForceScan(pair, a);
        await ForceScan(pair, b);

        return (a, b);
    }

    private string NameOf(TestPair pair, EntityUid uid) =>
        pair.Server.EntMan.GetComponent<MetaDataComponent>(uid).EntityName;

    /// <summary>
    /// With the test-fixture LLM disabled, SocialSystem's fallback resolves synchronously all the way through
    /// the bounded exchange (opener AND reply) within this single TryDoAction call - so the real causal proof
    /// isn't a mid-flight state (already resolved by the time control returns), it's that BOTH participants
    /// actually completed their turn: each is back to ConversationState.None with a fresh cooldown, and each
    /// has its own "conversation" memory of the exchange (RecordExchange writes only the current speaker's
    /// side, and each side is "speaker" exactly once in a two-line exchange).
    /// </summary>
    [Test]
    public async Task TalkToAction_KnownNearbyIdlePartner_SucceedsAndCompletesRealConversation()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (a, b) = await SpawnTwoNearbyAiPlayers(pair, station);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(a, TalkToAction.ActionName, new TalkToActionParams(NameOf(pair, b), "I wanted to check in"), out var reason);
            Assert.That(ok, Is.True, reason);

            var convA = server.EntMan.GetComponent<ConversationComponent>(a);
            var convB = server.EntMan.GetComponent<ConversationComponent>(b);
            Assert.Multiple(() =>
            {
                Assert.That(convA.State, Is.EqualTo(ConversationState.None), "Initiator's turn should be over.");
                Assert.That(convB.State, Is.EqualTo(ConversationState.None), "Partner's reply turn should also be over.");
                Assert.That(convA.CooldownUntil, Is.GreaterThan(server.Timing.CurTime), "Initiator should be on post-conversation cooldown.");
                Assert.That(convB.CooldownUntil, Is.GreaterThan(server.Timing.CurTime), "Partner should be on post-conversation cooldown too - proves its reply actually ran.");
                Assert.That(server.EntMan.GetComponent<MemoryComponent>(a).Memories.Any(m => m.Source == "conversation"), Is.True);
                Assert.That(server.EntMan.GetComponent<MemoryComponent>(b).Memories.Any(m => m.Source == "conversation"), Is.True);
            });
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task TalkToAction_InitiatorAlreadyInConversation_FailsCleanly()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (a, b) = await SpawnTwoNearbyAiPlayers(pair, station);

        await server.WaitPost(() =>
        {
            server.EntMan.GetComponent<ConversationComponent>(a).State = ConversationState.AwaitingReplyLine;
            server.EntMan.GetComponent<ConversationComponent>(a).Partner = b;
        });

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(a, TalkToAction.ActionName, new TalkToActionParams(NameOf(pair, b), "hi"), out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("already in the middle"));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task TalkToAction_TargetNotVisible_FailsCleanly()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid a = default, b = default;

        await server.WaitPost(() =>
        {
            var players = server.System<AIPlayerSystem>();
            a = players.SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var coords = server.EntMan.GetComponent<TransformComponent>(a).Coordinates;
            b = players.SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            server.EntMan.GetComponent<TransformComponent>(b).Coordinates = coords.Offset(new System.Numerics.Vector2(500, 500));
        });
        await ForceScan(pair, a);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(a, TalkToAction.ActionName, new TalkToActionParams(NameOf(pair, b), "hi"), out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("don't see anyone nearby"));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task TalkToAction_TargetAlreadyBusy_FailsCleanly()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (a, b) = await SpawnTwoNearbyAiPlayers(pair, station);

        await server.WaitPost(() =>
        {
            // Spawned far away - it only needs to exist as a valid EntityUid to reserve b.Partner, not be
            // perceived. Left near a/b it would spawn on the same default station spawn point and its own
            // collision fixture could obstruct the a<->b line-of-sight check, same interference every other
            // slice's tests guard against via ClearOtherMobsNearby.
            var third = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var farAway = server.EntMan.GetComponent<TransformComponent>(a).Coordinates.Offset(new System.Numerics.Vector2(500, 500));
            server.EntMan.GetComponent<TransformComponent>(third).Coordinates = farAway;
            server.EntMan.GetComponent<ConversationComponent>(b).Partner = third;
        });

        // Re-confirm freshly, rather than trust the earlier ForceScan is still current after the extra spawn.
        await ForceScan(pair, a);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(a, TalkToAction.ActionName, new TalkToActionParams(NameOf(pair, b), "hi"), out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("busy right now"));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task TalkToAction_TargetWithoutConversationComponent_FailsCleanly()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (a, b) = await SpawnTwoNearbyAiPlayers(pair, station);

        // Simulate a non-AI-player character (e.g. a real human player) - has everything PerceptionSystem
        // needs to notice it, just no conversation capability.
        await server.WaitPost(() => server.EntMan.RemoveComponent<ConversationComponent>(b));
        await ForceScan(pair, a);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(a, TalkToAction.ActionName, new TalkToActionParams(NameOf(pair, b), "hi"), out var reason);

            Assert.That(ok, Is.False);
            Assert.That(reason, Does.Contain("isn't someone I can talk to"));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Proves the ContextBuilderSystem/DialogueContext threading itself (design item 3) directly - not
    /// through TalkToAction/SocialSystem, since with the test-fixture LLM disabled the whole exchange
    /// (including ConversationComponent.Reason getting consumed and reset by EndConversation) resolves
    /// synchronously within a single TryDoAction call, leaving no window to observe the field mid-flight.
    /// </summary>
    [Test]
    public async Task ContextBuilderSystem_BuildDialogueContext_ReasonForApproachingRoundTrips()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (a, b) = await SpawnTwoNearbyAiPlayers(pair, station);
        const string reason = "I wanted to ask what happened earlier";

        await server.WaitAssertion(() =>
        {
            var context = server.System<ContextBuilderSystem>().BuildDialogueContext(a, b, partnerJustSaid: null, reasonForApproaching: reason);
            Assert.That(context, Is.Not.Null);
            Assert.That(context!.ReasonForApproaching, Is.EqualTo(reason));
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task TalkToAction_LegacyAiPlayerPair_StillWorks()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (a, b) = await SpawnTwoNearbyAiPlayers(pair, station, cognitiveMode: false);

        await server.WaitAssertion(() =>
        {
            var actions = server.System<AiActionRegistrySystem>();
            var ok = actions.TryDoAction(a, TalkToAction.ActionName, new TalkToActionParams(NameOf(pair, b), "hi"), out var reason);
            Assert.That(ok, Is.True, reason);

            // Same synchronous-fallback reasoning as the cognitive-mode test above: by the time TryDoAction
            // returns, the whole bounded exchange already completed, so the causal proof is the memory each
            // side is left with, not a mid-flight ConversationComponent state.
            Assert.That(server.EntMan.GetComponent<MemoryComponent>(a).Memories.Any(m => m.Source == "conversation"), Is.True);
            Assert.That(server.EntMan.GetComponent<MemoryComponent>(b).Memories.Any(m => m.Source == "conversation"), Is.True);
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// End-to-end wiring proof: a hand-built cognitive decision proposing TalkTo for a real nearby AI player
    /// actually starts the conversation, mirroring the previous three slices' TryApplyCognitiveDecision_...
    /// style (no real LLM).
    /// </summary>
    [Test]
    public async Task TryApplyCognitiveDecision_TalkTo_ForANearbyPlayer_StartsConversation()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var (a, b) = await SpawnTwoNearbyAiPlayers(pair, station);

        await server.WaitAssertion(() =>
        {
            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "curiosity", "meet_person", 0.6f, 0.8f, "I want to ask them what happened",
                TalkToAction.ActionName, new Dictionary<string, string> { ["target"] = NameOf(pair, b) });
            Assert.That(gateway.TryApplyCognitiveDecision(a, decision), Is.True);

            var intent = server.EntMan.GetComponent<IntentComponent>(a);
            Assert.That(intent.Name, Is.EqualTo("meet_person"));

            // Same synchronous-fallback reasoning: the exchange already completed by the time this returns.
            Assert.That(server.EntMan.GetComponent<MemoryComponent>(b).Memories.Any(m => m.Source == "conversation"), Is.True);
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
