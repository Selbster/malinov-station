#nullable enable
using System.Collections.Generic;
using System.Linq;
using Content.IntegrationTests.Fixtures;
using Content.IntegrationTests.Fixtures.Attributes;
using Content.Server.Administration.Managers;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.ViewVariables;
using Robust.Server.Console;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.ViewVariables;

namespace Content.IntegrationTests.Tests._MalinovStation.Client;

public sealed class ViewVariablesEntityLifetimeTest : GameTest
{
    // Host promotion has no public undo operation and must not survive into another test.
    private static PoolSettings RemoteSettings => new() { Connected = true, Destructive = true };

    [Test]
    [PairConfig(nameof(RemoteSettings))]
    public async Task RemoteEntityOutsideClientPvsCanLoadBothComponentTabs()
    {
        var map = await Pair.CreateTestMap();
        EntityUid entity = default;
        NetEntity netEntity = default;
        DefaultWindow? window = null;
        HashSet<Control> previous = null!;
        TabContainer tabs = null!;
        try
        {
            await Server.WaitPost(() =>
            {
                Server.CfgMan.SetCVar(CVars.NetPVS, true);
                entity = SEntMan.SpawnEntity(null, map.GridCoords);
                netEntity = SEntMan.GetNetEntity(entity);
                Server.System<SharedVisibilitySystem>().AddLayer(entity, 10);
                Server.ResolveDependency<IAdminManager>().PromoteHost(Pair.Player!);
            });
            await Pair.RunTicksSync(10);
            await Server.WaitAssertion(() =>
                Assert.That(Server.ResolveDependency<IConGroupController>().CanCommand(Pair.Player!, "vv"), Is.True));
            await Client.WaitAssertion(() =>
                Assert.That(CEntMan.TryGetEntity(netEntity, out _), Is.False));

            await Client.WaitPost(() =>
            {
                previous = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children.ToHashSet();
                Client.ResolveDependency<IClientViewVariablesManager>().OpenVV(new ViewVariablesEntitySelector(netEntity));
            });
            // Opening a remote entity requests a session, metadata and then members.
            await Pair.RunTicksSync(20);
            await Client.WaitAssertion(() =>
            {
                window = Client.ResolveDependency<IUserInterfaceManager>().WindowRoot.Children
                    .Except(previous).OfType<DefaultWindow>().Single();
                tabs = Descendants<TabContainer>(window).Single();
                Assert.That(CEntMan.TryGetEntity(netEntity, out _), Is.False);
                Assert.That(tabs.ChildCount, Is.EqualTo(4));
            });
            await Client.WaitPost(() => tabs.CurrentTab = FindTab(tabs,
                "view-variable-instance-entity-client-components-tab-title"));
            await Client.WaitAssertion(() =>
                Assert.That(tabs.GetChild(tabs.CurrentTab).Children.OfType<Button>().Count(), Is.EqualTo(1),
                    "Without a local entity, the client component list contains only the Add button."));

            await Client.WaitPost(() => tabs.CurrentTab = FindTab(tabs,
                "view-variable-instance-entity-server-components-tab-title"));
            await Pair.RunTicksSync(10);
            await Client.WaitAssertion(() =>
            {
                Assert.That(CEntMan.TryGetEntity(netEntity, out _), Is.False);
                Assert.That(tabs.GetChild(tabs.CurrentTab).Children.OfType<Button>().Count(), Is.GreaterThan(1),
                    "The remote inspector must retrieve the server's real component rows.");
            });
        }
        finally
        {
            await Client.WaitPost(() => CloseWindow(window));
            await Pair.RunTicksSync(2);
            await Server.WaitPost(() => SEntMan.DeleteEntity(entity));
        }
    }

    [TestCase(0)]
    [TestCase(int.MaxValue)]
    public async Task MissingEntityOpensObjectInspector(int id)
    {
        var netEntity = new NetEntity(id);
        DefaultWindow? window = null;
        try
        {
            await Client.WaitAssertion(() =>
                Assert.That(CEntMan.TryGetEntity(netEntity, out _), Is.False));
            await Client.WaitPost(() => window = OpenWindow(netEntity));
            await Pair.RunTicksSync(2);
            await Client.WaitAssertion(() => AssertObjectInspector(window!));
        }
        finally
        {
            await Client.WaitPost(() => CloseWindow(window));
        }
    }

