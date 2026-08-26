#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
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
/// AI Players 0.6.2, spec sections 15/16: a failed exploration has to be *reported*, and it has to change what
/// happens next. Before this milestone an action returned <c>void</c>, so "I set off to explore and there was
/// nowhere to go" was indistinguishable from success - the cognitive layer learned nothing and could decide
/// exactly the same thing again immediately.
/// </summary>
[TestFixture]
public sealed class PassengerExploreFeedbackTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "PassengerExploreFeedbackTestMap";

    [TestPrototypes]
    private static readonly string PassengerExploreFeedbackTestMap = @$"
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
    /// Strands the passenger somewhere with nothing to explore: no remembered place names, and off the
    /// navigable mesh entirely, so no frontier point can be found either.
    /// </summary>
    private static void StrandWithNowhereToGo(TestPair pair, EntityUid uid)
    {
        var server = pair.Server;
        server.EntMan.GetComponent<MemoryComponent>(uid).Memories.Clear();
        server.EntMan.GetComponent<LocationKnowledgeComponent>(uid).Places.Clear();

        var xform = server.EntMan.GetComponent<TransformComponent>(uid);
        var transform = server.System<SharedTransformSystem>();
        transform.SetCoordinates(uid, new EntityCoordinates(xform.MapUid!.Value, new Vector2(600, 600)));
    }

    [Test]
    public async Task WithNowhereToGo_ExploreStation_ReportsNoTarget_RatherThanSilentlySucceeding()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            StrandWithNowhereToGo(pair, aiPlayer);
        });

        await server.WaitAssertion(() =>
        {
            var succeeded = server.System<AiActionRegistrySystem>().TryDoAction(aiPlayer,
                ExploreStationAction.ActionName, new ExploreStationActionParams(), out var failReason);

            Assert.Multiple(() =>
            {
                Assert.That(succeeded, Is.False,
                    "Finding nowhere to go is a real failure and must be reported as one (spec section 15).");
                Assert.That(failReason, Is.Not.Null.And.Not.Empty,
                    "The reason reaches the LLM verbatim, so it has to say something.");
                Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.Null,
                    "A failed exploration must not leave the passenger marked as busy travelling.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// Spec section 16: the failure has to change future behaviour. A cooldown makes the action stop being
    /// offered for a while, so the cognitive layer picks something genuinely different instead of re-deciding
    /// to explore on its very next reflection.
    /// </summary>
    [Test]
    public async Task AFruitlessAttempt_StopsExploreStationBeingOffered_SoItCannotLoop()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            StrandWithNowhereToGo(pair, aiPlayer);
        });

        await server.WaitAssertion(() =>
        {
            var registry = server.System<AiActionRegistrySystem>();

            registry.TryDoAction(aiPlayer, ExploreStationAction.ActionName, new ExploreStationActionParams(), out _);

            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.GetComponent<ExplorationComponent>(aiPlayer).NoTargetCooldownUntil,
                    Is.GreaterThan(TimeSpan.Zero), "A fruitless attempt should arm the cooldown.");
                Assert.That(registry.GetLlmSelectableActions(aiPlayer).Select(a => a.Name),
                    Does.Not.Contain(ExploreStationAction.ActionName),
                    "While cooling down, exploring must not be offered again - that is what breaks the loop (spec section 16).");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    /// <summary>
    /// The other half of section 16: the suppression is temporary. Once circumstances could plausibly have
    /// changed, the passenger is willing to consider exploring again rather than being stuck indoors forever.
    /// </summary>
    [Test]
    public async Task TheCooldownExpires_SoExplorationIsNotSuppressedPermanently()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            StrandWithNowhereToGo(pair, aiPlayer);
            server.System<AiActionRegistrySystem>().TryDoAction(aiPlayer,
                ExploreStationAction.ActionName, new ExploreStationActionParams(), out _);
        });

        await server.WaitPost(() =>
        {
            // Expire the cooldown rather than waiting out two real minutes of ticks, and restore somewhere
            // there is genuinely something to explore again.
            server.EntMan.GetComponent<ExplorationComponent>(aiPlayer).NoTargetCooldownUntil = TimeSpan.Zero;

            var origin = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "Ты уже знаешь дорогу к «Бар».", importance: 0.25f, source: "landmark",
                location: origin.Offset(new Vector2(-6, 0)), subject: "Бар");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(server.System<AiActionRegistrySystem>().GetLlmSelectableActions(aiPlayer).Select(a => a.Name),
                Does.Contain(ExploreStationAction.ActionName),
                "Once the cooldown lapses and there is somewhere to go, exploring is back on the table.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
