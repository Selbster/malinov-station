#nullable enable
using System.Collections.Generic;
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
/// AI Players 0.6.3, spec section 20: several passengers should not all march to the same place, and the
/// difference has to come from who they are and where they have been - never from a hardcoded assignment.
///
/// These drive <see cref="ExplorationControllerSystem"/> directly rather than through the LLM: the question is
/// whether the deterministic half of the decision genuinely differentiates between characters, which is what
/// makes varied behaviour possible at all. If it collapses to one answer here, no amount of model variety
/// upstream would help.
/// </summary>
[TestFixture]
public sealed class PassengerExplorationDiversityTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "PassengerExplorationDiversityTestMap";

    private static readonly string[] Places = { "Карго", "Медотсек", "Бар" };

    [TestPrototypes]
    private static readonly string PassengerExplorationDiversityTestMap = @$"
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

    /// <summary>Gives a passenger the same three known places as everyone else, then makes it thoroughly
    /// familiar with exactly one of them - the only thing distinguishing it from its neighbours.</summary>
    private static void KnowsEverywhereButIsSickOf(TestPair pair, EntityUid uid, string familiarPlace)
    {
        var server = pair.Server;
        var memory = server.System<MemorySystem>();
        var origin = server.EntMan.GetComponent<TransformComponent>(uid).Coordinates;

        server.EntMan.GetComponent<MemoryComponent>(uid).Memories.Clear();
        var knowledge = server.EntMan.GetComponent<LocationKnowledgeComponent>(uid);
        knowledge.Places.Clear();

        var offset = -4f;
        foreach (var place in Places)
        {
            memory.AddMemory(uid,
                content: $"Ты уже знаешь дорогу к «{place}».", importance: 0.25f, source: "landmark",
                location: origin.Offset(new Vector2(offset, 0)), subject: place);
            offset -= 4f;
        }

        knowledge.Places[familiarPlace] = new LocationKnowledge { VisitCount = 8, Familiarity = 1f };
    }

    [Test]
    public async Task ThreePassengersWithDifferentHistories_DoNotAllPickTheSamePlace()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        var passengers = new List<EntityUid>();
        var chosen = new List<string?>();

        await server.WaitPost(() =>
        {
            var spawner = server.System<AIPlayerSystem>();

            for (var i = 0; i < Places.Length; i++)
            {
                var uid = spawner.SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
                passengers.Add(uid);

                // Each is fed up with a different place, so "somewhere I am not sick of" points elsewhere for
                // each of them. Nothing assigns a destination - it falls out of their own histories.
                KnowsEverywhereButIsSickOf(pair, uid, Places[i]);
            }
        });

        await server.WaitAssertion(() =>
        {
            var controller = server.System<ExplorationControllerSystem>();

            foreach (var uid in passengers)
            {
                Assert.That(controller.TryGetTarget(uid, out var target), Is.True,
                    "Each passenger knows three places, so each must have somewhere worth going.");
                chosen.Add(target.PlaceName);
            }

            Assert.Multiple(() =>
            {
                Assert.That(chosen.Distinct().Count(), Is.GreaterThan(1),
                    "Passengers with different visit histories must not all converge on one destination (spec section 20).");

                for (var i = 0; i < passengers.Count; i++)
                {
                    Assert.That(chosen[i], Is.Not.EqualTo(Places[i]),
                        $"The place this passenger is thoroughly sick of ({Places[i]}) is the one it should least want to revisit.");
                }
            });
        });

        await server.WaitPost(() =>
        {
            foreach (var uid in passengers)
                server.EntMan.DeleteEntity(uid);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The other half of section 20's requirement: personality has to matter too. A barely-curious passenger
    /// should not weigh an unfamiliar place as heavily as an inquisitive one does - otherwise every character
    /// explores identically and the trait is decorative.
    /// </summary>
    [Test]
    public async Task CuriosityChangesHowStronglyAnUnfamiliarPlaceIsPreferred()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid incurious = default;
        EntityUid inquisitive = default;

        await server.WaitPost(() =>
        {
            var spawner = server.System<AIPlayerSystem>();

            incurious = spawner.SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            inquisitive = spawner.SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            foreach (var uid in new[] { incurious, inquisitive })
                KnowsEverywhereButIsSickOf(pair, uid, Places[0]);

            server.EntMan.GetComponent<PersonalityComponent>(incurious).Curiosity = 0.05f;
            server.EntMan.GetComponent<PersonalityComponent>(inquisitive).Curiosity = 0.95f;
        });

        await server.WaitAssertion(() =>
        {
            var controller = server.System<ExplorationControllerSystem>();

            Assert.That(controller.TryGetTarget(incurious, out var dullTarget), Is.True);
            Assert.That(controller.TryGetTarget(inquisitive, out var keenTarget), Is.True);

            Assert.That(keenTarget.Score, Is.GreaterThan(dullTarget.Score),
                "An unfamiliar place has to appeal more to a curious character than to an incurious one.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(incurious);
            server.EntMan.DeleteEntity(inquisitive);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
