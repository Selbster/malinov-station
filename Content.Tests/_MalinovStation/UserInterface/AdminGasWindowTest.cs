#nullable enable
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Content.Client.Administration.UI.Tabs.AtmosTab;
using Content.Client.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Prototypes;
using Moq;
using NUnit.Framework;
using Robust.Client.Console;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML.Proxy;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization.Manager;
using Robust.UnitTesting;

namespace Content.Tests._MalinovStation.UserInterface;

[TestFixture]
public sealed class AdminGasWindowTest : RobustUnitTest
{
    public override UnitTestProject Project => UnitTestProject.Client;
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly List<string> _commands = new();

    protected override void OverrideIoC()
    {
        base.OverrideIoC();
        var proxyInterface = typeof(XamlProxyManagerStub).Assembly
            .GetType("Robust.Client.UserInterface.XAML.Proxy.IXamlProxyManager")!;
        IoCManager.Instance!.Register(proxyInterface, typeof(XamlProxyManagerStub), overwrite: true);
    }

    [OneTimeSetUp]
    public void SetupUi()
    {
        // Discover serialization definitions without starting content entity systems.
        IoCManager.Resolve<IReflectionManager>().LoadAssemblies(typeof(GasPrototype).Assembly);
        IoCManager.Resolve<ISerializationManager>().Initialize();
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        prototypes.Initialize();
        var yaml = new StringBuilder();
        for (var i = 0; i < Atmospherics.TotalNumberOfGases; i++)
        {
            yaml.AppendLine($"- type: gas\n  id: TestGas{i}\n  name: gas-name-oxygen\n  abbreviation: gas-name-oxygen");
        }
        prototypes.LoadString(yaml.ToString());
        prototypes.ResolveResults();
        IoCManager.Resolve<IUserInterfaceManager>().InitializeTesting();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NoGridsDisablesSubmitAndIgnoresItsCallback(bool fill)
    {
        WithWindow(fill, window =>
        {
            window.Open();
            var grids = window.FindControl<OptionButton>("GridOptions");
            var gases = window.FindControl<OptionButton>("GasOptions");
            var submit = window.FindControl<Button>("SubmitButton");
            Assert.Multiple(() =>
            {
                Assert.That(grids.ItemCount, Is.Zero);
                Assert.That(gases.ItemCount, Is.GreaterThan(0));
                Assert.That(submit.Disabled, Is.True);
            });

            // The handler must also reject an empty selection if an already queued callback arrives.
            Assert.DoesNotThrow(() => Press(submit));
            Assert.That(_commands, Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ReopeningRebuildsOptionsAndSubmitsExactlyOnce(bool fill)
    {
        var entities = IoCManager.Resolve<IEntityManager>();
        var maps = entities.System<SharedMapSystem>();
        var map = maps.CreateMap(out var mapId);
        try
        {
            var grid = maps.CreateGridEntity(mapId);
            var expectedGrid = entities.GetNetEntity(grid.Owner);
            WithWindow(fill, window =>
            {
                window.Open();
                var grids = window.FindControl<OptionButton>("GridOptions");
                var gases = window.FindControl<OptionButton>("GasOptions");
                var submit = window.FindControl<Button>("SubmitButton");
                var gasCount = gases.ItemCount;

                for (var i = 0; i < 2; i++)
                {
                    window.Close();
                    window.Open();
                }

                Assert.Multiple(() =>
                {
                    Assert.That(grids.ItemCount, Is.EqualTo(1));
                    Assert.That(gases.ItemCount, Is.EqualTo(gasCount));
                    Assert.That(submit.Disabled, Is.False);
                });

                Select(gases, gases.GetItemId(1));
                window.FindControl<SpinBox>("AmountSpin").Value = 17;
                if (!fill)
                {
                    window.FindControl<SpinBox>("TileXSpin").Value = 3;
                    window.FindControl<SpinBox>("TileYSpin").Value = 4;
                }
                Press(submit);

                var expected = fill
                    ? $"fillgas {expectedGrid} TestGas1 17"
                    : $"addgas 3 4 {expectedGrid} TestGas1 17";
                Assert.That(_commands, Is.EqualTo(new[] { expected }));
            });
        }
        finally
        {
            entities.DeleteEntity(map);
        }
    }

    private void WithWindow(bool fill, Action<DefaultWindow> action)
    {
        _commands.Clear();
        var parent = IoCManager.Instance!;
        var entities = parent.Resolve<IEntityManager>();
        var scope = parent.FromParent(parent);

        // Only gas catalogue access is substituted. Grid enumeration and IDs use the real engine world.
        var atmosphere = new AtmosphereSystem();
        var gases = (GasPrototype[]) atmosphere.Gases;
        var prototypes = parent.Resolve<IPrototypeManager>();
        for (var i = 0; i < gases.Length; i++)
        {
            gases[i] = prototypes.Index<GasPrototype>($"TestGas{i}");
        }

        var entityAccess = new Mock<IEntityManager>();
        entityAccess.Setup(manager => manager.AllEntityQueryEnumerator<MapGridComponent>())
            .Returns(() => entities.AllEntityQueryEnumerator<MapGridComponent>());
        entityAccess.Setup(manager => manager.GetNetEntity(It.IsAny<EntityUid>(), It.IsAny<MetaDataComponent?>()))
            .Returns((EntityUid uid, MetaDataComponent? metadata) => entities.GetNetEntity(uid, metadata));
        entityAccess.Setup(manager => manager.System<AtmosphereSystem>()).Returns(atmosphere);
        scope.RegisterInstance<IEntityManager>(entityAccess.Object);

        var console = new Mock<IClientConsoleHost>();
        console.Setup(host => host.ExecuteCommand(It.IsAny<string>())).Callback<string>(_commands.Add);
        scope.RegisterInstance<IClientConsoleHost>(console.Object);
        scope.BuildGraph();

        IoCManager.InitThread(scope, replaceExisting: true);
        DefaultWindow? window = null;
        try
        {
            window = fill ? new FillGasWindow() : new AddGasWindow();
            action(window);
        }
        finally
        {
            window?.Close();
            IoCManager.InitThread(parent, replaceExisting: true);
            scope.Clear();
        }
    }

    private static void Press(BaseButton button)
    {
        var callback = (Action<BaseButton.ButtonEventArgs>) typeof(BaseButton)
            .GetField("OnPressed", InstanceMembers)!.GetValue(button)!;
        callback(new BaseButton.ButtonEventArgs(button, null!));
    }

    private static void Select(OptionButton option, int id)
    {
        var callback = (Action<OptionButton.ItemSelectedEventArgs>) typeof(OptionButton)
            .GetField("OnItemSelected", InstanceMembers)!.GetValue(option)!;
        callback(new OptionButton.ItemSelectedEventArgs(id, option));
    }
}
