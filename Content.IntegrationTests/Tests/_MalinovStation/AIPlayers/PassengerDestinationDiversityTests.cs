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
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.6.1, spec sections 6/22 (destination selection + diversity): the place names the Cognitive
/// prompt actually offers used to come from <see cref="MemorySystem.GetKnownLocationNames"/>, which orders by
/// <see cref="AiMemory.Importance"/> then <see cref="AiMemory.Timestamp"/> - but
/// <see cref="LandmarkPerceptionSystem.SeedKnownBeacons"/> writes every station beacon with the exact same
/// importance at spawn, so that ordering is a tie broken by stable enumeration order in practice, and every AI
/// player saw the same fixed handful of names forever regardless of what it had actually visited. The
/// never-visited places most worth exploring were structurally invisible unless they happened to land in that
/// fixed set. <see cref="MemorySystem.GetExplorationCandidates"/> ranks by real familiarity instead; these
/// tests prove that ranking and its recent-visit penalty.
/// </summary>
[TestFixture]
public sealed class PassengerDestinationDiversityTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string NeverVisited = "Ангар";
    private const string Familiar = "Столовая";
    private const string JustLeft = "Мостик";

    private const string Map = "PassengerDestinationDiversityTestMap";

    [TestPrototypes]
    private static readonly string PassengerDestinationDiversityTestMap = @$"
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
    /// Seeds three places exactly the way <see cref="LandmarkPerceptionSystem.SeedKnownBeacons"/> really does -
    /// identical importance, identical source, back to back - so the only thing that can separate them is the
    /// familiarity ranking under test, not incidental importance/timestamp ordering.
    /// </summary>
    private void SeedIdenticallyScoredPlaces(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        var memory = server.System<MemorySystem>();
        var coords = server.EntMan.GetComponent<TransformComponent>(uid).Coordinates;

        foreach (var name in new[] { Familiar, JustLeft, NeverVisited })
        {
            memory.AddMemory(uid,
                content: $"Ты уже знаешь дорогу к «{name}».", importance: 0.25f, source: "landmark",
                location: coords, subject: name);
        }
    }

    [Test]
    public async Task NeverVisitedPlace_OutranksAFamiliarOne_DespiteIdenticalMemoryImportance()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            SeedIdenticallyScoredPlaces(pair, aiPlayer);

            // Thoroughly familiar - established directly rather than via real visits, since this test is about
            // the ranking, not the visit mechanic itself (PassengerLocationMemoryTests covers that).
            var knowledge = server.EntMan.GetComponent<LocationKnowledgeComponent>(aiPlayer);
            knowledge.Places[Familiar] = new LocationKnowledge { VisitCount = 6, Familiarity = 0.9f };
        });

        await server.WaitAssertion(() =>
        {
            var candidates = server.System<MemorySystem>().GetExplorationCandidates(aiPlayer);

            Assert.Multiple(() =>
            {
                Assert.That(candidates, Does.Contain(NeverVisited).And.Contain(Familiar),
                    "Both places are known by name, so both should still be offered as candidates.");
                Assert.That(candidates.ToList().IndexOf(NeverVisited), Is.LessThan(candidates.ToList().IndexOf(Familiar)),
                    "The never-visited place should outrank the thoroughly familiar one - identical memory importance must not decide this.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task JustVisitedPlace_IsDownrankedBelowEverythingElse()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            SeedIdenticallyScoredPlaces(pair, aiPlayer);

            var timing = server.ResolveDependency<IGameTiming>();
            var knowledge = server.EntMan.GetComponent<LocationKnowledgeComponent>(aiPlayer);

            // Barely known at all (so familiarity alone would rank it high), but just left a moment ago.
            knowledge.Places[JustLeft] = new LocationKnowledge
            {
                VisitCount = 1,
                Familiarity = 0.15f,
                LastVisitedAt = timing.CurTime,
            };
        });

        await server.WaitAssertion(() =>
        {
            var candidates = server.System<MemorySystem>().GetExplorationCandidates(aiPlayer).ToList();

            Assert.Multiple(() =>
            {
                Assert.That(candidates.IndexOf(JustLeft), Is.GreaterThan(candidates.IndexOf(NeverVisited)),
                    "A place just visited should rank below a never-visited one even though its familiarity is still low.");
                Assert.That(candidates.IndexOf(JustLeft), Is.GreaterThan(candidates.IndexOf(Familiar)),
                    "The recent-visit penalty should outweigh the familiarity difference - don't keep re-suggesting the room just left.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The regression this whole fix exists for: with every place seeded at identical importance (real
    /// <see cref="LandmarkPerceptionSystem.SeedKnownBeacons"/> behaviour), the prompt's own candidate list must
    /// actually reorder as the AI's familiarity changes, rather than staying frozen.
    /// </summary>
    [Test]
    public async Task PromptCandidates_Reorder_AsFamiliarityChanges()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            SeedIdenticallyScoredPlaces(pair, aiPlayer);
        });

        string firstBefore = default!;
        await server.WaitAssertion(() =>
        {
            var candidates = server.System<MemorySystem>().GetExplorationCandidates(aiPlayer).ToList();
            Assert.That(candidates, Is.Not.Empty, "Test setup: seeded places should be offered as candidates.");
            firstBefore = candidates[0];
        });

        // Become thoroughly familiar with whatever currently ranks first.
        await server.WaitPost(() =>
        {
            var knowledge = server.EntMan.GetComponent<LocationKnowledgeComponent>(aiPlayer);
            knowledge.Places[firstBefore] = new LocationKnowledge { VisitCount = 8, Familiarity = 1f };
        });

        await server.WaitAssertion(() =>
        {
            var candidates = server.System<MemorySystem>().GetExplorationCandidates(aiPlayer).ToList();
            Assert.That(candidates[0], Is.Not.EqualTo(firstBefore),
                "Once a place becomes thoroughly familiar it should stop leading the candidate list - the old ordering was frozen regardless of experience.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
