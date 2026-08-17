#nullable enable
using System.Linq;
using System.Threading.Tasks;
using Content.IntegrationTests.Fixtures;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Content.Shared.GameTicking;
using Content.Shared.Inventory;
using Content.Shared.Roles;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.AIPlayers;

/// <summary>
/// Stabilization milestone, stage on equipment: settles whether AI players actually get the same starting
/// equipment a real player would (job startingGear AND their roleLoadout - the O2/N2 survival box, backpack,
/// etc - not just startingGear). AIPlayerSystem.SpawnAiPlayer calls the same
/// StationSpawningSystem.SpawnPlayerCharacterOnStation real players go through, and that method already
/// falls back to RoleLoadout.SetDefault() when no player-chosen loadout exists (which AI players never have,
/// having no player session) - so in principle this should already work. This test proves it rather than
/// assuming it, since StationEngineerGear (the job's startingGear) alone never equips a backpack at all -
/// only the StationEngineerBackpack roleLoadout group does.
/// </summary>
[TestFixture]
public sealed class EngineerEquipmentTests : GameTest
{
    private static readonly ProtoId<JobPrototype> StationEngineer = "StationEngineer";

    private const string Map = "AIPlayerEquipmentTestMap";

    [TestPrototypes]
    private static readonly string AIPlayerEquipmentTestMap = @$"
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
            {StationEngineer}: [ -1, -1 ]
";

    public override PoolSettings PoolSettings => new()
    {
        Connected = true,
        InLobby = true,
        DummyTicker = false,
        Dirty = true,
    };

    /// <summary>
    /// If this fails, AI players are only getting startingGear (belt/eyes/ears - no backpack, no survival
    /// box, no oxygen tank) and missing everything a real player's default roleLoadout would add - a real
    /// gap worth fixing in AIPlayerSystem.SpawnAiPlayer, not just a documentation/expectation mismatch.
    /// </summary>
    [Test]
    public async Task Engineer_GetsRoleLoadoutEquipment_NotJustBareStartingGear()
    {
        var pair = Pair;
        var server = pair.Server;
        server.CfgMan.SetCVar(CCVars.GameMap, Map);
        var ticker = server.System<GameTicker>();

        ticker.ToggleReadyAll(true);
        await server.WaitPost(() => ticker.StartRound());
        await pair.RunTicksSync(10);

        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.InRound));

        var station = server.EntMan.EntityQuery<StationDataComponent>().Select(x => x.Owner).FirstOrDefault();
        Assert.That(server.EntMan.EntityExists(station), "Expected a station to exist after round start.");

        EntityUid aiPlayer = default;
        await server.WaitPost(() =>
        {
            aiPlayer = server.System<AIPlayerSystem>().SpawnAiPlayer(StationEngineer, station)!.Value;
        });
        await pair.RunTicksSync(5);

        await server.WaitAssertion(() =>
        {
            var inventory = server.System<InventorySystem>();

            Assert.That(inventory.TryGetSlotEntity(aiPlayer, "belt", out _),
                "Sanity check: StationEngineerGear (startingGear) should always equip a belt regardless of roleLoadout.");

            var hasBackpack = inventory.TryGetSlotEntity(aiPlayer, "back", out _);
            Assert.That(hasBackpack,
                "AI engineer has no backpack - StationEngineerGear alone never equips one (only the " +
                "StationEngineerBackpack roleLoadout group does), so this means roleLoadout isn't being " +
                "applied to AI players at all, unlike real players spawning into the same job.");
        });

        await server.WaitPost(() => server.EntMan.DeleteEntity(aiPlayer));
        await server.WaitPost(() => ticker.RestartRound());
    }
}
