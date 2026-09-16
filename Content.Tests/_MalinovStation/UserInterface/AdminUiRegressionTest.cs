#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text;
using Content.Client._MalinovStation.ViewVariables;
using NUnit.Framework;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.CustomControls;
using Robust.Client.UserInterface.XAML.Proxy;
using Robust.Client.ViewVariables;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.Tests._MalinovStation.UserInterface;

[TestFixture]
public sealed class AdminUiRegressionTest : RobustUnitTest
{
    public override UnitTestProject Project => UnitTestProject.Client;
    protected override Type[] ExtraComponents => new[] { typeof(AppearanceComponent) };
    private const BindingFlags InstanceMembers = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;

    protected override void OverrideIoC()
    {
        base.OverrideIoC();
        // Match the integration client's compiled-XAML setup; unit tests do not initialize hot reload.
        var proxyInterface = typeof(XamlProxyManagerStub).Assembly.GetType("Robust.Client.UserInterface.XAML.Proxy.IXamlProxyManager")!;
        IoCManager.Instance!.Register(proxyInterface, typeof(XamlProxyManagerStub), overwrite: true);
    }

    [OneTimeSetUp]
    public void SetupUi()
    {
        IoCManager.Resolve<IUserInterfaceManager>().InitializeTesting();
        var controller = new MalinovViewVariablesUIController();
        IoCManager.InjectDependencies(controller);
        controller.Initialize();
    }

    [Test]
    public void ViewVariablesTabsKeepOneHeaderRowInNarrowScrollContainer()
    {
        using var scroll = new ScrollContainer();
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        scroll.AddChild(box);
        var tabs = CreateTabs();
        var panel = new RecordingStyleBox();
        tabs.PanelStyleBoxOverride = panel;
        var wrapper = new MalinovViewVariablesTabs(tabs);
        box.AddChild(wrapper);

        scroll.Measure(new Vector2(180, 20));
        scroll.Arrange(new UIBox2(0, 0, 180, 20));
        tabs.DrawForTest();
        Assert.Multiple(() =>
        {
            Assert.That(tabs.MinWidth, Is.GreaterThan(660));
            Assert.That(tabs.Width, Is.GreaterThanOrEqualTo(tabs.MinWidth));
            Assert.That(tabs.GetChild(0).Position.Y, Is.EqualTo(20));
            Assert.That(panel.LastBox.Top, Is.EqualTo(20));
            Assert.That(panel.LastBox.Bottom, Is.GreaterThanOrEqualTo(panel.LastBox.Top));
            Assert.That(wrapper.DesiredSize.X, Is.GreaterThan(scroll.Width), "The existing scroll container must see the full row width.");
        });
    }

    [Test]
    public void ViewVariablesTabsRecomputeHeaderForThemeAndScaleChanges()
    {
        var tabs = CreateTabs();
        using var wrapper = new MalinovViewVariablesTabs(tabs);
        var unconstrained = new Vector2(float.PositiveInfinity);
        wrapper.Measure(unconstrained);
        var initialWidth = tabs.MinWidth;

        var inactive = new StyleBoxEmpty();
        inactive.SetContentMarginOverride(StyleBox.Margin.Horizontal, 9);
        tabs.Stylesheet = TabStyle(new TestFont(20, 30), inactive);
        tabs.TestScale = 1.25f;
        wrapper.InvalidateMeasure();
        wrapper.Measure(unconstrained);
        Assert.Multiple(() =>
        {
            Assert.That(tabs.MinWidth, Is.GreaterThan(initialWidth));
            Assert.That(tabs.MinHeight, Is.EqualTo(30));
        });
        wrapper.Arrange(new UIBox2(0, 0, 180, 20));
        tabs.DrawForTest();
        Assert.That(tabs.GetChild(0).Position.Y, Is.EqualTo(29.6f).Within(0.01f));

        tabs.Stylesheet = TabStyle(new TestFont());
        tabs.TestScale = 1;
        wrapper.InvalidateMeasure();
        wrapper.Measure(unconstrained);
        Assert.That(tabs.MinWidth, Is.EqualTo(initialWidth), "Smaller fonts must release the extra width.");
    }

