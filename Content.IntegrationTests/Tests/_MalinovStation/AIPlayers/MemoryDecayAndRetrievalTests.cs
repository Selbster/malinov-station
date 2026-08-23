#nullable enable
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared._MalinovStation.AIPlayers;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.4 Milestones 7-10: <see cref="MemorySystem"/> implements <see cref="IMemoryStore"/>/
/// <see cref="IMemoryRetriever"/> additively, importance decays at retrieval time only
/// (<see cref="MemorySystem.GetEffectiveImportance"/>, stored <c>Importance</c> is never mutated), and
/// <see cref="MemorySystem.Recall"/> ranks by relevance to a <see cref="RecallQuery"/> rather than only raw
/// importance. Every existing <see cref="MemorySystem"/> method/test stays unaffected - this file covers only
/// what's actually new.
/// </summary>
[TestFixture]
public sealed class MemoryDecayAndRetrievalTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "MemoryDecayAndRetrievalTestMap";

    [TestPrototypes]
    private static readonly string MemoryDecayAndRetrievalTestMap = @$"
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

    [Test]
    public async Task MemorySystem_ImplementsIMemoryStoreAndIMemoryRetriever_ThroughTheSamePublicSurface()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var memorySystem = server.System<MemorySystem>();
            IMemoryStore store = memorySystem;
            IMemoryRetriever retriever = memorySystem;

            var written = store.AddMemory(aiPlayer, content: "Test event.", importance: 0.5f, source: "outcome");
            Assert.That(written, Is.Not.Null, "IMemoryStore.AddMemory should behave exactly like the concrete AddMemory.");

            var recalled = retriever.Recall(aiPlayer, new RecallQuery(Keyword: "Test"));
            Assert.That(recalled.Select(m => m.Id), Does.Contain(written!.Id));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task GetEffectiveImportance_DecaysWithAge_ButNeverMutatesStoredImportance()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersMemoryDecayHalfLifeSeconds, 10f);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var memorySystem = server.System<MemorySystem>();
            var entry = memorySystem.AddMemory(aiPlayer, content: "Old event.", importance: 0.8f, source: "outcome")!;

            var now = server.Timing.CurTime;
            var freshImportance = memorySystem.GetEffectiveImportance(entry, now);
            Assert.That(freshImportance, Is.EqualTo(0.8f).Within(0.01f), "A brand-new memory shouldn't have decayed yet.");

            // Simulate the memory being 10s old (exactly one half-life) without waiting real time.
            entry.Timestamp = now - TimeSpan.FromSeconds(10);
            var decayedImportance = memorySystem.GetEffectiveImportance(entry, now);

            Assert.Multiple(() =>
            {
                Assert.That(decayedImportance, Is.EqualTo(0.4f).Within(0.02f), "One half-life should roughly halve the effective importance.");
                Assert.That(entry.Importance, Is.EqualTo(0.8f), "Decay must never mutate the stored Importance field.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task GetMostImportant_OrdersByEffectiveImportance_SoAnOldMemoryCanBeOutrankedByADecayedTie()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersMemoryDecayHalfLifeSeconds, 10f);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var memorySystem = server.System<MemorySystem>();
            var oldMemory = memorySystem.AddMemory(aiPlayer, content: "Old but equally important.", importance: 0.6f, source: "outcome")!;
            var freshMemory = memorySystem.AddMemory(aiPlayer, content: "Fresh and equally important.", importance: 0.6f, source: "outcome")!;

            // Same raw importance, but the old one is many half-lives stale - it should now rank behind the fresh one.
            oldMemory.Timestamp = server.Timing.CurTime - TimeSpan.FromSeconds(100);

            var ranked = memorySystem.GetMostImportant(aiPlayer, max: 2);
            Assert.That(ranked[0].Id, Is.EqualTo(freshMemory.Id),
                "The fresh memory should now outrank the equally-important-but-decayed old one.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task Recall_PrioritizesAQueryMatchOverHigherRawImportanceAlone()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;

        await server.WaitAssertion(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var memorySystem = server.System<MemorySystem>();
            var unrelatedButImportant = memorySystem.AddMemory(aiPlayer,
                content: "Something else entirely happened.", importance: 0.5f, source: "outcome")!;
            var relevantButLessImportant = memorySystem.AddMemory(aiPlayer,
                content: "Saw Sarah helping someone in Medbay.", importance: 0.3f, source: "perception", subject: "Sarah")!;

            var recalled = memorySystem.Recall(aiPlayer, new RecallQuery(Subject: "Sarah", Keyword: "Medbay"), max: 2);

            Assert.That(recalled[0].Id, Is.EqualTo(relevantButLessImportant.Id),
                "A memory matching the query's subject/keyword should outrank one that's merely more important but unrelated.");
            Assert.That(recalled.Select(m => m.Id), Does.Contain(unrelatedButImportant.Id),
                "Recall should still degrade gracefully to including non-matching memories, not exclude them entirely.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
