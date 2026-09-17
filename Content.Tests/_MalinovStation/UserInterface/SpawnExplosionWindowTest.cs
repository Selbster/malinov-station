#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client.Administration.UI.SpawnExplosion;
using Content.Client.Eui;
using Content.Shared.Administration;
using Content.Shared.Eui;
using Content.Shared.Explosion;
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
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Reflection;
using Robust.Shared.Serialization.Manager;
using Robust.UnitTesting;

namespace Content.Tests._MalinovStation.UserInterface;

[TestFixture]
public sealed class SpawnExplosionWindowTest : RobustUnitTest
{
    public override UnitTestProject Project => UnitTestProject.Client;
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    private IEntityManager _entities = default!;
    private SharedMapSystem _maps = default!;
    private SpawnExplosionWindow _window = default!;
    private EntityUid? _localEntity;
    private readonly List<EntityUid> _spawned = new();
    private readonly List<ExplosionPrototype> _types = new();
    private readonly List<SpawnExplosionEuiMsg.PreviewRequest> _previews = new();
    private readonly List<string> _commands = new();

    protected override void OverrideIoC()
    {
        base.OverrideIoC();
        var proxyInterface = typeof(XamlProxyManagerStub).Assembly.GetType("Robust.Client.UserInterface.XAML.Proxy.IXamlProxyManager")!;
        IoCManager.Instance!.Register(proxyInterface, typeof(XamlProxyManagerStub), overwrite: true);
    }

    [OneTimeSetUp]
    public void SetupUi()
    {
        // Load normal serialized prototypes while keeping this fixture's small UI/system harness.
        IoCManager.Resolve<IReflectionManager>().LoadAssemblies(typeof(ExplosionPrototype).Assembly);
        IoCManager.Resolve<ISerializationManager>().Initialize();
        var prototypes = IoCManager.Resolve<IPrototypeManager>();
        prototypes.Initialize();
        prototypes.LoadString("""
            - type: explosion
              id: MalinovTestExplosion
              damagePerIntensity: {}
            - type: explosion
              id: MalinovReplacementExplosion
              damagePerIntensity: {}
            """);
        prototypes.ResolveResults();
        IoCManager.Resolve<IUserInterfaceManager>().InitializeTesting();
        _entities = IoCManager.Resolve<IEntityManager>();
        _maps = _entities.System<SharedMapSystem>();
    }

    [SetUp]
    public void SetupWindow()
    {
        _localEntity = null;
        _types.Clear();
        _types.Add(Explosion("MalinovTestExplosion"));
        _previews.Clear();
        _commands.Clear();

        var eui = new SpawnExplosionEui();
        _window = (SpawnExplosionWindow) typeof(SpawnExplosionEui).GetField("_window", InstanceMembers)!.GetValue(eui)!;

        // Keep the real window, entity/map systems and EUI message construction. Only replace the
        // external catalog, attached-player lookup and outbound network/console transports.
        var prototypes = new Mock<IPrototypeManager>(MockBehavior.Strict);
        prototypes.Setup(manager => manager.EnumeratePrototypes<ExplosionPrototype>()).Returns(_types);
        SetWindowDependency("_prototypeManager", prototypes.Object);
        var players = new Mock<IPlayerManager>(MockBehavior.Strict);
        players.SetupGet(manager => manager.LocalEntity).Returns(() => _localEntity);
        SetWindowDependency("_playerManager", players.Object);
        var console = new Mock<IClientConsoleHost>(MockBehavior.Strict);
        console.Setup(host => host.ExecuteCommand(It.IsAny<string>())).Callback<string>(_commands.Add);
        SetWindowDependency("_conHost", console.Object);
        var network = new Mock<IClientNetManager>(MockBehavior.Strict);
        network.Setup(manager => manager.ClientSendMessage(It.IsAny<NetMessage>())).Callback<NetMessage>(message =>
        {
            if (message is MsgEuiMessage { Message: SpawnExplosionEuiMsg.PreviewRequest preview })
                _previews.Add(preview);
        });
        typeof(BaseEui).GetField("_netManager", InstanceMembers)!.SetValue(eui, network.Object);
    }

