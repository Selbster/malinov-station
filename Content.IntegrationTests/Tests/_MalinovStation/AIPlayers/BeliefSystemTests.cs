#nullable enable
using System.Linq;
using System.Numerics;
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
/// AI Players 2.0 Milestone 1 spec section 31: "a false rumor changes the AI's belief, not objective world
/// state." Reuses the exact same rumor-sharing scenario
/// <see cref="SocialSystemTests.OpeningLine_SharesImportantUnsharedMemory_AsRumor_WithListener"/> already
/// proves for a legacy listener, but for a cognitive-mode one - the content is copied unvalidated either way
/// (nothing here checks truth), the difference is entirely in *where* it lands and how strongly it's trusted.
/// </summary>
[TestFixture]
public sealed class BeliefSystemTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "BeliefSystemTestMap";

    [TestPrototypes]
    private static readonly string BeliefSystemTestMap = @$"
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

    /// <summary>
    /// Sets up the exact rumor-sharing scenario: A has an important firsthand "danger" memory about C, wants
    /// to socialize with co-located B, and C is moved far out of vision range so B can't learn about C by
    /// directly perceiving it - the only way B can learn about C is via A's rumor.
    /// </summary>
    private async Task<(EntityUid A, EntityUid B, EntityUid C)> SetUpRumorScenario(TestPair pair, EntityUid station, bool listenerCognitive)
    {
        var server = pair.Server;
        EntityUid a = default;
        EntityUid b = default;
        EntityUid c = default;

        await server.WaitPost(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            a = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;
            b = aiPlayers.SpawnAiPlayer(Passenger, station, cognitiveMode: listenerCognitive)!.Value;
            c = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;

            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            var aCoords = server.EntMan.GetComponent<TransformComponent>(a).Coordinates;
            xformSystem.SetCoordinates(b, aCoords);
            xformSystem.SetCoordinates(c, aCoords.Offset(new Vector2(500, 500)));

            server.System<MemorySystem>().AddMemory(
                a,
                content: "Was attacked by C!",
                importance: 0.7f,
                source: "danger",
                participants: new[] { c },
                emotionalWeight: -0.8f);

            var goalA = server.EntMan.GetComponent<GoalComponent>(a);
            goalA.CurrentGoal = AIGoals.Socialize;
            goalA.ReconsiderAccumulator = 999f;
            server.EntMan.GetComponent<GoalComponent>(b).ReconsiderAccumulator = 999f;

            server.EntMan.GetComponent<PerceptionComponent>(a).PerceiveAccumulator = 0f;
        });

        await pair.RunTicksSync(5);
        return (a, b, c);
    }

    [Test]
    public async Task CognitiveListener_RumorLandsAsBelief_NotMemory()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (a, b, c) = await SetUpRumorScenario(pair, station, listenerCognitive: true);

        await server.WaitAssertion(() =>
        {
            var belief = server.EntMan.GetComponent<BeliefComponent>(b);
            Assert.That(belief.Beliefs.Any(x => x.Source == "rumor" && x.Participants.Contains(c) && x.Confidence < 1f), Is.True,
                "A cognitive listener should have received the rumor as a hedged Belief, not a certain fact.");

            var memory = server.EntMan.GetComponent<MemoryComponent>(b);
            Assert.That(memory.Memories.Any(m => m.Source == "rumor"), Is.False,
                "A cognitive listener's Memory (treated as ground truth by every existing reader) should not gain a rumor-sourced entry - that's what Belief exists to avoid.");

            var relationships = server.EntMan.GetComponent<RelationshipComponent>(b);
            Assert.That(relationships.Relationships.TryGetValue(c, out var data), Is.True);
            Assert.That(data!.Fear, Is.GreaterThan(0f), "Hearing a rumor should still nudge feelings toward who it's about, same as the legacy path.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(a);
            server.EntMan.DeleteEntity(b);
            server.EntMan.DeleteEntity(c);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Companion regression: a non-cognitive listener must behave exactly as before this milestone - the
    /// rumor still lands as a verbatim Memory entry, and never touches BeliefComponent (which it doesn't even
    /// have).
    /// </summary>
    [Test]
    public async Task LegacyListener_RumorStillLandsAsMemory_Unchanged()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        var (a, b, c) = await SetUpRumorScenario(pair, station, listenerCognitive: false);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.HasComponent<BeliefComponent>(b), Is.False,
                "Test setup: listener should be a legacy (non-cognitive) AI player.");

            var memory = server.EntMan.GetComponent<MemoryComponent>(b);
            Assert.That(memory.Memories.Any(m => m.Source == "rumor" && m.Participants.Contains(c)), Is.True,
                "A legacy listener should still receive the rumor as a Memory entry, exactly as before this milestone.");
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