    [Test]
    public void ControllerWrapsVvEntityTabsOnceAndLeavesOtherWindowsAlone()
    {
        using var vvWindow = new DefaultWindow { Title = Loc.GetString("view-variables") };
        var vvBox = AddEntityTabLayout(vvWindow);
        vvWindow.Open();
        Assert.That(vvBox.GetChild(0), Is.TypeOf<MalinovViewVariablesTabs>());
        vvWindow.Close();
        vvWindow.Open();
        Assert.That(vvBox.GetChild(0).GetChild(0), Is.TypeOf<TestTabs>());
        vvWindow.Close();

        using var otherWindow = new DefaultWindow { Title = "Other window" };
        var otherBox = AddEntityTabLayout(otherWindow);
        otherWindow.Open();
        Assert.That(otherBox.GetChild(0), Is.TypeOf<TestTabs>());
        otherWindow.Close();
    }

    [Test]
    public void ControllerWrapsTabsPopulatedAfterRemoteWindowOpens()
    {
        using var window = new DefaultWindow { Title = Loc.GetString("view-variables") };
        window.Open();
        var box = AddEntityTabLayout(window);
        Assert.That(box.GetChild(0), Is.TypeOf<MalinovViewVariablesTabs>());
        window.Close();

        using var closedWindow = new DefaultWindow { Title = Loc.GetString("view-variables") };
        closedWindow.Open();
        closedWindow.Close();
        var closedBox = AddEntityTabLayout(closedWindow);
        Assert.That(closedBox.GetChild(0), Is.TypeOf<TestTabs>(), "A closed window must no longer have a pending watcher.");
        closedWindow.Open();
        Assert.That(closedBox.GetChild(0), Is.TypeOf<MalinovViewVariablesTabs>());
        closedWindow.Close();
    }

    [Test]
    public void EntityInspectorProtectsRequiredComponentsAcrossListRefreshes()
    {
        var entities = IoCManager.Resolve<IEntityManager>();
        var ui = IoCManager.Resolve<IUserInterfaceManager>();
        var entity = entities.SpawnEntity(null, MapCoordinates.Nullspace);
        entities.AddComponent<AppearanceComponent>(entity);
        var previousWindows = ui.WindowRoot.Children.ToHashSet();
        DefaultWindow? window = null;
        try
        {
            IoCManager.Resolve<IClientViewVariablesManager>().OpenVV(entities.GetNetEntity(entity));
            window = ui.WindowRoot.Children.OfType<DefaultWindow>().Single(child => !previousWindows.Contains(child));
            var tabs = Descendants(window).OfType<TabContainer>().Single();
            var components = tabs.GetChild(1);
            var initialRemove = RemovalButton<MetaDataComponent>(components);
            AssertProtectedComponents(components);
            Assert.That(RemovalButton<AppearanceComponent>(components).Disabled, Is.False);

            tabs.CurrentTab = 1;
            Assert.That(RemovalButton<MetaDataComponent>(components), Is.Not.SameAs(initialRemove));
            AssertProtectedComponents(components);

            // Invoke the ordinary row action; the guard must preserve removal of optional components.
            var remove = RemovalButton<AppearanceComponent>(components);
            var press = (Action<BaseButton.ButtonEventArgs>) typeof(BaseButton)
                .GetField("OnPressed", InstanceMembers)!.GetValue(remove)!;
            press(new BaseButton.ButtonEventArgs(remove, null!));
            Assert.That(entities.HasComponent<AppearanceComponent>(entity), Is.False);
            AssertProtectedComponents(components);
        }
        finally
        {
            window?.Close();
            window?.Dispose();
            entities.DeleteEntity(entity);
        }
    }

