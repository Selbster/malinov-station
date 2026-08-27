using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._MalinovStation.Messenger;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.CCVar;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Maps;
using Content.Shared.Power;
using Content.Shared.PDA;
using Content.Shared.Radio.Components;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Systems;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
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

    [Test]
    public async Task MessengerServer_BecomesActive()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();

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

        EntityUid pda = default;
        EntityUid program = default;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda = entityManager.SpawnEntity("PassengerPDA", coords);
            program = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda)!.Value.Owner;
            cartridgeLoaderSystem.ActivateProgram((pda, entityManager.GetComponent<CartridgeLoaderComponent>(pda)), program);
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var session = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(program);
            Assert.That(session.ServerAvailable, Is.True,
                "Messenger cartridge should see an active TelecomServer");
            Assert.That(session.ServerAddress, Is.Not.Null,
                "Messenger cartridge should have discovered the TelecomServer address");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerServer_DirectoryMakesContactsOnline()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var recordsSystem = entSysMan.GetEntitySystem<StationRecordsSystem>();

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

        const string senderName = "Alice Test";
        const string recipientName = "Bob Test";

        await server.WaitAssertion(() =>
        {
            var key1 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = senderName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            Assert.That(key1.IsValid, Is.True);
            recordsSystem.Synchronize(key1);

            var key2 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = recipientName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            Assert.That(key2.IsValid, Is.True);
            recordsSystem.Synchronize(key2);
        });

        await server.WaitRunTicks(1);

        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);

            Assert.That(session1.Peers, Does.ContainKey(recipientName),
                "Sender should see recipient online after receiving the server directory");
            Assert.That(session1.Peers, Does.Not.ContainKey(senderName),
                "Sender should not see itself in the peer directory");
            Assert.That(session2.Peers, Does.ContainKey(senderName),
                "Recipient should see sender online after receiving the server directory");
            Assert.That(session2.Peers, Does.Not.ContainKey(recipientName),
                "Recipient should not see itself in the peer directory");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerUiStateExcludesSelf()
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

        const string selfName = "Self Test";

        await server.WaitAssertion(() =>
        {
            var record = new GeneralStationRecord
            {
                Name = selfName,
                JobTitle = "Test Specialist",
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
            SetPdaIdentity(entityManager, pda, selfName);

            var programEnt = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda);
            Assert.That(programEnt, Is.Not.Null, "Messenger program should be auto-installed");
            program = programEnt!.Value.Owner;
        });

        // Wait for the cartridge Update loop to pick up the new ID card identity.
        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var evt = new CartridgeUiReadyEvent(pda);
            entityManager.EventBus.RaiseLocalEvent(program, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda, PdaUiKey.Key, out var state), Is.True,
                "Messenger UI state should be set");
            Assert.That(state.Contacts, Is.Empty,
                "Contact list should exclude the owner's own identity");
            Assert.That(state.SelectedContact, Is.Null,
                "Self-contact selection should be cleared");
        });

        await server.WaitRunTicks(2);
        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerServer_CannotSendToSelf()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var recordsSystem = entSysMan.GetEntitySystem<StationRecordsSystem>();
        var messengerSystem = entSysMan.GetEntitySystem<MalinovMessengerCartridgeSystem>();
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

        const string selfName = "Alice Test";

        await server.WaitAssertion(() =>
        {
            var key = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = selfName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            Assert.That(key.IsValid, Is.True);
            recordsSystem.Synchronize(key);
        });

        await server.WaitRunTicks(1);

        EntityUid pda1 = default;
        EntityUid prog1 = default;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            SetPdaIdentity(entityManager, pda1, selfName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var result = messengerSystem.TrySendMessage(prog1, selfName, "Hello me!");
            Assert.That(result, Is.False, "Sending a message to yourself should fail");

            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.LastError, Is.EqualTo("malinov-messenger-error-self"),
                "Session should store the self-send error key");

            var evt = new CartridgeUiReadyEvent(pda1);
            entityManager.EventBus.RaiseLocalEvent(prog1, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda1, PdaUiKey.Key, out var state), Is.True,
                "Sender UI state should be set");
            Assert.That(state.Status, Is.EqualTo("malinov-messenger-error-self"),
                "Sender UI state should expose the self-send error");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerServer_SendViaServer()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var recordsSystem = entSysMan.GetEntitySystem<StationRecordsSystem>();
        var messengerSystem = entSysMan.GetEntitySystem<MalinovMessengerCartridgeSystem>();
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

        const string senderName = "Alice Test";
        const string recipientName = "Bob Test";
        const string messageText = "Hello from Alice!";

        await server.WaitAssertion(() =>
        {
            var key1 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = senderName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key1);

            var key2 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = recipientName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key2);
        });

        await server.WaitRunTicks(1);

        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.ServerAddress, Is.Not.Null,
                "Sender should have discovered the active messenger server");
            Assert.That(session1.Peers, Does.ContainKey(recipientName),
                "Sender should see recipient in the server directory before sending");

            Assert.That(messengerSystem.TrySendMessage(prog1, recipientName, messageText), Is.True,
                "Sending to a known online contact through the server should succeed");

            var evt1 = new CartridgeUiReadyEvent(pda1);
            entityManager.EventBus.RaiseLocalEvent(prog1, ref evt1);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda1, PdaUiKey.Key, out var senderState), Is.True,
                "Sender UI state should be set immediately after sending");
            Assert.That(senderState.Messages.Any(m => m.SenderName == senderName && m.Text == messageText && m.Outgoing),
                Is.True, "Sender UI state should show the just-sent outgoing message without re-entering the messenger");
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2, Does.ContainKey(senderName),
                "Recipient should have a session from the sender");
            Assert.That(sessions2[senderName].Any(m =>
                    m.SenderName == senderName && m.Text == messageText && !m.Outgoing),
                Is.True, "Recipient session should contain the original incoming message");

            var evt = new CartridgeUiReadyEvent(pda2);
            entityManager.EventBus.RaiseLocalEvent(prog2, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda2, PdaUiKey.Key, out var state), Is.True,
                "Recipient UI state should be set");
            Assert.That(state.Messages.Any(m => m.SenderName == senderName && m.Text == messageText && !m.Outgoing),
                Is.True, "Recipient UI state should show the incoming message");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerServer_SendToUnknownReturnsError()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var recordsSystem = entSysMan.GetEntitySystem<StationRecordsSystem>();
        var messengerSystem = entSysMan.GetEntitySystem<MalinovMessengerCartridgeSystem>();
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

        const string senderName = "Alice Test";

        await server.WaitAssertion(() =>
        {
            var key = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = senderName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key);
        });

        await server.WaitRunTicks(1);

        EntityUid pda1 = default;
        EntityUid prog1 = default;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            SetPdaIdentity(entityManager, pda1, senderName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var result = messengerSystem.TrySendMessage(prog1, "Unknown Person", "test");
            Assert.That(result, Is.False, "Sending to an unknown/offline contact should return false");

            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.LastError, Is.EqualTo("malinov-messenger-error-offline"),
                "Session should store the offline error key");

            var evt = new CartridgeUiReadyEvent(pda1);
            entityManager.EventBus.RaiseLocalEvent(prog1, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda1, PdaUiKey.Key, out var state), Is.True,
                "Sender UI state should be set");
            Assert.That(state.Status, Is.EqualTo("malinov-messenger-error-offline"),
                "Sender UI state should expose the offline error");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerServer_UnavailableShowsError()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var recordsSystem = entSysMan.GetEntitySystem<StationRecordsSystem>();
        var messengerSystem = entSysMan.GetEntitySystem<MalinovMessengerCartridgeSystem>();
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

        const string senderName = "Alice Test";

        await server.WaitAssertion(() =>
        {
            var key = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = senderName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key);
        });

        await server.WaitRunTicks(1);

        EntityUid relay = default;
        EntityUid pda1 = default;
        EntityUid prog1 = default;

        await server.WaitAssertion(() =>
        {
            relay = SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            SetPdaIdentity(entityManager, pda1, senderName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            entityManager.DeleteEntity(relay);
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var result = messengerSystem.TrySendMessage(prog1, "Anyone", "test");
            Assert.That(result, Is.False, "Sending without an active server should fail");

            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.LastError, Is.EqualTo("malinov-messenger-server-unavailable"),
                "Session should store the server-unavailable error key");

            var evt = new CartridgeUiReadyEvent(pda1);
            entityManager.EventBus.RaiseLocalEvent(prog1, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda1, PdaUiKey.Key, out var state), Is.True,
                "Sender UI state should be set");
            Assert.That(state.Status, Is.EqualTo("malinov-messenger-server-unavailable"),
                "Sender UI state should expose the server-unavailable error");
            Assert.That(state.ServerStatus, Is.EqualTo("malinov-messenger-server-unavailable"),
                "Sender UI state should show the server-unavailable mode");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerHistory_PersistsAndLimits()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var recordsSystem = entSysMan.GetEntitySystem<StationRecordsSystem>();
        var messengerSystem = entSysMan.GetEntitySystem<MalinovMessengerCartridgeSystem>();
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

        const string senderName = "Alice Test";
        const string recipientName = "Bob Test";

        await server.WaitAssertion(() =>
        {
            var key1 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = senderName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key1);

            var key2 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = recipientName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key2);
        });

        await server.WaitRunTicks(1);

        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;
        });

        await server.WaitRunTicks(5);

        // A -> B
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientName, "A1"), Is.True);
        });
        await server.WaitRunTicks(5);

        // B -> A
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog2, senderName, "B1"), Is.True);
        });
        await server.WaitRunTicks(5);

        // A -> B
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientName, "A2"), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var sessions1 = GetAccountSessions(entityManager, session1);
            Assert.That(sessions1, Does.ContainKey(recipientName),
                "Sender should have a history with the recipient");

            var history = sessions1[recipientName];
            Assert.That(history, Has.Count.EqualTo(3), "History should contain three messages");
            Assert.That(history[0].Text, Is.EqualTo("A1"));
            Assert.That(history[0].Outgoing, Is.True);
            Assert.That(history[1].Text, Is.EqualTo("B1"));
            Assert.That(history[1].Outgoing, Is.False);
            Assert.That(history[2].Text, Is.EqualTo("A2"));
            Assert.That(history[2].Outgoing, Is.True);
        });

        // Deactivate and reactivate the program; history must survive.
        await server.WaitAssertion(() =>
        {
            var loader1 = entityManager.GetComponent<CartridgeLoaderComponent>(pda1);
            cartridgeLoaderSystem.DeactivateProgram((pda1, loader1), prog1);
            cartridgeLoaderSystem.ActivateProgram((pda1, loader1), prog1);

            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            session1.SelectedContact = recipientName;

            var evt = new CartridgeUiReadyEvent(pda1);
            entityManager.EventBus.RaiseLocalEvent(prog1, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda1, PdaUiKey.Key, out var state), Is.True,
                "UI state should be set after reactivation");
            Assert.That(state.Messages, Has.Count.EqualTo(3), "UI state should still show the full history");
            Assert.That(state.Messages[0].Text, Is.EqualTo("A1"));
            Assert.That(state.Messages[2].Text, Is.EqualTo("A2"));
        });

        // Limit history to two messages and send a fourth one.
        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.MalinovMessengerHistoryPerContact, 2);
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientName, "A3"), Is.True);
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var sessions1 = GetAccountSessions(entityManager, session1);
            var history = sessions1[recipientName];
            Assert.That(history, Has.Count.EqualTo(2), "History should be capped at the configured limit");
            Assert.That(history[0].Text, Is.EqualTo("A2"));
            Assert.That(history[0].Outgoing, Is.True);
            Assert.That(history[1].Text, Is.EqualTo("A3"));
            Assert.That(history[1].Outgoing, Is.True);

            var evt = new CartridgeUiReadyEvent(pda1);
            entityManager.EventBus.RaiseLocalEvent(prog1, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda1, PdaUiKey.Key, out var state), Is.True);
            Assert.That(state.Messages, Has.Count.EqualTo(2), "UI state should reflect the capped history");
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.MalinovMessengerHistoryPerContact, CCVars.MalinovMessengerHistoryPerContact.DefaultValue);
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerIdCardTransferCarriesHistory()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var itemSlotsSystem = entSysMan.GetEntitySystem<ItemSlotsSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var recordsSystem = entSysMan.GetEntitySystem<StationRecordsSystem>();
        var messengerSystem = entSysMan.GetEntitySystem<MalinovMessengerCartridgeSystem>();

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

        const string senderName = "Alice Test";
        const string recipientName = "Bob Test";

        await server.WaitAssertion(() =>
        {
            var key1 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = senderName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key1);

            var key2 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = recipientName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key2);
        });

        await server.WaitRunTicks(1);

        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid pda3 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;
        EntityUid prog3 = default;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda3 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);
            // pda3 keeps its default empty ID card.

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;
            prog3 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda3)!.Value.Owner;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientName, "First message"), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2, Does.ContainKey(senderName));
            Assert.That(sessions2[senderName], Has.Count.EqualTo(1));

            // Eject recipient's ID card from pda2.
            Assert.That(itemSlotsSystem.TryEject(pda2, PdaComponent.PdaIdSlotId, null, out var transferredCard), Is.True);
            Assert.That(transferredCard, Is.Not.Null);

            // Eject the default card from pda3 to make room.
            Assert.That(itemSlotsSystem.TryEject(pda3, PdaComponent.PdaIdSlotId, null, out var oldCard3), Is.True);
            entityManager.DeleteEntity(oldCard3!.Value);

            // Insert recipient's card into pda3.
            Assert.That(itemSlotsSystem.TryInsert(pda3, PdaComponent.PdaIdSlotId, transferredCard!.Value, null), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientName, "Second message"), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session3 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog3);
            var sessions3 = GetAccountSessions(entityManager, session3);
            Assert.That(sessions3, Does.ContainKey(senderName),
                "Transferred ID card should carry the conversation history to the new PDA");
            Assert.That(sessions3[senderName], Has.Count.EqualTo(2),
                "History should contain both messages sent to the recipient");

            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2, Does.Not.ContainKey(senderName),
                "Old PDA without the ID card should no longer have access to the account");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerServer_TruncatesLongMessage()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var recordsSystem = entSysMan.GetEntitySystem<StationRecordsSystem>();
        var messengerSystem = entSysMan.GetEntitySystem<MalinovMessengerCartridgeSystem>();

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

        const string senderName = "Alice Test";
        const string recipientName = "Bob Test";
        var longText = new string('a', 150);
        var expectedText = longText[..100];

        await server.WaitAssertion(() =>
        {
            var key1 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = senderName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key1);

            var key2 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = recipientName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key2);
        });

        await server.WaitRunTicks(1);

        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientName, longText), Is.True,
                "Sending a long message should succeed and be truncated");

            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var sessions1 = GetAccountSessions(entityManager, session1);
            Assert.That(sessions1, Does.ContainKey(recipientName),
                "Sender should have a history with the recipient");
            Assert.That(sessions1[recipientName], Has.Count.EqualTo(1));
            Assert.That(sessions1[recipientName][0].Text, Is.EqualTo(expectedText),
                "Sender history should store the truncated message");
            Assert.That(sessions1[recipientName][0].Text.Length, Is.EqualTo(100));
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2, Does.ContainKey(senderName),
                "Recipient should have a history from the sender");
            Assert.That(sessions2[senderName], Has.Count.EqualTo(1));
            Assert.That(sessions2[senderName][0].Text, Is.EqualTo(expectedText),
                "Recipient history should store the truncated message");
            Assert.That(sessions2[senderName][0].Text.Length, Is.EqualTo(100));
        });

        await server.WaitIdleAsync();
    }

    private static void SetPdaIdentity(IEntityManager entityManager, EntityUid pda, string name)
    {
        var pdaComp = entityManager.GetComponent<PdaComponent>(pda);
        Assert.That(pdaComp.ContainedId, Is.Not.Null, "PassengerPDA should spawn with an ID card");

        var idCard = entityManager.GetComponent<IdCardComponent>(pdaComp.ContainedId!.Value);
        idCard.FullName = name;
    }

    private static Dictionary<string, List<MalinovMessengerMessage>> GetAccountSessions(
        IEntityManager entityManager,
        MalinovMessengerCartridgeSessionComponent session)
    {
        if (session.LinkedAccount is { } accountUid
            && entityManager.TryGetComponent<MalinovMessengerAccountComponent>(accountUid, out var account))
        {
            return account.Sessions;
        }

        return session.LocalSessions;
    }

    private static EntityUid SpawnMessengerServer(IEntityManager entityManager, EntityCoordinates coords)
    {
        var server = entityManager.SpawnEntity("TelecomServer", coords);

        var apcReceiver = entityManager.GetComponent<ApcPowerReceiverComponent>(server);
        apcReceiver.NeedsPower = false;
        apcReceiver.Powered = true;

        var powerEvent = new PowerChangedEvent(true, 0);
        entityManager.EventBus.RaiseLocalEvent(server, ref powerEvent);

        Assert.That(entityManager.HasComponent<TelecomServerComponent>(server), Is.True,
            "Spawned entity should be a TelecomServer");
        Assert.That(entityManager.HasComponent<MalinovMessengerServerComponent>(server), Is.True,
            "TelecomServer should carry the messenger server component");
        Assert.That(entityManager.HasComponent<DeviceNetworkComponent>(server), Is.True,
            "TelecomServer should be connected to the device network");

        return server;
    }
}
