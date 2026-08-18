#nullable enable
using System.Linq;
using System.Numerics;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Pair;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// AI Players 2.0 Milestone 1 spec section 31: "the AI never magically knows about an event it didn't
/// perceive." <see cref="CognitiveState"/> deliberately has no "UnknownInformation" field (see its own doc
/// comment) - the boundary is proven here behaviourally instead: an event a cognitive AI player never
/// perceived must leave zero trace in its assembled CognitiveState, Memory, Belief or Emotion.
/// </summary>
[TestFixture]
public sealed class CognitiveStateTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private static readonly ProtoId<DamageTypePrototype> BluntDamageType = "Blunt";

    private const string Map = "CognitiveStateTestMap";

    [TestPrototypes]
    private static readonly string CognitiveStateTestMap = @$"
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
    /// An observer far from a victim who gets attacked - well outside vision range, so the observer never
    /// perceives it at all - must have zero knowledge of it: no VisibleWorld entry, no KnownFacts/Beliefs
    /// mention, no memory, and no emotional reaction (Fear stays 0), even though the exact same event would
    /// have produced all of those for a nearby witness.
    /// </summary>
    [Test]
    public async Task UnperceivedAttack_LeavesNoTraceInObserverCognitiveStateMemoryOrEmotion()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid observer = default;
        EntityUid victim = default;
        EntityUid attacker = default;

        await server.WaitPost(() =>
        {
            var aiPlayers = server.System<AIPlayerSystem>();
            observer = aiPlayers.SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            victim = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;
            attacker = aiPlayers.SpawnAiPlayer(Passenger, station)!.Value;

            var xformSystem = server.EntMan.System<SharedTransformSystem>();
            var observerCoords = server.EntMan.GetComponent<TransformComponent>(observer).Coordinates;

            // Well outside the default PerceptionComponent.VisionRadius (10) - the observer cannot perceive
            // this by any means.
            var farCoords = observerCoords.Offset(new Vector2(500, 500));
            xformSystem.SetCoordinates(victim, farCoords);
            xformSystem.SetCoordinates(attacker, farCoords);

            var damageable = server.System<DamageableSystem>();
            var damageType = server.ProtoMan.Index(BluntDamageType);
            var damage = new DamageSpecifier(damageType, FixedPoint2.New(10));
            damageable.TryChangeDamage(victim, damage, origin: attacker);
        });

        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(observer);
            Assert.That(state, Is.Not.Null);

            Assert.Multiple(() =>
            {
                Assert.That(state!.VisibleWorld.Any(c => c.Name == server.EntMan.GetComponent<MetaDataComponent>(victim).EntityName), Is.False,
                    "The observer should not see the victim at all.");
                Assert.That(state.KnownFacts.Any(f => f.Contains("attacked") || f.Contains("hurt")), Is.False,
                    "The observer should have no knowledge of an event it never perceived.");

                // Not asserting the observer's Memory/Belief are empty outright: its own routine, unrelated
                // HTN/Goal churn can legitimately produce an "outcome" memory via the feedback loop (spec
                // section 11) within these 5 ticks - that's a real, unrelated feature working correctly, not
                // a perception-boundary leak. What actually matters here is that nothing in either store
                // mentions the victim/attacker specifically.
                var observerMemory = server.EntMan.GetComponent<MemoryComponent>(observer);
                Assert.That(observerMemory.Memories.Any(m => m.Participants.Contains(victim) || m.Participants.Contains(attacker)), Is.False,
                    "The observer should have no memory referencing the victim or attacker of an event it never witnessed.");

                var observerBelief = server.EntMan.GetComponent<BeliefComponent>(observer);
                Assert.That(observerBelief.Beliefs.Any(b => b.Participants.Contains(victim) || b.Participants.Contains(attacker)), Is.False,
                    "The observer should have no belief referencing the victim or attacker of an event it never witnessed.");

                var observerEmotion = server.EntMan.GetComponent<EmotionComponent>(observer);
                Assert.That(observerEmotion.Fear, Is.EqualTo(0f), "The observer should feel nothing about an event it never perceived.");
            });

            // Sanity check the setup itself was meaningful: the victim (a legacy AI player) really did
            // register the attack via DangerComponent - proving this is a real perception-boundary result for
            // the observer, not just an empty test that would pass regardless of whether DangerSystem even ran.
            Assert.That(server.EntMan.HasComponent<EmotionComponent>(victim), Is.False,
                "Test setup: the victim is a legacy AI player and should have no EmotionComponent at all.");
            var victimDanger = server.EntMan.GetComponent<DangerComponent>(victim);
            Assert.That(victimDanger.ThreatSource, Is.EqualTo(attacker), "Test setup: the victim itself should have registered the attack.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(observer);
            server.EntMan.DeleteEntity(victim);
            server.EntMan.DeleteEntity(attacker);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