    [Test]
    public void ProtectedComponentGuardHandlesLatePopulationAndStopsWhenWindowCloses()
    {
        using var window = new DefaultWindow { Title = Loc.GetString("view-variables") };
        window.Open();
        var scroll = new ScrollContainer();
        var box = new BoxContainer();
        var tabs = new TabContainer();
        window.Contents.AddChild(scroll);
        scroll.AddChild(box);
        box.AddChild(tabs);
        tabs.AddChild(new Control());
        var components = new BoxContainer();
        tabs.AddChild(components);
        tabs.SetTabTitle(1, Loc.GetString("view-variable-instance-entity-client-components-tab-title"));
        var metadata = AddComponentRow<MetaDataComponent>(components);
        var transform = AddComponentRow<TransformComponent>(components);
        var appearance = AddComponentRow<AppearanceComponent>(components);
        Assert.Multiple(() =>
        {
            Assert.That(metadata.Disabled, Is.True);
            Assert.That(transform.Disabled, Is.True);
            Assert.That(appearance.Disabled, Is.False);
        });

        tabs.AddChild(new Control());
        var serverComponents = new BoxContainer();
        tabs.AddChild(serverComponents);
        tabs.SetTabTitle(3, Loc.GetString("view-variable-instance-entity-server-components-tab-title"));
        Assert.That(AddComponentRow<MetaDataComponent>(serverComponents).Disabled, Is.False);

        window.Close();
        components.RemoveAllChildren();
        var afterClose = AddComponentRow<MetaDataComponent>(components);
        Assert.That(afterClose.Disabled, Is.False, "Window removal must detach component-list watchers.");
        window.Open();
        Assert.That(afterClose.Disabled, Is.True, "Reopening must guard rows already in the window.");
        window.Close();
    }

    [Test]
    public void FactorySelectsContentEnumEditorWithoutReplacingOtherEditors()
    {
        var factory = IoCManager.Resolve<IViewVariableControlFactory>();
        Assert.Multiple(() =>
        {
            Assert.That(factory.CreateFor(typeof(UnsignedFlags)), Is.TypeOf<MalinovEnumPropEditor>());
            Assert.That(factory.CreateFor(typeof(int)), Is.Not.TypeOf<MalinovEnumPropEditor>());
        });
    }

    [TestCaseSource(nameof(EnumValues))]
    public void EnumEditorPreservesEveryUnderlyingType(Enum value)
    {
        var editor = IoCManager.Resolve<IViewVariableControlFactory>().CreateFor(value.GetType());
        using var control = editor.Initialize(value, false);
        var option = (OptionButton) control.GetChild(0);
        object? changed = null;
        Action<object?, bool> callback = (result, _) => changed = result;
        typeof(VVPropEditor).GetEvent("OnValueChanged", InstanceMembers)!.GetAddMethod(true)!.Invoke(editor, new object[] { callback });

        // Exercise the same selection callback as the popup, including item 0.
        var select = (Action<OptionButton.ItemSelectedEventArgs>) typeof(OptionButton)
            .GetField("OnItemSelected", InstanceMembers)!.GetValue(option)!;
        select(new OptionButton.ItemSelectedEventArgs(option.SelectedId, option));

        var underlying = Enum.GetUnderlyingType(value.GetType());
        Assert.Multiple(() =>
        {
            Assert.That(changed, Is.TypeOf(underlying));
            Assert.That(changed, Is.EqualTo(Convert.ChangeType(value, underlying)));
        });
    }

    [Test]
    public void EnumEditorTogglesUnsignedHighBitWithoutOverflow()
    {
        var editor = IoCManager.Resolve<IViewVariableControlFactory>().CreateFor(typeof(UnsignedFlags));
        using var control = editor.Initialize(UnsignedFlags.Low, false);
        object? changed = null;
        Action<object?, bool> callback = (result, _) => changed = result;
        typeof(VVPropEditor).GetEvent("OnValueChanged", InstanceMembers)!.GetAddMethod(true)!.Invoke(editor, new object[] { callback });
        var button = control.Children.OfType<Button>().Single(b => b.Text == nameof(UnsignedFlags.High));
        var toggle = (Action<BaseButton.ButtonToggledEventArgs>) typeof(BaseButton)
            .GetField("OnToggled", InstanceMembers)!.GetValue(button)!;

        toggle(new BaseButton.ButtonToggledEventArgs(true, button, null!));
        Assert.That(changed, Is.EqualTo((1UL << 63) | 1));
        toggle(new BaseButton.ButtonToggledEventArgs(false, button, null!));
        Assert.That(changed, Is.EqualTo(1UL));
    }

    [Test]
    public void ReadOnlyEnumEditorDisablesFlagsAndSelection()
    {
        var editor = IoCManager.Resolve<IViewVariableControlFactory>().CreateFor(typeof(UnsignedFlags));
        using var control = editor.Initialize(UnsignedFlags.High, true);
        Assert.That(control.Children.OfType<BaseButton>().All(button => button.Disabled), Is.True);
    }