    [Test]
    public async Task DeletedLocalEntityOpensObjectInspector()
    {
        DefaultWindow? window = null;
        EntityUid entity = default;
        NetEntity netEntity = default;
        try
        {
            await Client.WaitPost(() =>
            {
                entity = CEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
                netEntity = CEntMan.GetNetEntity(entity);
                CEntMan.DeleteEntity(entity);
            });
            await Client.WaitAssertion(() =>
            {
                Assert.That(netEntity.IsClientSide(), Is.True);
                Assert.That(CEntMan.Deleted(entity), Is.True);
                Assert.That(CEntMan.TryGetEntity(netEntity, out _), Is.False);
            });
            await Client.WaitPost(() => window = OpenWindow(netEntity));
            await Pair.RunTicksSync(2);
            await Client.WaitAssertion(() => AssertObjectInspector(window!));
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                CloseWindow(window);
                CEntMan.DeleteEntity(entity);
            });
        }
    }

    [Test]
    public async Task OpenEntityInspectorCanRefreshAndSearchAfterDeletion()
    {
        EntityUid entity = default;
        DefaultWindow? window = null;
        TabContainer tabs = null!;
        Control components = null!;
        var componentTab = -1;
        try
        {
            await Client.WaitPost(() =>
            {
                entity = CEntMan.SpawnEntity(null, MapCoordinates.Nullspace);
                window = OpenWindow(CEntMan.GetNetEntity(entity));
                tabs = Descendants<TabContainer>(window).Single();
                componentTab = Enumerable.Range(0, tabs.ChildCount).Single(i =>
                    tabs.GetActualTabTitle(i) == Loc.GetString("view-variable-instance-entity-client-components-tab-title"));
                tabs.CurrentTab = componentTab;
                components = tabs.GetChild(componentTab);
            });
            await Pair.RunTicksSync(2);
            await Client.WaitAssertion(() =>
            {
                Assert.That(window!.IsInsideTree, Is.True);
                Assert.That(components.Children.OfType<Button>().Count(), Is.GreaterThan(1),
                    "The live entity must populate component rows before its deletion.");
            });

            await Client.WaitPost(() =>
            {
                tabs.CurrentTab = 0;
                CEntMan.DeleteEntity(entity);
                // This invokes the engine's real OnTabChanged refresh handler.
                tabs.CurrentTab = componentTab;
            });
            await Pair.RunTicksSync(2);
            await Client.WaitPost(() =>
                components.Children.OfType<LineEdit>().Single().SetText("MetaData", invokeEvent: true));
            await Client.WaitAssertion(() =>
            {
                Assert.That(CEntMan.Deleted(entity), Is.True);
                Assert.That(CEntMan.GetComponents(entity), Is.Empty);
                Assert.That(window!.IsInsideTree, Is.True);
                Assert.That(tabs.CurrentTab, Is.EqualTo(componentTab));
                // The empty list retains its search field and Add button.
                Assert.That(components.ChildCount, Is.EqualTo(2));
                Assert.That(components.Children.OfType<LineEdit>().Single().Text, Is.EqualTo("MetaData"));
            });
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                CloseWindow(window);
                CEntMan.DeleteEntity(entity);
            });
        }
    }

    private DefaultWindow OpenWindow(NetEntity entity)
    {
        var ui = Client.ResolveDependency<IUserInterfaceManager>();
        var previous = ui.WindowRoot.Children.ToHashSet();
        Client.ResolveDependency<IClientViewVariablesManager>().OpenVV(entity);
        return ui.WindowRoot.Children.Except(previous).OfType<DefaultWindow>().Single();
    }

    private static void AssertObjectInspector(DefaultWindow window)
    {
        Assert.That(window.IsInsideTree, Is.True);
        var tabs = Descendants<TabContainer>(window).Single();
        Assert.That(tabs.ChildCount, Is.EqualTo(1));
        Assert.That(tabs.GetChild(0).ChildCount, Is.GreaterThan(0),
            "Missing entities still expose their NetEntity value through the object inspector.");
    }

    private static void CloseWindow(DefaultWindow? window)
    {
        window?.Close();
        window?.Dispose();
    }

    private static int FindTab(TabContainer tabs, string title) =>
        Enumerable.Range(0, tabs.ChildCount).Single(i => tabs.GetActualTabTitle(i) == Loc.GetString(title));

    private static IEnumerable<T> Descendants<T>(Control control) where T : Control
    {
        if (control is T match)
            yield return match;

        foreach (var child in control.Children)
        {
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }
}
