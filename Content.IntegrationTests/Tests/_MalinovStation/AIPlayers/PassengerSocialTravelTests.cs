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
/// AI Players 0.6, spec section 32: "Sarah is often in Medbay" -&gt; destination Medbay, without hard-coding
/// that pairing anywhere. The entire mechanism is <see cref="MemorySystem.FindKnownLocation"/>'s widened
/// filter (a "social-location" memory resolves a person's name exactly like a place's) plus
/// <see cref="MemorySystem.GetKnownPersonLocations"/> surfacing it into the prompt - no new action, no new
/// resolver path. This test seeds the memory directly (the write side - <see cref="ContextBuilderSystem"/>'s
/// per-visible-character loop writing a "social-location" memory while standing in a named area - happens
/// automatically whenever two cognitive AI players see each other somewhere named) and proves the read side:
/// a real travel commitment to a known person's last-seen location (arrival simulated, see the test's own
/// doc comment), then TalkTo becoming eligible once she's actually visible there.
/// </summary>
[TestFixture]
public sealed class PassengerSocialTravelTests : GameTest
{
    private static readonly ProtoId<JobPrototype> Passenger = "Passenger";
    private const string Map = "PassengerSocialTravelTestMap";

    [TestPrototypes]
    private static readonly string PassengerSocialTravelTestMap = @$"
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

    private async Task ClearOtherMobsNearby(TestPair pair, IReadOnlySet<EntityUid> keep, EntityCoordinates center, float radius)
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

    /// <summary>
    /// Real HTN pathfinding-to-completion isn't observable inside this integration test harness (see
    /// <see cref="PassengerTravelTests.GoToKnownLocation_CreatesAGenuinePersistentMovementCommitment_NotJustAnIntentLabel"/>'s
    /// own doc comment for what was directly confirmed while building these tests). Arrival is simulated the
    /// same established way <see cref="AiBusyStateTests"/> already does for this exact mechanism - this test's
    /// real subject is that a person's name resolves through GoToKnownLocation and that TalkTo becomes eligible
    /// once actually there, not the pathfinding engine underneath it.
    /// </summary>
    [Test]
    public async Task KnownPersonLocation_ResolvesThroughGoToKnownLocation_ThenTalkBecomesEligible()
    {
        var pair = Pair;
        var server = pair.Server;
        var station = await StartRoundAndGetStation(pair);

        EntityUid passenger = default;
        EntityUid sarah = default;
        EntityCoordinates medbay = default;
        await server.WaitPost(() =>
        {
            var players = server.System<AIPlayerSystem>();
            passenger = players.SpawnAiPlayer(Passenger, station, cognitiveMode: true)!.Value;
            sarah = players.SpawnAiPlayer(Passenger, station)!.Value;
            server.System<MetaDataSystem>().SetEntityName(sarah, "Сара");

            // Negative x: the empty test map's single grid chunk covers roughly [-32, 0) and spawn sits right
            // at that edge - a positive offset leaves the grid entirely (nothing to path across).
            medbay = server.EntMan.GetComponent<TransformComponent>(passenger).Coordinates.Offset(new Vector2(-6, 0));
            server.System<SharedTransformSystem>().SetCoordinates(sarah, medbay);

            // Content mirrors ContextBuilderSystem.RecordSocialLocationSighting's exact production convention:
            // the raw entity name inserted verbatim ("Видел(а) Сара...", not the grammatically-declined
            // "Сару") - GetKnownPersonLocations surfaces this Content string as-is, so it must actually contain
            // the base name for a substring check against it to mean anything.
            server.System<MemorySystem>().AddMemory(passenger,
                content: "Видел(а) Сара в «Медотсек».", importance: 0.2f, source: "social-location",
                participants: new[] { sarah }, location: medbay, subject: "Сара");
        });
        await ClearOtherMobsNearby(pair, new HashSet<EntityUid> { passenger, sarah },
            server.EntMan.GetComponent<TransformComponent>(passenger).Coordinates, 10f);

        await server.WaitAssertion(() =>
        {
            var state = server.System<ContextBuilderSystem>().BuildCognitiveState(passenger);
            Assert.That(state, Is.Not.Null);
            Assert.That(state!.KnownLocations.Any(l => l.Contains("Сара")), Is.True,
                "A known person's last-seen location should surface into the prompt-facing KnownLocations list.");

            var gateway = server.System<LlmGatewaySystem>();
            var decision = new LlmCognitiveDecision(
                "social-need", "найти_сару", 0.7f, 0.8f, "Давно не видел(а) Сару, пойду поищу её.",
                GoToKnownLocationAction.ActionName, new Dictionary<string, string> { ["location"] = "Сара" });
            Assert.That(gateway.TryApplyCognitiveDecision(passenger, decision), Is.True,
                "GoToKnownLocation should resolve a person's name exactly like a place's, with no new action.");

            var htn = server.EntMan.GetComponent<HTNComponent>(passenger);
            Assert.That(htn.Blackboard.TryGetValue<EntityCoordinates>(MoveToAction.ForcedDestinationKey, out var stored, server.EntMan), Is.True);
            Assert.That(stored, Is.EqualTo(medbay),
                "The commitment should target exactly where Sarah was last seen, without her needing to be visible right now.");
        });

        // Simulate the arrival a working pathfind would eventually produce.
        await server.WaitPost(() => server.System<SharedTransformSystem>().SetCoordinates(passenger, medbay));
        await pair.RunTicksSync(10);

        await server.WaitAssertion(() =>
        {
            var eligible = server.System<AiActionRegistrySystem>().GetEligibleActions(passenger);
            Assert.That(eligible.Select(a => a.Name), Does.Contain(TalkToAction.ActionName),
                "Now that Sarah is actually visible, TalkTo should become eligible - completing the loop with no new action type.");
        });

        await server.WaitPost(() =>
        {
            server.EntMan.DeleteEntity(passenger);
            server.EntMan.DeleteEntity(sarah);
        });
        await server.WaitPost(() => server.System<GameTicker>().RestartRound());
    }
}
