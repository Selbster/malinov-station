#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client.Administration.UI.Tabs.AdminbusTab;
using Moq;
using NUnit.Framework;
using Robust.Client.Console;
using Robust.Client.Player;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.XAML.Proxy;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Map;
using Robust.UnitTesting;

namespace Content.Tests._MalinovStation.UserInterface;

[TestFixture]
public sealed class LoadBlueprintsWindowTest : RobustUnitTest
{
    public override UnitTestProject Project => UnitTestProject.Client;

    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private readonly Mock<IClientConsoleHost> _console = new();
    private readonly List<string> _commands = new();
    private readonly List<EntityUid> _createdEntities = new();
    private IEntityManager _entities = default!;
    private SharedMapSystem _maps = default!;
    private int _nextMapId = 100;

    protected override void OverrideIoC()
    {
        base.OverrideIoC();
        var proxyInterface = typeof(XamlProxyManagerStub).Assembly.GetType("Robust.Client.UserInterface.XAML.Proxy.IXamlProxyManager")!;
        IoCManager.Instance!.Register(proxyInterface, typeof(XamlProxyManagerStub), overwrite: true);
        IoCManager.RegisterInstance<IClientConsoleHost>(_console.Object, overwrite: true);
    }

    [OneTimeSetUp]
    public void SetupUi()
    {
        IoCManager.Resolve<IUserInterfaceManager>().InitializeTesting();
        _entities = IoCManager.Resolve<IEntityManager>();
        _maps = _entities.System<SharedMapSystem>();
        _console.Setup(host => host.ExecuteCommand(It.IsAny<string>())).Callback<string>(_commands.Add);
    }

    [SetUp]
    public void ClearCommands()
    {
        _commands.Clear();
    }

    [TearDown]
    public void DeleteTestEntities()
    {
        for (var i = _createdEntities.Count - 1; i >= 0; i--)
        {
            if (_entities.EntityExists(_createdEntities[i]))
                _entities.DeleteEntity(_createdEntities[i]);
        }
        _createdEntities.Clear();
    }

    [TestCase(-1, false)]
    [TestCase(-41, true)]
    public void ResetSelectsCurrentMapByIdAndCommandsUseThatId(int currentMapId, bool addOtherMap)
    {
        if (addOtherMap)
            CreateMap(-7);
        var mapId = CreateMap(currentMapId);
        var player = SpawnPlayer(new MapCoordinates(new Vector2(12, -6), mapId));
        using var window = CreateWindow(player);

        window.Open();

        var options = window.FindControl<OptionButton>("MapOptions");
        Assert.Multiple(() =>
        {
            Assert.That(options.SelectedId, Is.EqualTo(currentMapId));
            Assert.That(window.FindControl<SpinBox>("XCoordinate").Value, Is.EqualTo(12));
            Assert.That(window.FindControl<SpinBox>("YCoordinate").Value, Is.EqualTo(-6));
        });
        ConfigureCommand(window);
        Press(window, "SubmitButton");
        Press(window, "TeleportButton");
        Assert.That(_commands, Is.EqualTo(new[]
        {
            $"loadbp {mapId} \"/Maps/test-blueprint.yml\" 8 -3 90",
            $"tp 8 -3 {mapId}",
        }));
    }

