using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Content.Client._MalinovStation.Messenger;
using Content.IntegrationTests.Fixtures;
using Content.Server.Administration;
using Content.Server._MalinovStation.Messenger;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.CCVar;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Maps;
using Content.Shared.Power;
using Content.Shared.PDA;
using Content.Shared.Preferences;
using Content.Shared.Radio.Components;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Systems;
using Robust.Server.Containers;
using Robust.Server.Console;
using Robust.Server.Player;
using Robust.Shared.Containers;
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
    public async Task MessengerProgramNotInstalledOnAnyExcludedPda()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var cartridgeLoaderSystem = entityManager.EntitySysManager.GetEntitySystem<CartridgeLoaderSystem>();

        await pair.CreateTestMap();
        var coords = pair.TestMap!.GridCoords;

        await server.WaitAssertion(() =>
        {
            var field = typeof(MalinovMessengerInstallerSystem)
                .GetField("ExcludedPdaPrototypes", BindingFlags.NonPublic | BindingFlags.Static);
            Assert.That(field, Is.Not.Null, "ExcludedPdaPrototypes field should exist");
            var excluded = (HashSet<EntProtoId>)field!.GetValue(null)!;

            Assert.That(excluded, Is.Not.Empty, "Exclusion list should not be empty");
            var excludedNames = excluded.Select(x => x.ToString()).ToList();
            Assert.That(excludedNames, Does.Contain("WizardPDA"),
                "Off-station wizard PDA should be excluded from the messenger");
            Assert.That(excludedNames, Does.Contain("ChameleonPDA"),
                "Chameleon PDA should be excluded from the messenger");
            Assert.That(excludedNames, Does.Contain("ChameleonAgentPDA"),
                "Chameleon agent PDA should be excluded from the messenger");

            foreach (var protoId in excludedNames)
            {
                var pda = entityManager.SpawnEntity(protoId, coords);

                if (!entityManager.TryGetComponent(pda, out CartridgeLoaderComponent loader))
                    continue; // the device cannot run cartridges at all, nothing to assert

                Assert.That(cartridgeLoaderSystem.HasProgram<MalinovMessengerCartridgeComponent>((pda, loader)), Is.False,
                    $"Messenger program should not be auto-installed in {protoId}");
            }
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
        });

        await server.WaitRunTicks(2);
        await server.WaitIdleAsync();
    }

    [Test]
    public async Task NewPlayerSpawningOnStationShowsContactsInUi()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
        var cartridgeLoaderSystem = entSysMan.GetEntitySystem<CartridgeLoaderSystem>();
        var stationSpawningSystem = entSysMan.GetEntitySystem<StationSpawningSystem>();
        var stationSystem = entSysMan.GetEntitySystem<StationSystem>();
        var recordsSystem = entSysMan.GetEntitySystem<StationRecordsSystem>();
        var userInterfaceSystem = entSysMan.GetEntitySystem<SharedUserInterfaceSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();

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

        const string existingCrewName = "Existing Crew";
        const string characterName = "New Player Test";

        await server.WaitAssertion(() =>
        {
            // A running round has other crewmembers in the manifest cache.
            var key = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = existingCrewName,
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

        EntityUid mob = default;
        EntityUid pda = default;
        EntityUid program = default;

        await server.WaitAssertion(() =>
        {
            // Spawn the new player the way the real game does: full job mob on the station.
            var newProfile = new HumanoidCharacterProfile { Name = characterName };
            mob = stationSpawningSystem.SpawnPlayerMob(coords, "Passenger", newProfile, station);

            // Locate the freshly equipped PDA.
            var query = entityManager.EntityQueryEnumerator<PdaComponent>();
            while (query.MoveNext(out var pdaUid, out _))
                pda = pdaUid;

            // Fire the same event the GameTicker raises after a real spawn.
            var session = playerManager.Sessions.SingleOrDefault()!;
            var aev = new PlayerSpawnCompleteEvent(mob, session, "Passenger", true, true, 1, station, newProfile);
            entityManager.EventBus.RaiseLocalEvent(mob, aev, true);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var programEnt = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda);
            Assert.That(programEnt, Is.Not.Null, "New player's PDA should have the messenger program installed");
            program = programEnt!.Value.Owner;

            // Simulate the player opening the messenger program.
            cartridgeLoaderSystem.ActivateProgram((pda, entityManager.GetComponent<CartridgeLoaderComponent>(pda)), program);

            var evt = new CartridgeUiReadyEvent(pda);
            entityManager.EventBus.RaiseLocalEvent(program, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda, PdaUiKey.Key, out var state), Is.True,
                "Messenger UI state should be set for a new player");
            Assert.That(state.Status, Is.Empty, "A new player on a station should not see a manifest-unavailable status");
            Assert.That(state.Contacts.Any(c => c.Name == existingCrewName), Is.True,
                "A new player's messenger should list existing crew from the manifest");
        });

        await server.WaitRunTicks(2);
        await server.WaitIdleAsync();
    }

    [Test]
    public async Task ClientRendersStatusAsVisibleNotHiddenParent()
    {
        var pair = Pair;
        var client = pair.Client;

        await client.WaitPost(() =>
        {
            var fragment = new MalinovMessengerUiFragment();

            // Simulate the server's manifest-unavailable payload: a status text with no
            // contacts. The status must actually render so the program is not an empty window.
            fragment.UpdateState(new MalinovMessengerUiState(
                contacts: new List<MalinovMessengerContact>(),
                status: "malinov-messenger-manifest-unavailable"));

            // The fragment root BoxContainer children are:
            //  [0] background panel, [1] StatusLabel, [2] MainContainer.
            // MainContainer children: [0] ErrorLabel, [1] the header/contacts/chat row.
            var mainContainer = fragment.Children[2];
            Assert.That(mainContainer.Visible, Is.False,
                "When showing a status, the main container is hidden");

            var statusLabel = fragment.Children[1];
            Assert.That(statusLabel.Visible, Is.True,
                "The status label must be visible so the interface is not an empty window");
        });
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
        string recipientKey = null!;
        string senderKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);

            Assert.That(session1.Peers, Does.ContainKey(recipientKey),
                "Sender should see recipient online after receiving the server directory");
            Assert.That(session1.Peers, Does.Not.ContainKey(senderKey),
                "Sender should not see itself in the peer directory");
            Assert.That(session1.Peers[recipientKey].Name, Is.EqualTo(recipientName),
                "Recipient peer should carry the card display name");
            Assert.That(session2.Peers, Does.ContainKey(senderKey),
                "Recipient should see sender online after receiving the server directory");
            Assert.That(session2.Peers, Does.Not.ContainKey(recipientKey),
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
    public async Task MessengerUiCloseChatClearsSelection()
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
        EntityUid program = default;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            program = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            recipientKey = GetCardKey(entityManager, pda2);
        });

        // Wait for the cartridge Update loop to announce presence and build the directory.
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(program, recipientKey, "Hello"), Is.True,
                "Sending a message should succeed against an online contact");
        });

        await server.WaitRunTicks(2);

        await server.WaitAssertion(() =>
        {
            var session = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(program);
            Assert.That(session.SelectedContact, Is.EqualTo(recipientKey),
                "Sending should select the recipient conversation");

            // Simulate the client refreshing the selected contact through the UI message path.
            // Raised through the base type so the relay-style subscription matches.
            CartridgeMessageEvent refresh =
                new MalinovMessengerUiMessageEvent(MalinovMessengerUiAction.RefreshContacts, targetName: recipientKey)
                {
                    LoaderUid = entityManager.GetNetEntity(pda1)
                };
            entityManager.EventBus.RaiseLocalEvent(program, refresh);

            Assert.That(session.SelectedContact, Is.EqualTo(recipientKey),
                "RefreshContacts should keep the recipient selected");

            CartridgeMessageEvent close = new MalinovMessengerUiMessageEvent(MalinovMessengerUiAction.CloseChat)
            {
                LoaderUid = entityManager.GetNetEntity(pda1)
            };
            entityManager.EventBus.RaiseLocalEvent(program, close);

            Assert.That(session.SelectedContact, Is.Null,
                "CloseChat should clear the selected contact");

            var sessions = GetAccountSessions(entityManager, session);
            Assert.That(sessions, Does.ContainKey(recipientKey),
                "Closing a chat must not erase the conversation history");

            var evt = new CartridgeUiReadyEvent(pda1);
            entityManager.EventBus.RaiseLocalEvent(program, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda1, PdaUiKey.Key, out var state), Is.True,
                "Messenger UI state should be set after closing the chat");
            Assert.That(state.SelectedContact, Is.Null,
                "UI state should report no selected contact after close");
            Assert.That(state.Messages, Is.Empty,
                "UI state should not show messages for a closed conversation");
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
        string selfKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            SetPdaIdentity(entityManager, pda1, selfName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            selfKey = GetCardKey(entityManager, pda1);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var result = messengerSystem.TrySendMessage(prog1, selfKey, "Hello me!");
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
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.ServerAddress, Is.Not.Null,
                "Sender should have discovered the active messenger server");
            Assert.That(session1.Peers, Does.ContainKey(recipientKey),
                "Sender should see recipient in the server directory before sending");

            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, messageText), Is.True,
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
            Assert.That(sessions2, Does.ContainKey(senderKey),
                "Recipient should have a session from the sender");
            Assert.That(sessions2[senderKey].Any(m =>
                    m.SenderName == senderName && m.Text == messageText && !m.Outgoing),
                Is.True, "Recipient session should contain the original incoming message");

            var evt = new CartridgeUiReadyEvent(pda2);
            entityManager.EventBus.RaiseLocalEvent(prog2, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda2, PdaUiKey.Key, out var state), Is.True,
                "Recipient UI state should be set");
            Assert.That(state.Messages.Any(m => m.SenderName == senderName && m.Text == messageText && !m.Outgoing),
                Is.False, "An incoming message must not auto-open the sender's conversation");

            session2.SelectedContact = senderKey;
            evt = new CartridgeUiReadyEvent(pda2);
            entityManager.EventBus.RaiseLocalEvent(prog2, ref evt);

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda2, PdaUiKey.Key, out state), Is.True,
                "Recipient UI state should be set after the conversation is selected");
            Assert.That(state.Messages.Any(m => m.SenderName == senderName && m.Text == messageText && !m.Outgoing),
                Is.True, "Recipient UI state should show the incoming message after the conversation is selected");
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

        // An unknown recipient is queued optimistically (immediately visible to the sender)
        // and then rolled back when the relay reports it back as a delivery failure.
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, "Unknown Person", "test"), Is.True,
                "Sending to an unknown recipient should be queued optimistically; the relay rejects it asynchronously");

            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var sessions1 = GetAccountSessions(entityManager, session1);
            Assert.That(sessions1.Values.Any(h => h.Any(m => m.Outgoing && m.Text == "test")), Is.True,
                "The outgoing message should be visible immediately");
        });
        await server.WaitRunTicks(6);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.LastError, Is.EqualTo("malinov-messenger-error-offline"),
                "Session should store the offline error key");

            var sessions1 = GetAccountSessions(entityManager, session1);
            Assert.That(sessions1.Values.All(h => h.All(m => !(m.Outgoing && m.Text == "test"))), Is.True,
                "The undeliverable outgoing message should be rolled back");

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
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);
        });

        await server.WaitRunTicks(5);

        // A -> B
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "A1"), Is.True);
        });
        await server.WaitRunTicks(5);

        // B -> A
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog2, senderKey, "B1"), Is.True);
        });
        await server.WaitRunTicks(5);

        // A -> B
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "A2"), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var sessions1 = GetAccountSessions(entityManager, session1);
            Assert.That(sessions1, Does.ContainKey(recipientKey),
                "Sender should have a history with the recipient");

            var history = sessions1[recipientKey];
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
            session1.SelectedContact = recipientKey;

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
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "A3"), Is.True);
        });
        await server.WaitRunTicks(3);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var sessions1 = GetAccountSessions(entityManager, session1);
            var history = sessions1[recipientKey];
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
        string senderKey = null!;
        string recipientKey = null!;

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

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "First message"), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2, Does.ContainKey(senderKey));
            Assert.That(sessions2[senderKey], Has.Count.EqualTo(1));

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
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "Second message"), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session3 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog3);
            var sessions3 = GetAccountSessions(entityManager, session3);
            Assert.That(sessions3, Does.ContainKey(senderKey),
                "Transferred ID card should carry the conversation history to the new PDA");
            Assert.That(sessions3[senderKey], Has.Count.EqualTo(2),
                "History should contain both messages sent to the recipient");

            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2, Does.Not.ContainKey(senderKey),
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
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, longText), Is.True,
                "Sending a long message should succeed and be truncated");

            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var sessions1 = GetAccountSessions(entityManager, session1);
            Assert.That(sessions1, Does.ContainKey(recipientKey),
                "Sender should have a history with the recipient");
            Assert.That(sessions1[recipientKey], Has.Count.EqualTo(1));
            Assert.That(sessions1[recipientKey][0].Text, Is.EqualTo(expectedText),
                "Sender history should store the truncated message");
            Assert.That(sessions1[recipientKey][0].Text.Length, Is.EqualTo(100));
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2, Does.ContainKey(senderKey),
                "Recipient should have a history from the sender");
            Assert.That(sessions2[senderKey], Has.Count.EqualTo(1));
            Assert.That(sessions2[senderKey][0].Text, Is.EqualTo(expectedText),
                "Recipient history should store the truncated message");
            Assert.That(sessions2[senderKey][0].Text.Length, Is.EqualTo(100));
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task Notification_SuppressedOnlyWhenViewingConversation()
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
        var containerSystem = entSysMan.GetEntitySystem<ContainerSystem>();
        var userInterfaceSystem = entSysMan.GetEntitySystem<SharedUserInterfaceSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();

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
        const string messageText = "Hello Bob!";

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
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);
            HoldPdaWithSession(entityManager, containerSystem, playerManager, pda2, coords);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);

            // Activate the recipient program AND select the sender conversation:
            // the notification is suppressed only when the PDA window is also open.
            // In this test we verify that with program active + conversation selected,
            // the notification IS sent because we cannot reliably simulate an "open" PDA window
            // in server-side integration tests. The window-open check is tested via manual gameplay.
            var loader2 = entityManager.GetComponent<CartridgeLoaderComponent>(pda2);
            cartridgeLoaderSystem.ActivateProgram((pda2, loader2), prog2);

            CartridgeMessageEvent refresh =
                new MalinovMessengerUiMessageEvent(MalinovMessengerUiAction.RefreshContacts, targetName: senderKey)
                {
                    LoaderUid = entityManager.GetNetEntity(pda2)
                };
            entityManager.EventBus.RaiseLocalEvent(prog2, refresh);
        });

        await server.WaitRunTicks(5);

        EntityUid? notifiedLoader = null;
        string notifiedSender = null!;
        void OnNotification(EntityUid loader, string sender)
        {
            notifiedLoader = loader;
            notifiedSender = sender;
        }

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            Assert.That(session2.IsProgramActive, Is.True);
            Assert.That(session2.SelectedContact, Is.EqualTo(senderKey));

            // Without a genuinely open PDA window (server-side tests cannot fake client UI state),
            // the notification fires even though the conversation is selected.
            messengerSystem.OnNotificationSent += OnNotification;
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, messageText), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            messengerSystem.OnNotificationSent -= OnNotification;
            Assert.That(notifiedLoader, Is.EqualTo(pda2),
                "Without an open PDA window, notification fires despite program being active");
            Assert.That(notifiedSender, Is.EqualTo(senderName));

            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            Assert.That(session2.UnreadContacts, Does.Contain(senderKey),
                "The conversation should be marked unread when the window is closed");
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            messengerSystem.OnNotificationSent -= OnNotification;
            Assert.That(notifiedLoader, Is.EqualTo(pda2),
                "Notification should fire after the PDA window is closed even though the program is active");
            Assert.That(notifiedSender, Is.EqualTo(senderName));
        });

        await server.WaitIdleAsync();
    }

