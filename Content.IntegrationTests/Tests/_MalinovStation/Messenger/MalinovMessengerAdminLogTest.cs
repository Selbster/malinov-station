using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Server._MalinovStation.Messenger;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.Database;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Maps;
using Content.Shared.PDA;
using Content.Shared.Power;
using Content.Shared.Radio.Components;
using Content.Shared.StationRecords;
using Content.Shared.StationRecords.Systems;
using Robust.Shared.Enums;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._MalinovStation.Messenger;

[TestFixture]
[TestOf(typeof(MalinovMessengerServerSystem))]
public sealed class MalinovMessengerAdminLogTest : GameTest
{
    private const string StationMapId = "MalinovMessengerAdminLogTestStation";

    public override PoolSettings PoolSettings => new()
    {
        AdminLogsEnabled = true,
        DummyTicker = false,
        Connected = true
    };

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
    public async Task SendDeliveryFailRateLimitAndMuteAreLogged()
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

        const string senderName = "Alice AdminLog";
        const string recipientName = "Bob AdminLog";

        await server.WaitAssertion(() =>
        {
            AddRecord(recordsSystem, station, senderName);
            AddRecord(recordsSystem, station, recipientName);
        });

        await server.WaitRunTicks(1);

        EntityUid relay = default;
        EntityUid pda1 = default;
        EntityUid pda2 = default;
        EntityUid prog1 = default;
        EntityUid prog2 = default;

        await server.WaitAssertion(() =>
        {
            relay = SpawnMessengerServer(entityManager, coords);

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
            Assert.That(messengerSystem.TrySendMessage(prog1, recipientName, "Hello"), Is.True);
        });
        await server.WaitRunTicks(5);

        // Delivery failure: recipient not registered in the relay directory.
        await server.WaitAssertion(() =>
        {
            messengerSystem.TrySendMessage(prog1, "Nobody AdminLog", "Hi");
        });
        await server.WaitRunTicks(5);

        // Rate limit: burst past the default per-window maximum (5).
        await server.WaitAssertion(() =>
        {
            for (var i = 0; i < 6; i++)
                messengerSystem.TrySendMessage(prog1, recipientName, $"Burst {i}");
        });
        await server.WaitRunTicks(5);

        // Mute and unmute by an administrator name.
        await server.WaitAssertion(() =>
        {
            serverSystem.Mute(relay, senderName, "TestAdmin");
        });
        await server.WaitRunTicks(5);

        await server.WaitAssertion(() =>
        {
            serverSystem.Unmute(relay, senderName, "TestAdmin");
        });
        await server.WaitRunTicks(5);

        var sAdminLogSystem = server.ResolveDependency<IAdminLogManager>();

        await PoolManager.WaitUntil(server, async () =>
        {
            var logs = await sAdminLogSystem.CurrentRoundLogs();
            return logs.Any(l => l.Type == LogType.MalinovMessengerSend)
                && logs.Any(l => l.Type == LogType.MalinovMessengerDeliveryFail)
                && logs.Any(l => l.Type == LogType.MalinovMessengerRateLimited)
                && logs.Any(l => l.Type == LogType.MalinovMessengerMute);
        });

        await server.WaitAssertion(async () =>
        {
            var logs = await sAdminLogSystem.CurrentRoundLogs();

            Assert.That(logs.Any(l => l.Type == LogType.MalinovMessengerSend
                && l.Message.Contains(senderName) && l.Message.Contains(recipientName)), "Send log should record sender and recipient");

            Assert.That(logs.Any(l => l.Type == LogType.MalinovMessengerDeliveryFail), "Delivery fail log should be emitted");

            Assert.That(logs.Any(l => l.Type == LogType.MalinovMessengerRateLimited
                && l.Message.Contains(senderName)), "Rate limit log should name the sender");

            Assert.That(logs.Any(l => l.Type == LogType.MalinovMessengerMute
                && l.Message.Contains(senderName)), "Mute log should name the muted sender");
        });
    }

    private static void AddRecord(StationRecordsSystem recordsSystem, EntityUid station, string name)
    {
        var key = recordsSystem.AddRecordEntry(station, new GeneralStationRecord
        {
            Name = name,
            JobTitle = "Test",
            JobPrototype = "Passenger",
            Age = 30,
            Species = "Human",
            Gender = Gender.Epicene,
        });
        recordsSystem.Synchronize(key);
    }

    private static void SetPdaIdentity(IEntityManager entityManager, EntityUid pda, string name)
    {
        var pdaComp = entityManager.GetComponent<PdaComponent>(pda);
        Assert.That(pdaComp.ContainedId, Is.Not.Null, "PassengerPDA should spawn with an ID card");

        var idCard = entityManager.GetComponent<IdCardComponent>(pdaComp.ContainedId!.Value);
        idCard.FullName = name;
    }

    private static EntityUid SpawnMessengerServer(IEntityManager entityManager, EntityCoordinates coords)
    {
        var server = entityManager.SpawnEntity("TelecomServer", coords);

        var apcReceiver = entityManager.GetComponent<ApcPowerReceiverComponent>(server);
        apcReceiver.NeedsPower = false;
        apcReceiver.Powered = true;

        var powerEvent = new PowerChangedEvent(true, 0);
        entityManager.EventBus.RaiseLocalEvent(server, ref powerEvent);

        Assert.That(entityManager.HasComponent<TelecomServerComponent>(server), Is.True);
        Assert.That(entityManager.HasComponent<MalinovMessengerServerComponent>(server), Is.True);
        Assert.That(entityManager.HasComponent<DeviceNetworkComponent>(server), Is.True);

        return server;
    }
}