    [Test]
    public void EmptyMapListDisablesActionsAndRejectsQueuedButtonCallbacks()
    {
        using var window = CreateWindow();
        window.Open();
        AssertMapControls(window, false);
        Assert.That(window.FindControl<OptionButton>("MapOptions").ItemCount, Is.Zero);

        ConfigureCommand(window);
        // Also exercise callbacks already queued before the buttons became disabled.
        Press(window, "SubmitButton");
        Press(window, "TeleportButton");
        Assert.That(_commands, Is.Empty);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void MissingCurrentMapAllowsTheFirstExistingMap(bool playerInNullspace)
    {
        var mapId = CreateMap();
        EntityUid? player = playerInNullspace ? SpawnPlayer(MapCoordinates.Nullspace) : null;
        using var window = CreateWindow(player);
        window.Open();
        AssertMapControls(window, true);
        Assert.That(window.FindControl<OptionButton>("MapOptions").SelectedId, Is.EqualTo((int) mapId));

        Press(window, "TeleportButton");
        Assert.That(_commands, Is.EqualTo(new[] { $"tp 0 0 {mapId}" }));
    }

    [Test]
    public void ResetRefreshesMapsAndReenablesActionsAfterMapsAreAdded()
    {
        var oldMap = CreateMap();
        using var window = CreateWindow();
        window.Open();
        _maps.DeleteMap(oldMap);

        Press(window, "ResetButton");
        Assert.That(MapIds(window), Is.Empty);
        AssertMapControls(window, false);

        var newMap = CreateMap();
        Press(window, "ResetButton");
        Assert.That(MapIds(window), Is.EqualTo(new[] { (int) newMap }));
        AssertMapControls(window, true);
        Press(window, "TeleportButton");
        Assert.That(_commands, Is.EqualTo(new[] { $"tp 0 0 {newMap}" }));
    }

    [Test]
    public void ReopeningRefreshesMapIdsAndExecutesEachCommandOnce()
    {
        var currentMap = CreateMap();
        var expectedCommands = new List<string>();
        using var window = CreateWindow();
        for (var opening = 0; opening < 3; opening++)
        {
            window.Open();
            Assert.That(MapIds(window), Is.EqualTo(new[] { (int) currentMap }));
            ConfigureCommand(window);
            Press(window, "SubmitButton");
            Press(window, "TeleportButton");
            expectedCommands.Add($"loadbp {currentMap} \"/Maps/test-blueprint.yml\" 8 -3 90");
            expectedCommands.Add($"tp 8 -3 {currentMap}");
            Assert.That(_commands, Is.EqualTo(expectedCommands));
            window.Close();

            if (opening == 0)
            {
                _maps.DeleteMap(currentMap);
                currentMap = CreateMap();
            }
        }
    }

    [TestCase("SubmitButton")]
    [TestCase("TeleportButton")]
    public void RemovedSelectedMapCancelsTheActionBeforeSelectingAFallback(string buttonName)
    {
        var fallback = CreateMap();
        var selected = CreateMap();
        using var window = CreateWindow();
        window.Open();
        var options = window.FindControl<OptionButton>("MapOptions");
        SelectOption(options, (int) selected);
        Assert.That(options.SelectedId, Is.EqualTo((int) selected));
        ConfigureCommand(window);
        _maps.DeleteMap(selected);

        Press(window, buttonName);

        Assert.Multiple(() =>
        {
            Assert.That(_commands, Is.Empty, "The cancelled action must not execute on the fallback map.");
            Assert.That(MapIds(window), Is.EqualTo(new[] { (int) fallback }));
            Assert.That(options.SelectedId, Is.EqualTo((int) fallback));
        });
        AssertMapControls(window, true);
        ConfigureCommand(window);
        Press(window, buttonName);
        var expected = buttonName == "SubmitButton"
            ? $"loadbp {fallback} \"/Maps/test-blueprint.yml\" 8 -3 90"
            : $"tp 8 -3 {fallback}";
        Assert.That(_commands, Is.EqualTo(new[] { expected }));
    }

    private MapId CreateMap(int? id = null)
    {
        // Client-created map entities must use negative IDs. They exercise the same ID-vs-index
        // selection contract as server maps without fabricating network ownership in this unit fixture.
        var mapId = new MapId(id ?? -(_nextMapId += 37));
        _createdEntities.Add(_maps.CreateMap(mapId));
        return mapId;
    }

    private EntityUid SpawnPlayer(MapCoordinates coordinates)
    {
        var player = _entities.SpawnEntity(null, coordinates);
        _createdEntities.Add(player);
        return player;
    }

    private static LoadBlueprintsWindow CreateWindow(EntityUid? localEntity = null)
    {
        var window = new LoadBlueprintsWindow();
        IoCManager.InjectDependencies(window);
        var player = new Mock<IPlayerManager>();
        player.SetupGet(manager => manager.LocalEntity).Returns(localEntity);
        // Keep the engine player manager intact; only this window needs a controlled local player.
        typeof(LoadBlueprintsWindow).GetField("_playerManager", InstanceMembers)!.SetValue(window, player.Object);
        return window;
    }

    private static int[] MapIds(LoadBlueprintsWindow window)
    {
        var options = window.FindControl<OptionButton>("MapOptions");
        return Enumerable.Range(0, options.ItemCount).Select(options.GetItemId).ToArray();
    }

    private static void AssertMapControls(LoadBlueprintsWindow window, bool enabled)
    {
        Assert.Multiple(() =>
        {
            Assert.That(window.FindControl<OptionButton>("MapOptions").Disabled, Is.EqualTo(!enabled));
            Assert.That(window.FindControl<Button>("SubmitButton").Disabled, Is.EqualTo(!enabled));
            Assert.That(window.FindControl<Button>("TeleportButton").Disabled, Is.EqualTo(!enabled));
        });
    }

    private static void ConfigureCommand(LoadBlueprintsWindow window)
    {
        window.FindControl<LineEdit>("MapPath").Text = "/Maps/test-blueprint.yml";
        window.FindControl<SpinBox>("XCoordinate").Value = 8;
        window.FindControl<SpinBox>("YCoordinate").Value = -3;
        window.FindControl<SpinBox>("RotationSpin").Value = 90;
    }

    private static void Press(LoadBlueprintsWindow window, string name)
    {
        var button = window.FindControl<Button>(name);
        var callback = (Action<BaseButton.ButtonEventArgs>) typeof(BaseButton)
            .GetField("OnPressed", InstanceMembers)!.GetValue(button)!;
        callback(new BaseButton.ButtonEventArgs(button, null!));
    }

    private static void SelectOption(OptionButton button, int id)
    {
        var callback = (Action<OptionButton.ItemSelectedEventArgs>) typeof(OptionButton)
            .GetField("OnItemSelected", InstanceMembers)!.GetValue(button)!;
        callback(new OptionButton.ItemSelectedEventArgs(id, button));
    }
}