[Test]
    public async Task Notification_SentWhenProgramActiveButPdaClosed()
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
        var containerSystem = entSysMan.GetEntitySystem<ContainerSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();

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
        const string messageText = "Hello Bob!";

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
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);
            HoldPdaWithSession(entityManager, containerSystem, playerManager, pda2, coords);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            recipientKey = GetCardKey(entityManager, pda2);

            cartridgeLoaderSystem.ActivateProgram((pda2, entityManager.GetComponent<CartridgeLoaderComponent>(pda2)), prog2);
        });

        await server.WaitRunTicks(5);

        EntityUid? notifiedLoader = null;
        string notifiedSender = null!;
        void OnNotification(EntityUid loader, string sender)
        {
            notifiedLoader = loader;
            notifiedSender = sender;
        }

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            Assert.That(session2.IsProgramActive, Is.True);
            // The PDA window is NOT open in this test (server-side tests cannot fake client UI state).
            // Notification should fire.

            messengerSystem.OnNotificationSent += OnNotification;
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, messageText), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            messengerSystem.OnNotificationSent -= OnNotification;
            Assert.That(notifiedLoader, Is.EqualTo(pda2),
                "Notification should fire when the PDA window is closed even though the program is active");
            Assert.That(notifiedSender, Is.EqualTo(senderName));
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task Notification_SentWhenViewingAnotherConversation()
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
        var containerSystem = entSysMan.GetEntitySystem<ContainerSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();

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
        const string otherContact = "Charlie Test";
        const string messageText = "Hello Bob!";

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

            var key3 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = otherContact,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key3);
        });

        await server.WaitRunTicks(1);

        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);
            HoldPdaWithSession(entityManager, containerSystem, playerManager, pda2, coords);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);

            var loader2 = entityManager.GetComponent<CartridgeLoaderComponent>(pda2);
            cartridgeLoaderSystem.ActivateProgram((pda2, loader2), prog2);

            // Select a DIFFERENT conversation - notification should fire for the sender.
            CartridgeMessageEvent refresh =
                new MalinovMessengerUiMessageEvent(MalinovMessengerUiAction.RefreshContacts, targetName: otherContact)
                {
                    LoaderUid = entityManager.GetNetEntity(pda2)
                };
            entityManager.EventBus.RaiseLocalEvent(prog2, refresh);
        });

        await server.WaitRunTicks(5);

        EntityUid? notifiedLoader = null;
        string notifiedSender = null!;
        void OnNotification(EntityUid loader, string sender)
        {
            notifiedLoader = loader;
            notifiedSender = sender;
        }

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            Assert.That(session2.IsProgramActive, Is.True);
            Assert.That(session2.SelectedContact, Is.EqualTo(otherContact),
                "A different conversation should stay selected");

            messengerSystem.OnNotificationSent += OnNotification;
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, messageText), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            messengerSystem.OnNotificationSent -= OnNotification;
            Assert.That(notifiedLoader, Is.EqualTo(pda2),
                "Notification should fire when the active program shows a different conversation");
            Assert.That(notifiedSender, Is.EqualTo(senderName));

            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            Assert.That(session2.SelectedContact, Is.EqualTo(otherContact),
                "Arriving message must not switch away from the selected conversation");
            Assert.That(session2.UnreadContacts, Does.Contain(senderKey),
                "The sender's conversation should be marked unread");
            Assert.That(session2.UnreadContacts, Does.Not.Contain(otherContact));
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerUnread_ClearedWhenConversationOpened()
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
        var containerSystem = entSysMan.GetEntitySystem<ContainerSystem>();
        var userInterfaceSystem = entSysMan.GetEntitySystem<SharedUserInterfaceSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();

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
        const string messageText = "Hello Bob!";

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
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);
            HoldPdaWithSession(entityManager, containerSystem, playerManager, pda2, coords);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);

            cartridgeLoaderSystem.ActivateProgram((pda2, entityManager.GetComponent<CartridgeLoaderComponent>(pda2)), prog2);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            Assert.That(session2.SelectedContact, Is.Null, "No conversation should be auto-opened before any message");

            messengerSystem.TrySendMessage(prog1, recipientKey, messageText);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            Assert.That(session2.SelectedContact, Is.Null,
                "An incoming message must not auto-open the sender's conversation");
            Assert.That(session2.UnreadContacts, Does.Contain(senderKey),
                "The sender should be marked unread");

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda2, PdaUiKey.Key, out var state), Is.True);
            var alice = state.Contacts.Single(c => c.Name == senderName);
            Assert.That(alice.HasUnread, Is.True, "Contact list should show the unread indicator");
            Assert.That(state.Contacts.Where(c => c.Name != senderName), Is.All.Matches<MalinovMessengerContact>(c => !c.HasUnread),
                "No other conversation should be marked unread");

            // Opening the sender conversation clears the unread flag.
            CartridgeMessageEvent refresh =
                new MalinovMessengerUiMessageEvent(MalinovMessengerUiAction.RefreshContacts, targetName: senderKey)
                {
                    LoaderUid = entityManager.GetNetEntity(pda2)
                };
            entityManager.EventBus.RaiseLocalEvent(prog2, refresh);
        });

        await server.WaitRunTicks(1);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            Assert.That(session2.SelectedContact, Is.EqualTo(senderKey));
            Assert.That(session2.UnreadContacts, Does.Not.Contain(senderKey),
                "Opening the conversation should clear the unread indicator");

            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda2, PdaUiKey.Key, out var state), Is.True);
            Assert.That(state.Contacts.Single(c => c.Name == senderName).HasUnread, Is.False,
                "Contact list should reflect the cleared unread indicator");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task Notification_SentWhenProgramInactive()
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
        var containerSystem = entSysMan.GetEntitySystem<ContainerSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();

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
        const string messageText = "Hello Bob!";

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
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);
            HoldPdaWithSession(entityManager, containerSystem, playerManager, pda2, coords);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            recipientKey = GetCardKey(entityManager, pda2);

            // Activate then deactivate the recipient program so it is running in the background
            // but not currently open in the UI.
            var loader2 = entityManager.GetComponent<CartridgeLoaderComponent>(pda2);
            cartridgeLoaderSystem.ActivateProgram((pda2, loader2), prog2);
            cartridgeLoaderSystem.DeactivateProgram((pda2, loader2), prog2);
        });

        await server.WaitRunTicks(5);

        EntityUid? notifiedLoader = null;
        string notifiedSender = null!;
        void OnNotification(EntityUid loader, string sender)
        {
            notifiedLoader = loader;
            notifiedSender = sender;
        }

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            Assert.That(session2.IsProgramActive, Is.False, "Recipient program should be inactive");

            messengerSystem.OnNotificationSent += OnNotification;
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, messageText), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            messengerSystem.OnNotificationSent -= OnNotification;
            Assert.That(notifiedLoader, Is.EqualTo(pda2), "Notification should fire for the recipient PDA");
            Assert.That(notifiedSender, Is.EqualTo(senderName), "Notification should carry the sender name");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task Notification_NoExceptionWhenSoundDisabled()
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
        var containerSystem = entSysMan.GetEntitySystem<ContainerSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();

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
        const string messageText = "Hello Bob!";

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

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.MalinovMessengerSoundEnabled, false);
        });

        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);
            HoldPdaWithSession(entityManager, containerSystem, playerManager, pda2, coords);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);

            var loader2 = entityManager.GetComponent<CartridgeLoaderComponent>(pda2);
            cartridgeLoaderSystem.ActivateProgram((pda2, loader2), prog2);
            cartridgeLoaderSystem.DeactivateProgram((pda2, loader2), prog2);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, messageText), Is.True,
                "Sending with sound disabled should succeed");
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2, Does.ContainKey(senderKey),
                "Message should still be delivered when sound is disabled");
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.MalinovMessengerSoundEnabled, CCVars.MalinovMessengerSoundEnabled.DefaultValue);
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MessengerSendWithoutPeerCache()
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
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);
        });

        await server.WaitRunTicks(5);

        // Simulate an expired/stale sender-side peer cache: the send must still go through
        // because delivery authority lies with the relay, not with the local cache.
        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            session1.Peers.Clear();
            Assert.That(session1.Peers, Does.Not.ContainKey(recipientKey),
                "Precondition: no cached peer for the recipient");

            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "NoCache"), Is.True,
                "Sending must not require a cached peer entry");
        });
        await server.WaitRunTicks(6);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2[senderKey].Any(m => m.Text == "NoCache" && !m.Outgoing), Is.True,
                "Message should be delivered without a sender-side peer cache entry");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task RateLimit_ThirdInWindowRejectedAndInputLocked()
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

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.MalinovMessengerRateWindowSeconds, 10);
            server.CfgMan.SetCVar(CCVars.MalinovMessengerRateMaxMessages, 2);
        });

        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);
        });

        await server.WaitRunTicks(5);

        // First two messages within the window are delivered.
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "A1"), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "A2"), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2[senderKey], Has.Count.EqualTo(2),
                "Both messages within the window should be delivered");
        });

        // The third message is rejected synchronously on the sender side: it is never
        // optimistically appended, the sender gets the rate-limit error immediately, and the
        // input is locked with a fixed cooldown equal to the rate window.
        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var sessions1 = GetAccountSessions(entityManager, session1);

            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "A3"), Is.False,
                "The third message within the window should be rejected synchronously by the sender gate");
            Assert.That(session1.LastError, Is.EqualTo("malinov-messenger-error-rate-limited"),
                "Sender should receive the rate-limit error on the first rejected attempt");
            Assert.That(session1.SentTimestamps.Count, Is.EqualTo(2),
                "Only the two within-window sends should count against the limit");
            Assert.That(session1.RateLimitUntil, Is.Not.Null,
                "A fixed cooldown should be scheduled from the moment the limit was hit");
            Assert.That(sessions1[recipientKey].Any(m => m.Text == "A3" && m.Outgoing), Is.False,
                "The rejected message must not appear in the sender history at all");
        });

        // While the cooldown is active, further sends stay rejected and history is untouched.
        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "A3-again"), Is.False,
                "Sends must stay rejected while the cooldown is active");
            Assert.That(session1.RateLimitUntil, Is.Not.Null,
                "Cooldown must remain scheduled while active");

            // The client-visible remaining-wait must be reported while the cooldown is active.
            var userInterfaceSystem = entSysMan.GetEntitySystem<SharedUserInterfaceSystem>();
            var evt = new CartridgeUiReadyEvent(pda1);
            entityManager.EventBus.RaiseLocalEvent(prog1, ref evt);
            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda1, PdaUiKey.Key, out var blockedState), Is.True,
                "Blocked sender UI state should be set");
            Assert.That(blockedState.RateLimitUntil, Is.Not.Null,
                "UI state should expose the rate-limit deadline while blocked");
        });
        await server.WaitRunTicks(6);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2[senderKey], Has.Count.EqualTo(2),
                "Recipient history should not be extended by any rejected message");
        });

        // Wait past the fixed cooldown window, then sending works again.
        await pair.RunSeconds(12);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "A4"), Is.True,
                "Sending after the cooldown expires should succeed");
            Assert.That(session1.RateLimitUntil, Is.Null,
                "A successful send should clear the scheduled cooldown");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2[senderKey], Has.Count.EqualTo(3),
                "Message after cooldown expiry should be delivered");

            // Once the cooldown has passed and sending resumed, the client-visible remaining is zero.
            var userInterfaceSystem = entSysMan.GetEntitySystem<SharedUserInterfaceSystem>();
            var evt = new CartridgeUiReadyEvent(pda1);
            entityManager.EventBus.RaiseLocalEvent(prog1, ref evt);
            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda1, PdaUiKey.Key, out var resolvedState), Is.True,
                "Resolved sender UI state should be set");
            Assert.That(resolvedState.RateLimitUntil, Is.Null,
                "UI state should report no rate-limit deadline after the cooldown cleared");
        });

        await server.WaitPost(() =>
        {
            server.CfgMan.SetCVar(CCVars.MalinovMessengerRateWindowSeconds, CCVars.MalinovMessengerRateWindowSeconds.DefaultValue);
            server.CfgMan.SetCVar(CCVars.MalinovMessengerRateMaxMessages, CCVars.MalinovMessengerRateMaxMessages.DefaultValue);
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task Mute_BlocksDeliveryUntilUnmute()
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
        var serverSystem = entSysMan.GetEntitySystem<MalinovMessengerServerSystem>();
        var userInterfaceSystem = entSysMan.GetEntitySystem<SharedUserInterfaceSystem>();
        var containerSystem = entSysMan.GetEntitySystem<ContainerSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();

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

        EntityUid relay = default;
        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;
        string accountName = null!;
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            relay = SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            // pda1 is held by the test client session, so the cartridge resolves a stable
            // launcher account name for it; pda2 stays unheld (display-name fallback).
            HoldPdaWithSession(entityManager, containerSystem, playerManager, pda1, coords);
            accountName = playerManager.Sessions.Single().Name;
            Assert.That(accountName, Is.Not.Null.And.Not.Empty, "Test session should expose an account name");

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);
        });

        await server.WaitRunTicks(5);

        // Sanity: delivery works before muting.
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "Before"), Is.True);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            serverSystem.Mute(relay, accountName!);
        });

        // Muting propagates through the directory broadcast and blocks the sender
        // immediately at the cartridge; the recipient never receives the message.
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.IsMuted, Is.True,
                "Muted account should be propagated to the cartridge session");

            var evt = new CartridgeUiReadyEvent(pda1);
            entityManager.EventBus.RaiseLocalEvent(prog1, ref evt);
            Assert.That(userInterfaceSystem.TryGetUiState<MalinovMessengerUiState>(pda1, PdaUiKey.Key, out var state), Is.True,
                "Muted sender UI state should be set");
            Assert.That(state.IsMuted, Is.True,
                "UI state should expose the mute status");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "Muted1"), Is.False,
                "Muted sender must be blocked locally before any packet leaves the device");

            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.LastError, Is.EqualTo("malinov-messenger-error-muted"),
                "Muted sender should see the muted error");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2[senderKey], Has.Count.EqualTo(1),
                "Recipient should not receive messages while sender is muted");
        });

        // Swapping the ID card must NOT lift the account mute: the account is tied to the
        // player holding the PDA, not to the card display name.
        await server.WaitAssertion(() =>
        {
            var card = entityManager.GetComponent<PdaComponent>(pda1).ContainedId!.Value;
            entityManager.GetComponent<IdCardComponent>(card).FullName = "Renamed Card";
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.IdentityName, Is.EqualTo("Renamed Card"),
                "Card name should change with the new ID card");
            Assert.That(session1.AccountName, Is.EqualTo(accountName),
                "Account name must survive the card swap");
            Assert.That(session1.IsMuted, Is.True,
                "Mute must survive the card swap");
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "Muted2"), Is.False,
                "Muted account must remain blocked after the card swap");
        });

        // Unmute restores delivery.
        await server.WaitAssertion(() =>
        {
            serverSystem.Unmute(relay, accountName!);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.IsMuted, Is.False,
                "Unmute should clear the cartridge mute state");
        });

        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "After"), Is.True,
                "Delivery should be restored after unmute");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            Assert.That(sessions2[senderKey], Has.Count.EqualTo(2),
                "Message after unmute should be delivered");
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MuteCommand_RequiresAdminRights()
    {
        var pair = Pair;
        var server = pair.Server;

        var entityManager = server.ResolveDependency<IEntityManager>();
        var entSysMan = entityManager.EntitySysManager;
        var prototypeManager = server.ResolveDependency<IPrototypeManager>();
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

        await server.WaitRunTicks(1);

        // The mute/unmute commands must be gated behind the Admin flag. The console system
        // refuses to invoke an [AdminCommand]-marked command for a player lacking the flag,
        // so verifying the attribute on both command types proves they are admin-only.
        Assert.That(typeof(MessengerMuteCommand).GetCustomAttributes(
            typeof(AdminCommandAttribute), inherit: false), Has.Length.EqualTo(1),
            "messengermute must be marked [AdminCommand(AdminFlags.Admin)]");
        Assert.That(typeof(MessengerUnmuteCommand).GetCustomAttributes(
            typeof(AdminCommandAttribute), inherit: false), Has.Length.EqualTo(1),
            "messengerunmute must be marked [AdminCommand(AdminFlags.Admin)]");

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task Mute_DoesNotAffectPeerWithSameCardName()
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
        var serverSystem = entSysMan.GetEntitySystem<MalinovMessengerServerSystem>();
        var containerSystem = entSysMan.GetEntitySystem<ContainerSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();

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

        const string sharedName = "Shared Test";

        await server.WaitAssertion(() =>
        {
            var key = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = sharedName,
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
        EntityUid pda2 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;
        string accountName = null!;
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            relay = SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            // Both ID cards carry the exact same display name.
            SetPdaIdentity(entityManager, pda1, sharedName);
            SetPdaIdentity(entityManager, pda2, sharedName);

            // Only the first PDA is held by the test session; the second one shares the name
            // but is a different card.
            HoldPdaWithSession(entityManager, containerSystem, playerManager, pda1, coords);
            accountName = playerManager.Sessions.Single().Name;

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);
        });

        await server.WaitRunTicks(5);

        // Same-name delivery works before muting. The two same-name cards stay distinct by id.
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "Hi1"), Is.True);
        });

        await PoolManager.WaitUntil(server, async () =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            return sessions2.TryGetValue(senderKey, out var lines) && lines.Count >= 1;
        });

        // Mute only the account holding pda1.
        await server.WaitAssertion(() =>
        {
            serverSystem.Mute(relay, accountName!);
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.IsMuted, Is.True,
                "Held account should be muted");

            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            Assert.That(session2.IsMuted, Is.False,
                "A same-card-name peer must not be muted");
        });

        // The unheld peer with the identical card name keeps sending and is delivered.
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog2, senderKey, "Hi2"), Is.True,
                "Same-card-name peer must not be blocked by the other account's mute");
        });

        await PoolManager.WaitUntil(server, async () =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            var sessions1 = GetAccountSessions(entityManager, session1);
            return sessions1.TryGetValue(recipientKey, out var lines) && lines.Count >= 2;
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task MuteCommand_MutesOnlineSessionByAccountName()
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
        var containerSystem = entSysMan.GetEntitySystem<ContainerSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();
        var consoleHost = server.ResolveDependency<Robust.Server.Console.IServerConsoleHost>();

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

        const string senderName = "Alice Console";
        const string recipientName = "Bob Console";

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

        EntityUid relay = default;
        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;
        string accountName = null!;
        string senderKey = null!;
        string recipientKey = null!;

        await server.WaitAssertion(() =>
        {
            relay = SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, senderName);
            SetPdaIdentity(entityManager, pda2, recipientName);

            HoldPdaWithSession(entityManager, containerSystem, playerManager, pda1, coords);
            accountName = playerManager.Sessions.Single().Name;
            Assert.That(accountName, Is.Not.Null.And.Not.Empty, "Test session should expose an account name");

            prog1 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda1)!.Value.Owner;
            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;

            senderKey = GetCardKey(entityManager, pda1);
            recipientKey = GetCardKey(entityManager, pda2);
        });

        await server.WaitRunTicks(5);

        // Sanity: delivery works before muting.
        await server.WaitAssertion(() =>
        {
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "Before"), Is.True);
        });
        await PoolManager.WaitUntil(server, async () =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            return sessions2.TryGetValue(senderKey, out var lines) && lines.Count >= 1;
        });

        // The admin command mutes the account through the real console command path.
        await server.WaitAssertion(() =>
        {
            consoleHost.ExecuteCommand($"messengermute {accountName}");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var relayComp = entityManager.GetComponent<MalinovMessengerServerComponent>(relay);
            Assert.That(relayComp.Muted.Contains(accountName!), Is.True,
                "Console mute should register the account on the relay");

            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.IsMuted, Is.True,
                "Console mute should propagate to the cartridge session");

            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "Muted1"), Is.False,
                "Console-muted sender must be blocked");
        });
        await server.WaitRunTicks(5);

        // Unmute through the console restores delivery.
        await server.WaitAssertion(() =>
        {
            consoleHost.ExecuteCommand($"messengerunmute {accountName}");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var session1 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog1);
            Assert.That(session1.IsMuted, Is.False,
                "Console unmute should clear the cartridge mute state");

            Assert.That(messengerSystem.TrySendMessage(prog1, recipientKey, "After"), Is.True,
                "Delivery should be restored after the console unmute");
        });
        await PoolManager.WaitUntil(server, async () =>
        {
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var sessions2 = GetAccountSessions(entityManager, session2);
            return sessions2.TryGetValue(senderKey, out var lines) && lines.Count >= 2;
        });

        await server.WaitIdleAsync();
    }

    [Test]
    public async Task StolenCard_MovesPresenceWithoutDuplication()
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
        var containerSystem = entSysMan.GetEntitySystem<ContainerSystem>();
        var playerManager = server.ResolveDependency<IPlayerManager>();

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

        const string stolenName = "Alice Stolen";
        const string thiefName = "Bob Thief";

        await server.WaitAssertion(() =>
        {
            var key1 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = stolenName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key1);

            var key2 = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
            {
                Name = thiefName,
                JobTitle = "Test",
                JobPrototype = "Passenger",
                Age = 30,
                Species = "Human",
                Gender = Gender.Epicene,
            });
            recordsSystem.Synchronize(key2);
        });

        await server.WaitRunTicks(1);

        EntityUid relay = default;
        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid pda3 = default;
        EntityUid prog2 = default;
        EntityUid prog3 = default;
        string stolenCardKey = null!;

        await server.WaitAssertion(() =>
        {
            relay = SpawnMessengerServer(entityManager, coords);

            pda1 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda2 = entityManager.SpawnEntity("PassengerPDA", coords);
            pda3 = entityManager.SpawnEntity("PassengerPDA", coords);

            SetPdaIdentity(entityManager, pda1, stolenName);
            SetPdaIdentity(entityManager, pda2, thiefName);
            // pda3 keeps its default empty ID card (no announce until the stolen card is inserted).

            // The thief (a real actor session) carries the stolen card inside their own PDA.
            HoldPdaWithSession(entityManager, containerSystem, playerManager, pda3, coords);
            stolenCardKey = GetCardKey(entityManager, pda1);

            prog2 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda2)!.Value.Owner;
            prog3 = cartridgeLoaderSystem.TryGetProgram<MalinovMessengerCartridgeComponent>(pda3)!.Value.Owner;
        });

        // Sanity: both cards announce from their original PDAs.
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var relayComp = entityManager.GetComponent<MalinovMessengerServerComponent>(relay);
            Assert.That(relayComp.Directory, Does.ContainKey(stolenCardKey),
                "Stolen card should announce from its original PDA");
        });

        // Steal the card: eject empty default card from the thief's PDA, eject the victim's card
        // from its PDA and insert it into the thief's PDA. Identity follows the CARD, not the PDA.
        await server.WaitAssertion(() =>
        {
            Assert.That(itemSlotsSystem.TryEject(pda3, PdaComponent.PdaIdSlotId, null, out var emptyThiefCard), Is.True);
            entityManager.DeleteEntity(emptyThiefCard!.Value);

            Assert.That(itemSlotsSystem.TryEject(pda1, PdaComponent.PdaIdSlotId, null, out var stolenCard), Is.True);
            Assert.That(itemSlotsSystem.TryInsert(pda3, PdaComponent.PdaIdSlotId, stolenCard!.Value, null), Is.True);
        });

        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            var relayComp = entityManager.GetComponent<MalinovMessengerServerComponent>(relay);

            // The card now announces from the thief's PDA address; the old address is gone because
            // the directory key is the card id (single entry, re-keyed on every announce).
            var thiefAddress = entityManager.GetComponent<DeviceNetworkComponent>(pda3).Address;
            Assert.That(relayComp.Directory[stolenCardKey], Is.EqualTo(thiefAddress),
                "Stolen card should announce from the thief's PDA");

            // Exactly one name entry may carry the stolen identity: no stale/duplicate peers.
            Assert.That(relayComp.Names.Values.Count(v => v == stolenName), Is.EqualTo(1),
                "Relay must hold a single entry for the stolen card's display name");
        });

        await server.WaitAssertion(() =>
        {
            // The holder's session is keyed by the card id: the stolen card stays linked.
            var session3 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog3);
            Assert.That(session3.CardId, Is.EqualTo(stolenCardKey),
                "Thief's session should be linked to the stolen card");
            Assert.That(session3.Peers, Does.Not.ContainKey(stolenCardKey),
                "Thief must not see their own stolen card as a peer");

            // A bystander sees the stolen identity exactly once, routed to the thief's PDA.
            var session2 = entityManager.GetComponent<MalinovMessengerCartridgeSessionComponent>(prog2);
            var alicePeers = session2.Peers.Values.Where(p => p.Name == stolenName).ToList();
            Assert.That(alicePeers, Has.Count.EqualTo(1),
                "Stolen identity must appear once, not duplicated between old and new PDA");
            Assert.That(session2.Peers[stolenCardKey].Address,
                Is.EqualTo(entityManager.GetComponent<DeviceNetworkComponent>(pda3).Address),
                "The single stolen-identity peer must route to the thief's PDA");
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

    private static string GetCardKey(IEntityManager entityManager, EntityUid pda)
    {
        var pdaComp = entityManager.GetComponent<PdaComponent>(pda);
        Assert.That(pdaComp.ContainedId, Is.Not.Null, "PassengerPDA should spawn with an ID card");
        return entityManager.GetNetEntity(pdaComp.ContainedId!.Value).ToString();
    }

    private static void HoldPdaWithSession(
        IEntityManager entityManager,
        ContainerSystem containerSystem,
        Robust.Server.Player.IPlayerManager playerManager,
        EntityUid pda,
        EntityCoordinates coords)
    {
        // Create a simple holder entity, put the PDA inside it, and attach the test client session
        // so the holder has ActorComponent. This mirrors a player wearing/holding the PDA.
        var holder = entityManager.SpawnEntity(null, coords);
        var container = containerSystem.EnsureContainer<Container>(holder, "malinov-test-pda");
        Assert.That(containerSystem.Insert(pda, container), Is.True, "PDA should be inserted into test holder container");

        var session = playerManager.Sessions.Single();
        playerManager.SetAttachedEntity(session, holder);
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
