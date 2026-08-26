#nullable enable
using System.Linq;
using System.Numerics;
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
/// AI Players 0.6.1: <see cref="AiLodSystem.Update"/> used to recompute <see cref="AiLodComponent.Level"/>
/// on its own cadence without ever touching <see cref="CognitiveModeComponent.ReflectionAccumulator"/> -
/// an AI player idle at Background tier could have that accumulator seeded as high as
/// <c>ReflectionCooldown * BackgroundMultiplier</c> (up to ~11 real minutes), and nothing rescaled it down
/// when a player then got close enough to actually notice the AI, so it could keep sitting frozen for a long
/// time even while directly observed - exactly the "state visibly freezes and jumps" bug
/// <see cref="AiLodComponent"/>'s own doc comment says this whole subsystem is supposed to avoid. These
/// tests prove the fix: a tier *improvement* clamps the wait down to the new tier's own cooldown, a tier
/// *downgrade* leaves it untouched.
/// </summary>
[TestFixture]
public sealed class PassengerReflectionLodTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";

    private const string Map = "PassengerReflectionLodTestMap";

    [TestPrototypes]
    private static readonly string PassengerReflectionLodTestMap = @$"
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
    public async Task TierImprovement_ClampsInflatedReflectionAccumulator()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var lod = server.EntMan.GetComponent<AiLodComponent>(aiPlayer);
            lod.Level = AiLevelOfDetail.Background;

            // As if this had just been rescaled a moment ago while genuinely at Background tier.
            var cognitive = server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer);
            cognitive.ReflectionAccumulator = cognitive.ReflectionCooldown * lod.BackgroundMultiplier;

            // Move right on top of the real connected player and force an immediate reassessment.
            var realPlayer = server.PlayerMan.Sessions.First().AttachedEntity!.Value;
            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            xformSystem.SetCoordinates(aiPlayer, server.EntMan.GetComponent<TransformComponent>(realPlayer).Coordinates);
            lod.ReassessAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var lod = server.EntMan.GetComponent<AiLodComponent>(aiPlayer);
            var cognitive = server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(lod.Level, Is.EqualTo(AiLevelOfDetail.Full), "Test setup: should have reclassified to Full.");
                Assert.That(cognitive.ReflectionAccumulator, Is.LessThanOrEqualTo(cognitive.ReflectionCooldown),
                    "A tier improvement should rescue a reflection accumulator stuck counting down a wait sized for the old, less attentive tier.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task TierDowngrade_DoesNotRescaleReflectionAccumulator()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        var accumulatorBefore = 0f;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;

            var lod = server.EntMan.GetComponent<AiLodComponent>(aiPlayer);
            lod.Level = AiLevelOfDetail.Full;

            var cognitive = server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer);
            cognitive.ReflectionAccumulator = 10f;
            accumulatorBefore = cognitive.ReflectionAccumulator;

            // Move far from every connected player and force an immediate reassessment.
            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            var coords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;
            xformSystem.SetCoordinates(aiPlayer, coords.Offset(new Vector2(500, 500)));
            lod.ReassessAccumulator = 0f;
        });

        await pair.RunTicksSync(2);

        await server.WaitAssertion(() =>
        {
            var lod = server.EntMan.GetComponent<AiLodComponent>(aiPlayer);
            var cognitive = server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer);
            Assert.Multiple(() =>
            {
                Assert.That(lod.Level, Is.EqualTo(AiLevelOfDetail.Background), "Test setup: should have reclassified to Background.");
                Assert.That(cognitive.ReflectionAccumulator,
                    Is.LessThanOrEqualTo(accumulatorBefore).And.GreaterThan(accumulatorBefore - 1f),
                    "A tier downgrade should not rescale the reflection accumulator - only the ordinary per-tick decrement should apply.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
