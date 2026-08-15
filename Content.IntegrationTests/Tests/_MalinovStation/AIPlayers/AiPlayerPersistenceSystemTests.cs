#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.Database;
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
/// Milestone 9 acceptance checks: an AI player spawned with a PersistentId saves its personality/memories
/// when it's deleted and reloads them the next time the same PersistentId is used - but only when the
/// persistence CVar is on, and never at all without a PersistentId (matching every milestone before this one).
/// </summary>
[TestFixture]
public sealed class AiPlayerPersistenceSystemTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "AIPlayerPersistenceTestMap";

    [TestPrototypes]
    private static readonly string AIPlayerPersistenceTestMap = @$"
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

    private static async Task WaitForCondition(TestPair pair, Func<bool> condition, int maxIterations = 60)
    {
        for (var i = 0; i < maxIterations && !condition(); i++)
        {
            await Task.Delay(100);
            await pair.RunTicksSync(1);
        }
    }

    /// <summary>
    /// With persistence enabled: a distinctive personality and an important memory survive the AI player
    /// being deleted and a new one being spawned under the same PersistentId, instead of getting a fresh
    /// random roll.
    /// </summary>
    [Test]
    public async Task SaveThenLoad_RestoresPersonalityAndMemories_UnderSamePersistentId()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        const string persistentId = "TestCharacterFrank";

        server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersPersistenceEnabled, true);

        EntityUid first = default;

        await server.WaitPost(() =>
        {
            first = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, persistentId: persistentId)!.Value;

            // Force a distinctive, unmistakable personality so a fresh random roll could never match it by chance.
            var personality = server.EntMan.GetComponent<PersonalityComponent>(first);
            personality.Sociability = 0.91f;
            personality.Courage = 0.92f;
            personality.Curiosity = 0.93f;
            personality.Laziness = 0.94f;
            personality.Greed = 0.95f;
            personality.Aggression = 0.96f;
            personality.Loyalty = 0.97f;
            personality.RiskTolerance = 0.98f;
            personality.AuthorityRespect = 0.99f;
            personality.Professionalism = 0.11f;
            personality.Empathy = 0.12f;
            personality.Honesty = 0.13f;
            personality.Impulsiveness = 0.14f;

            server.System<MemorySystem>().AddMemory(
                first,
                content: "Saved a coworker from a hull breach!",
                importance: 0.9f,
                source: "danger",
                emotionalWeight: 0.7f);
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(first));

        var persistence = server.System<AiPlayerPersistenceSystem>();
        await WaitForCondition(pair, () => !persistence.HasPendingSave(persistentId));
        await server.WaitAssertion(() => Assert.That(persistence.HasPendingSave(persistentId), Is.False, "Save should have completed."));

        EntityUid second = default;
        await server.WaitPost(() =>
        {
            second = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, persistentId: persistentId)!.Value;
        });

        await WaitForCondition(pair, () => !persistence.HasPendingLoad(second));

        await server.WaitAssertion(() =>
        {
            Assert.That(persistence.HasPendingLoad(second), Is.False, "Load should have completed.");

            var personality = server.EntMan.GetComponent<PersonalityComponent>(second);
            Assert.Multiple(() =>
            {
                Assert.That(personality.Sociability, Is.EqualTo(0.91f));
                Assert.That(personality.Courage, Is.EqualTo(0.92f));
                Assert.That(personality.Curiosity, Is.EqualTo(0.93f));
                Assert.That(personality.Laziness, Is.EqualTo(0.94f));
                Assert.That(personality.Greed, Is.EqualTo(0.95f));
                Assert.That(personality.Aggression, Is.EqualTo(0.96f));
                Assert.That(personality.Loyalty, Is.EqualTo(0.97f));
                Assert.That(personality.RiskTolerance, Is.EqualTo(0.98f));
                Assert.That(personality.AuthorityRespect, Is.EqualTo(0.99f));
                Assert.That(personality.Professionalism, Is.EqualTo(0.11f));
                Assert.That(personality.Empathy, Is.EqualTo(0.12f));
                Assert.That(personality.Honesty, Is.EqualTo(0.13f));
                Assert.That(personality.Impulsiveness, Is.EqualTo(0.14f));
            });

            var memory = server.EntMan.GetComponent<MemoryComponent>(second);
            Assert.That(memory.Memories.Any(m => m.Content == "Saved a coworker from a hull breach!"), Is.True);
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(MalinovAiPlayerCVars.AiPlayersPersistenceEnabled, false);
            if (server.EntMan.EntityExists(second))
                server.EntMan.DeleteEntity(second);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// With the persistence CVar off (the default), an AI player spawned with a PersistentId still never
    /// touches the database on deletion - persistence must be fully opt-in, not just "opt-in via PersistentId".
    /// </summary>
    [Test]
    public async Task PersistenceDisabled_NeverSaves_EvenWithPersistentId()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);
        const string persistentId = "TestCharacterNeverSaved";

        Assert.That(server.CfgMan.GetCVar(MalinovAiPlayerCVars.AiPlayersPersistenceEnabled), Is.False);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, persistentId: persistentId)!.Value;
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var persistence = server.System<AiPlayerPersistenceSystem>();
            Assert.That(persistence.HasPendingSave(persistentId), Is.False);
        });

        var db = server.ResolveDependency<IServerDbManager>();
        var saved = await db.GetAiPlayerDataAsync(persistentId);
        Assert.That(saved, Is.Null, "Nothing should have been saved while persistence is disabled.");

        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
