using Content.IntegrationTests.Fixtures;
using Content.Server.Station.Systems;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.CartridgeLoader;
using Content.Shared.Maps;
using Content.Shared.PDA;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Systems;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.Messenger;

[TestFixture]
[TestOf(typeof(MalinovMessengerCartridgeComponent))]
public sealed class MalinovMessengerTest : GameTest
{
    private const string StationMapId = "MalinovMessengerTestStation";

    [TestPrototypes]
    private const string Prototypes = $@"
- type: gameMap
  id: {StationMapId}
  minPlayers: 0
  mapName: {StationMapId}
  mapPath: /Maps/Test/empty.yml
  stations:
    Station:
      mapNameTemplate: {StationMapId}
      stationProto: StandardNanotrasenStation
      components:
        - type: StationRecords
";

    [Test]
    public async Task MessengerProgramAutoInstalledInPassengerPda()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var cartridgeLoaderSystem = entityManager.EntitySysManager.GetEntitySystem<CartridgeLoaderSystem>();

        await pair.CreateTestMap();
        var coords = pair.TestMap!.GridCoords;

        EntityUid pda = default;

        await server.WaitAssertion(() =>
        {
            pda = entityManager.SpawnEntity("PassengerPDA", coords);

            Assert.That(entityManager.TryGetComponent(pda, out CartridgeLoaderComponent loader), Is.True);
            Assert.That(cartridgeLoaderSystem.HasProgram<MalinovMessengerCartridgeComponent>((pda, loader)), Is.True,
                "Messenger program should be auto-installed in PassengerPDA");
        });

        await server.WaitRunTicks(2);
        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerProgramNotInstalledInCentcomPda()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var cartridgeLoaderSystem = entityManager.EntitySysManager.GetEntitySystem<CartridgeLoaderSystem>();

        await pair.CreateTestMap();
        var coords = pair.TestMap!.GridCoords;

        EntityUid pda = default;

        await server.WaitAssertion(() =>
        {
            pda = entityManager.SpawnEntity("CentcomPDA", coords);

            Assert.That(entityManager.TryGetComponent(pda, out CartridgeLoaderComponent loader), Is.True);
            Assert.That(cartridgeLoaderSystem.HasProgram<MalinovMessengerCartridgeComponent>((pda, loader)), Is.False,
                "Messenger program should not be auto-installed in CentcomPDA");
        });

        await server.WaitRunTicks(2);
        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerUiStateEmptyWithoutStation()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var userInterfaceSystem = entSysMan.GetEntitySystem<SharedUserInterfaceSystem>();

        await pair.CreateTestMap();
        var coords = pair.TestMap!.GridCoords;

        EntityUid pda = default;
        EntityUid program = default;

        await server.WaitAssertion(() =>
        {
            pda = entityManager.SpawnEntity("PassengerPDA", coords);
            var programEnt = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda);
            Assert.That(programEnt, Is.Not.Null, "Messenger program should be auto-installed");
            program = programEnt!.Value.Owner;

            var evt = new CartridgeUiReadyEvent(pda);
            entityManager.EventBus.RaiseLocalEvent(program, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda, PdaUiKey.Key, out var state), Is.True,
                "Messenger UI state should be set");
            Assert.That(state.Status, Is.EqualTo("malinov-messenger-manifest-unavailable"));
            Assert.That(state.Contacts, Is.Empty);
        });

        await server.WaitRunTicks(2);
        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerUiStatePopulatedFromStationRecords()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var recordsSystem = entSysMan.GetEntitySystem<StationRecordsSystem>();
        var userInterfaceSystem = entSysMan.GetEntitySystem<SharedUserInterfaceSystem>();

        var testMap = await pair.CreateTestMap();
        var grid = testMap.Grid.Owner;
        var coords = testMap.GridCoords;

        var stationProto = prototypeManager.Index<GameMapPrototype>(StationMapId);
        EntityUid station = default;

        await server.WaitPost(() =>
        {
            station = stationSystem.InitializeNewStation(
                stationProto.Stations["Station"], [grid], StationMapId, stationProto);
        });

        const string testName = "Test Crewmember";
        const string testJobTitle = "Test Specialist";

        await server.WaitAssertion(() =>
        {
            var record = new GeneralStationRecord
            {
                Name = testName,
                JobTitle = testJobTitle,
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene
            };

            var key = recordsSystem.AddRecordEntry(station, record);
            Assert.That(key.IsValid, Is.True, "Station record should be added");
            recordsSystem.Synchronize(key);
        });

        await server.WaitRunTicks(1);

        EntityUid pda = default;
        EntityUid program = default;

        await server.WaitAssertion(() =>
        {
            pda = entityManager.SpawnEntity("PassengerPDA", coords);
            var programEnt = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda);
            Assert.That(programEnt, Is.Not.Null, "Messenger program should be auto-installed");
            program = programEnt!.Value.Owner;

            var evt = new CartridgeUiReadyEvent(pda);
            entityManager.EventBus.RaiseLocalEvent(program, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda, PdaUiKey.Key, out var state), Is.True,
                "Messenger UI state should be set");
            Assert.That(state.Status, Is.Empty);
            Assert.That(state.Contacts, Has.Count.EqualTo(1));
            Assert.That(state.Contacts[0].Name, Is.EqualTo(testName));
            Assert.That(state.Contacts[0].JobTitle, Is.EqualTo(testJobTitle));
        });

        await server.WaitRunTicks(2);
        await server.WaitIdleAsync();
    }
}
