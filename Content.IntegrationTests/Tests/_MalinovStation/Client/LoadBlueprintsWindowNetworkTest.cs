#nullable enable
using Content.Client.Administration.UI.Tabs.AdminbusTab;
using Content.IntegrationTests.Fixtures;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests._MalinovStation.Client;

public sealed class LoadBlueprintsWindowNetworkTest : GameTest
{
    [Test]
    public async Task OpeningOnReplicatedMapSelectsThePlayersMap()
    {
        var map = await Pair.CreateTestMap();
        EntityUid player = default;
        EntityUid? previousPlayer = null;
        MapId mapId = default;
        LoadBlueprintsWindow? window = null;
        try
        {
            await Server.WaitPost(() =>
            {
                previousPlayer = Pair.Player!.AttachedEntity;
                player = SEntMan.SpawnEntity(null, map.GridCoords);
                mapId = SEntMan.GetComponent<TransformComponent>(player).MapID;
                SEntMan.AddComponent<EyeComponent>(player);
                Server.PlayerMan.SetAttachedEntity(Pair.Player, player);
                Server.PlayerMan.JoinGame(Pair.Player);
            });
            await Pair.RunTicksSync(10);
            await Pair.SyncTicks();
            var clientPlayer = Pair.ToClientUid(player);
            await Client.WaitAssertion(() =>
            {
                Assert.That((int) mapId, Is.GreaterThan(0), "Exercise a server map, not a client-only map.");
                Assert.That(Client.PlayerMan.LocalEntity, Is.EqualTo(clientPlayer));
                Assert.That(Client.Transform(clientPlayer).MapID, Is.EqualTo(mapId));
            });

            await Client.WaitPost(() =>
            {
                window = new LoadBlueprintsWindow();
                IoCManager.InjectDependencies(window);
                window.OpenCentered();
            });
            await Client.WaitAssertion(() =>
            {
                Assert.That(window!.IsInsideTree, Is.True);
                Assert.That(window.FindControl<OptionButton>("MapOptions").SelectedId, Is.EqualTo((int) mapId));
                Assert.That(window.FindControl<Button>("SubmitButton").Disabled, Is.False);
            });
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                window?.Close();
            });
            await Server.WaitPost(() =>
            {
                Server.PlayerMan.SetAttachedEntity(Pair.Player!, previousPlayer);
                SEntMan.DeleteEntity(player);
            });
            await Pair.RunTicksSync(2);
        }
    }
}
