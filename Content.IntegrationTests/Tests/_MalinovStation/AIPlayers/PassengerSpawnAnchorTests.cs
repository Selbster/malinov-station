#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.LLM;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Server.NPC.HTN;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 0.6, spec sections 9-10/30: the forced-teleport test. Audit found no code anywhere that stores
/// or consumes a spawn coordinate as an automatic return target - the actual mechanism behind "passenger drifts
/// back toward where it was headed after being moved" is a stale <c>GoToKnownLocation</c> commitment (the HTN
/// blackboard's <see cref="MoveToAction.ForcedDestinationKey"/>) that nothing previously invalidated after an
/// out-of-band relocation. <see cref="AiBusyStateSystem"/>'s relocation detection is the fix; this proves it
/// directly - the stale commitment is aborted (not just eventually replaced), and a fresh decision afterward is
/// free to pick anywhere, including a place nowhere near the original spawn/destination, with no special-casing.
/// </summary>
[TestFixture]
public sealed class PassengerSpawnAnchorTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string Map = "PassengerSpawnAnchorTestMap";

    [TestPrototypes]
    private static readonly string PassengerSpawnAnchorTestMap = @$"
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
    public async Task ForcedRelocation_AbortsStaleCommitment_InsteadOfResumingTowardIt()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var spawnCoords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;

            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Room A\".", importance: 0.25f, source: "landmark",
                location: spawnCoords.Offset(new Vector2(5, 0)), subject: "Room A");

            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "curiosity", "исследовать_a", 0.6f, 0.7f, "Хочу посмотреть комнату A.",
                GoToKnownLocationAction.ActionName, new Dictionary<string, string> { ["location"] = "Room A" });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, decision), Is.True);

            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.EqualTo(GoToKnownLocationAction.ActionName),
                "Test setup: the travel decision should have set a busy commitment toward Room A.");

            server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator = 999f;

            // Forced, out-of-band relocation - an admin teleport, not the AI's own steering.
            var farAway = spawnCoords.Offset(new Vector2(500, -500));
            server.System<SharedTransformSystem>().SetCoordinates(aiPlayer, farAway);
        });

        // AiBusyStateSystem's shared scan accumulator needs up to a full ~1s cycle to notice - same margin
        // AiBusyStateTests/CognitiveRuntimeIntegrationTests already use for the danger-interruption case.
        await pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.Multiple(() =>
            {
                Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.Null,
                    "The stale commitment should have been cleared after the relocation, not left to keep executing.");
                Assert.That(
                    server.EntMan.GetComponent<HTNComponent>(aiPlayer).Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out _, server.EntMan),
                    Is.False,
                    "Room A's coordinates should no longer be the HTN's forced destination - the AI must not keep walking toward it.");
                // Not a tight "<= 45f" bound like EmergencyInterruptionLoop's own version of this check: that
                // test's AI stays near the connected client (LOD stays Full, so the reflection cooldown
                // LlmGatewaySystem.UpdateReflectionTriggers recycles the force-reset accumulator to is the base
                // 45s). This test's AI is deliberately teleported far away, so by the time this runs it's
                // legitimately Background-tier LOD, and that same recycle lands around 45s * AiLodComponent's
                // 15x BackgroundMultiplier (~675) instead - still unambiguously evidence of a fresh reset (vs.
                // the ~999 - elapsed it would still read at if nothing had force-reset it at all).
                Assert.That(server.EntMan.GetComponent<CognitiveModeComponent>(aiPlayer).ReflectionAccumulator, Is.LessThan(900f),
                    "The relocation should force an immediate fresh cognitive reflection, same as any other meaningful interruption.");
            });
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }

    [Test]
    public async Task FreshDecisionAfterRelocation_CanChooseAnyKnownPlace_NotConstrainedToNearOriginalSpawn()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid aiPlayer = default;
        EntityCoordinates roomB = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            var spawnCoords = server.EntMan.GetComponent<TransformComponent>(aiPlayer).Coordinates;

            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Room A\".", importance: 0.25f, source: "landmark",
                location: spawnCoords.Offset(new Vector2(5, 0)), subject: "Room A");

            var gateway = server.System<LlmGatewaySystem>();
            var toA = new LlmCognitiveDecision(
                "curiosity", "исследовать_a", 0.6f, 0.7f, "Хочу посмотреть комнату A.",
                GoToKnownLocationAction.ActionName, new Dictionary<string, string> { ["location"] = "Room A" });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, toA), Is.True);

            // Room B is seeded near where the AI is about to be relocated to - deliberately nowhere near the
            // original spawn/Room A, proving the follow-up decision isn't somehow constrained toward it.
            var farAway = spawnCoords.Offset(new Vector2(500, -500));
            roomB = farAway.Offset(new Vector2(4, 0));
            server.System<MemorySystem>().AddMemory(aiPlayer,
                content: "There's a place nearby called \"Room B\".", importance: 0.25f, source: "landmark",
                location: roomB, subject: "Room B");

            server.System<SharedTransformSystem>().SetCoordinates(aiPlayer, farAway);
        });

        await pair.RunTicksSync(40);

        await server.WaitAssertion(() =>
        {
            Assert.That(server.EntMan.GetComponent<AiBusyStateComponent>(aiPlayer).CurrentAction, Is.Null,
                "Test setup: the relocation should have cleared the stale commitment to Room A first.");

            var gateway = server.System<LlmGatewaySystem>();
            var toB = new LlmCognitiveDecision(
                "curiosity", "исследовать_b", 0.6f, 0.7f, "Раз уж я тут, посмотрю комнату B.",
                GoToKnownLocationAction.ActionName, new Dictionary<string, string> { ["location"] = "Room B" });
            Assert.That(gateway.TryApplyCognitiveDecision(aiPlayer, toB), Is.True,
                "A fresh decision should be free to pick any known place, not just one near the original spawn.");

            var htn = server.EntMan.GetComponent<HTNComponent>(aiPlayer);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out var stored, server.EntMan), Is.True);
            Assert.That(stored, Is.EqualTo(roomB));
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