    private static BoxContainer AddEntityTabLayout(DefaultWindow window)
    {
        var scroll = new ScrollContainer();
        var box = new BoxContainer { Orientation = BoxContainer.LayoutOrientation.Vertical };
        window.Contents.AddChild(scroll);
        scroll.AddChild(box);
        box.AddChild(CreateTabs());
        return box;
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }

    private static TextureButton RemovalButton<T>(Control list)
    {
        var name = PrettyPrint.PrintUserFacingTypeShort(typeof(T), 2);
        var row = list.Children.OfType<Button>().Single(button => button.Text == name);
        return row.Children.OfType<TextureButton>().Single();
    }

    private static void AssertProtectedComponents(Control list)
    {
        Assert.Multiple(() =>
        {
            Assert.That(RemovalButton<MetaDataComponent>(list).Disabled, Is.True);
            Assert.That(RemovalButton<TransformComponent>(list).Disabled, Is.True);
        });
    }

    private static TextureButton AddComponentRow<T>(Control list)
    {
        var row = new Button { Text = PrettyPrint.PrintUserFacingTypeShort(typeof(T), 2) };
        var remove = new TextureButton { StyleClasses = { DefaultWindow.StyleClassWindowCloseButton } };
        row.AddChild(remove);
        list.AddChild(row);
        return remove;
    }

    private static TestTabs CreateTabs()
    {
        var tabs = new TestTabs { Stylesheet = TabStyle(new TestFont()) };
        foreach (var title in new[] { "Client Variables", "Client Components", "Server Variables", "Server Components" })
        {
            tabs.AddChild(new ScaledControl());
            tabs.SetTabTitle(tabs.ChildCount - 1, title);
        }
        return tabs;
    }

    private static Stylesheet TabStyle(Font font, StyleBox? inactive = null)
    {
        return new Stylesheet(new[]
        {
            new StyleRule(new SelectorElement(typeof(TabContainer), null, null, null), new[]
            {
                new StyleProperty("font", font),
                new StyleProperty(TabContainer.StylePropertyTabStyleBoxInactive, inactive ?? new StyleBoxEmpty()),
            }),
        });
    }

    private static IEnumerable<Enum> EnumValues()
    {
        yield return ByteValue.High;
        yield return SByteValue.Low;
        yield return ShortValue.Low;
        yield return UShortValue.High;
        yield return IntValue.Low;
        yield return UIntValue.High;
        yield return LongValue.Low;
        yield return ULongValue.High;
    }

    private enum ByteValue : byte { High = byte.MaxValue }
    private enum SByteValue : sbyte { Low = sbyte.MinValue }
    private enum ShortValue : short { Low = short.MinValue }
    private enum UShortValue : ushort { High = ushort.MaxValue }
    private enum IntValue { Low = int.MinValue }
    private enum UIntValue : uint { High = uint.MaxValue }
    private enum LongValue : long { Low = long.MinValue }
    private enum ULongValue : ulong { High = ulong.MaxValue }
    [Flags] private enum UnsignedFlags : ulong { Low = 1, High = 1UL << 63 }

    private sealed class TestTabs : TabContainer
    {
        public float TestScale = 1;
        public override float UIScale => TestScale;
        public void DrawForTest() => Draw((DrawingHandleScreen)null!);
    }

    private sealed class ScaledControl : Control
    {
        public override float UIScale => Parent?.UIScale ?? 1;
    }

    private sealed class RecordingStyleBox : StyleBox
    {
        public UIBox2 LastBox;
        protected override void DoDraw(DrawingHandleScreen handle, UIBox2 box, float uiScale) => LastBox = box;
    }

    private sealed class TestFont(int advance = 10, int height = 20) : Font
    {
        public override int GetAscent(float scale) => (int)(height * scale);
        public override int GetHeight(float scale) => (int)(height * scale);
        public override int GetDescent(float scale) => 0;
        public override int GetLineHeight(float scale) => (int)(height * scale);
        public override float DrawChar(DrawingHandleBase handle, Rune rune, Vector2 baseline, float scale, Color color, bool fallback = true) => advance * scale;
        public override float DrawCharOutline(DrawingHandleBase handle, Rune rune, Vector2 baseline, float scale, TextOutline outline, bool fallback = true) => advance * scale;
        public override CharMetrics? GetCharMetrics(Rune rune, float scale, bool fallback = true) => new CharMetrics(0, 0, (int)(advance * scale), (int)(advance * scale), (int)(height * scale));
    }
}