    [TearDown]
    public void CleanupWindow()
    {
        _window.Close();
        foreach (var entity in _spawned.AsEnumerable().Reverse())
        {
            if (_entities.EntityExists(entity))
                _entities.DeleteEntity(entity);
        }
        _spawned.Clear();
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NoMapsDisablesActionsWithoutIndexingEmptyOptions(bool attachedInNullspace)
    {
        if (attachedInNullspace)
            _localEntity = SpawnEntity(MapCoordinates.Nullspace);

        _window.Open();
        AssertUnavailable();
        // A pending callback must also be safe even if the button has just been disabled.
        TogglePreview();
        Press(Spawn);
        Assert.That(_previews, Is.Empty);
        Assert.That(_commands, Is.Empty);
    }

    [Test]
    public void NullspaceFallsBackToAvailableMapAndPreservesExplosionArguments()
    {
        var map = CreateMap();
        _localEntity = SpawnEntity(MapCoordinates.Nullspace);
        _window.Open();
        AssertAvailable();
        _window.FindControl<FloatSpinBox>("MapX").Value = 12;
        _window.FindControl<FloatSpinBox>("MapY").Value = -4;
        _window.FindControl<FloatSpinBox>("Intensity").Value = 150;
        _window.FindControl<FloatSpinBox>("Slope").Value = 3;
        _window.FindControl<FloatSpinBox>("MaxIntensity").Value = 25;
        TogglePreview();

        var request = _previews.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.Epicenter, Is.EqualTo(new MapCoordinates(12, -4, map)));
            Assert.That(request.TypeId, Is.EqualTo("MalinovTestExplosion"));
            Assert.That(request.TotalIntensity, Is.EqualTo(150));
            Assert.That(request.IntensitySlope, Is.EqualTo(3));
            Assert.That(request.MaxIntensity, Is.EqualTo(25));
        });
        Press(Spawn);
        Assert.That(_commands, Is.EqualTo(new[] { $"explosion 150 3 25 12 -4 {map} MalinovTestExplosion" }));
        Assert.That(Preview.Pressed, Is.False);
    }

    [Test]
    public void MissingExplosionTypesDisablesActionsEvenWithAMap()
    {
        CreateMap();
        _types.Clear();
        _window.Open();
        AssertUnavailable();
        TogglePreview();
        Press(Spawn);
        Assert.That(_previews, Is.Empty);
        Assert.That(_commands, Is.Empty);
    }

    [Test]
    public void RecentreEnablesActionsWhenAMapBecomesAvailable()
    {
        _window.Open();
        AssertUnavailable();
        var map = CreateMap();
        Press(_window.FindControl<Button>("Recentre"));
        AssertAvailable();
        TogglePreview();
        Assert.That(_previews.Single().Epicenter.MapId, Is.EqualTo(map));
    }

    [Test]
    public void ReopeningPopulatesTypesBeforeLocationChangesRequestAPreview()
    {
        var map = CreateMap();
        _localEntity = SpawnEntity(new MapCoordinates(new Vector2(3, 4), map));
        _window.Open();
        TogglePreview();
        Assert.That(_previews.Single().TypeId, Is.EqualTo("MalinovTestExplosion"));
        _window.Close();

        _types.Clear();
        _types.Add(Explosion("MalinovReplacementExplosion"));
        _localEntity = SpawnEntity(new MapCoordinates(new Vector2(7, 8), map));
        _previews.Clear();
        _window.Open();
        var request = _previews.Single();
        Assert.Multiple(() =>
        {
            Assert.That(request.TypeId, Is.EqualTo("MalinovReplacementExplosion"));
            Assert.That(request.Epicenter, Is.EqualTo(new MapCoordinates(7, 8, map)));
        });
    }

    [Test]
    public void DeletedSelectedMapCannotPreviewOrSpawnAndRecentreRecovers()
    {
        var deletedMap = CreateMap();
        var deletedEntity = _spawned.Last();
        _window.Open();
        TogglePreview();
        Assert.That(_previews.Single().Epicenter.MapId, Is.EqualTo(deletedMap));
        var remainingMap = CreateMap();
        _entities.DeleteEntity(deletedEntity);
        _previews.Clear();

        // Both paths must revalidate the map, even while its old option is still visible.
        Press(_window.FindControl<FloatSpinBox>("Intensity").Children.OfType<Button>().Last());
        AssertUnavailable();
        Press(Spawn);
        Assert.That(_previews, Is.Empty);
        Assert.That(_commands, Is.Empty);

        Press(_window.FindControl<Button>("Recentre"));
        AssertAvailable();
        TogglePreview();
        Assert.That(_previews.Single().Epicenter.MapId, Is.EqualTo(remainingMap));
    }

    private CheckBox Preview => _window.FindControl<CheckBox>("Preview");
    private Button Spawn => _window.FindControl<Button>("Spawn");

    private void AssertUnavailable()
    {
        Assert.That(Spawn.Disabled, Is.True);
        Assert.That(Preview.Disabled, Is.True);
    }

    private void AssertAvailable()
    {
        Assert.That(Spawn.Disabled, Is.False);
        Assert.That(Preview.Disabled, Is.False);
    }

    private MapId CreateMap()
    {
        _spawned.Add(_maps.CreateMap(out var map));
        return map;
    }

    private EntityUid SpawnEntity(MapCoordinates coordinates)
    {
        var entity = _entities.SpawnEntity(null, coordinates);
        _spawned.Add(entity);
        return entity;
    }

    private static ExplosionPrototype Explosion(string id)
    {
        return IoCManager.Resolve<IPrototypeManager>().Index<ExplosionPrototype>(id);
    }

    private void SetWindowDependency(string name, object value)
    {
        typeof(SpawnExplosionWindow).GetField(name, InstanceMembers)!.SetValue(_window, value);
    }

    private static void Press(BaseButton button)
    {
        var press = (Action<BaseButton.ButtonEventArgs>) typeof(BaseButton).GetField("OnPressed", InstanceMembers)!.GetValue(button)!;
        press(new BaseButton.ButtonEventArgs(button, null!));
    }

    private void TogglePreview()
    {
        Preview.Pressed = true;
        var toggle = (Action<BaseButton.ButtonToggledEventArgs>) typeof(BaseButton).GetField("OnToggled", InstanceMembers)!.GetValue(Preview)!;
        toggle(new BaseButton.ButtonToggledEventArgs(true, Preview, null!));
    }
}
