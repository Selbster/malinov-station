using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server._MalinovStation.Messenger;
using Content.Server.Station.Systems;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.CartridgeLoader;
using Content.Shared.CCVar;
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

    [Test]
    public async Task MessengerP2P_AnnounceMakesContactOnline()
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
            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            entityManager.GetComponent<PdaComponent>(pda1).OwnerName = senderName;
            entityManager.GetComponent<PdaComponent>(pda2).OwnerName = recipientName;

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            cartridgeLoaderSystem.ActivateProgram((pda1, entityManager.GetComponent<CartridgeLoaderComponent>(pda1)), prog1);
            cartridgeLoaderSystem.ActivateProgram((pda2, entityManager.GetComponent<CartridgeLoaderComponent>(pda2)), prog2);
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);

            Assert.That(session1.Peers, Does.ContainKey(recipientName),
                "Sender should see recipient online after receiving their announce");
            Assert.That(session2.Peers, Does.ContainKey(senderName),
                "Recipient should see sender online after receiving their announce");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerP2P_SendDirectMessage()
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
            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            entityManager.GetComponent<PdaComponent>(pda1).OwnerName = senderName;
            entityManager.GetComponent<PdaComponent>(pda2).OwnerName = recipientName;

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            cartridgeLoaderSystem.ActivateProgram((pda1, entityManager.GetComponent<CartridgeLoaderComponent>(pda1)), prog1);
            cartridgeLoaderSystem.ActivateProgram((pda2, entityManager.GetComponent<CartridgeLoaderComponent>(pda2)), prog2);
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientName, messageText), Is.True,
                "Sending to a known online contact should succeed");
        });

        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            Assert.That(session2.Sessions, Does.ContainKey(senderName),
                "Recipient should have a session from the sender");
            Assert.That(session2.Sessions[senderName].Any(m =>
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
    public async Task MessengerP2P_SendToUnknownReturnsError()
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
            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            entityManager.GetComponent<PdaComponent>(pda1).OwnerName = senderName;

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            cartridgeLoaderSystem.ActivateProgram((pda1, entityManager.GetComponent<CartridgeLoaderComponent>(pda1)), prog1);
        });

        await server.WaitRunTicks(3);

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
            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            entityManager.GetComponent<PdaComponent>(pda1).OwnerName = senderName;
            entityManager.GetComponent<PdaComponent>(pda2).OwnerName = recipientName;

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            cartridgeLoaderSystem.ActivateProgram((pda1, entityManager.GetComponent<CartridgeLoaderComponent>(pda1)), prog1);
            cartridgeLoaderSystem.ActivateProgram((pda2, entityManager.GetComponent<CartridgeLoaderComponent>(pda2)), prog2);
        });

        await server.WaitRunTicks(3);

        // A -> B
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientName, "A1"), Is.True);
        });
        await server.WaitRunTicks(3);

        // B -> A
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog2, senderName, "B1"), Is.True);
        });
        await server.WaitRunTicks(3);

        // A -> B
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientName, "A2"), Is.True);
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.Sessions, Does.ContainKey(recipientName),
                "Sender should have a history with the recipient");

            var history = session1.Sessions[recipientName];
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
            var history = session1.Sessions[recipientName];
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
}
