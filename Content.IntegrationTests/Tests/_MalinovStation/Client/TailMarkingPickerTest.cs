#nullable enable
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client.Humanoid;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Content.Client.Players.PlayTimeTracking;
using Content.IntegrationTests.Fixtures;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._MalinovStation.Client;

[TestFixture]
public sealed class TailMarkingPickerTest : GameTest
{
    private const string TailMarking = "DraconidTailSpikesAnimated";

    [TestCase("Draconid", true)]
    [TestCase("Human", false)]
    [TestCase("Reptilian", false)]
    [TestCase("MuHuman", false)]
    public async Task FreshProfileGroupsOnlyAvailableTailMarkingsUnderTorso(string species, bool hasTail)
    {
        MarkingPicker? picker = null;
        try
        {
            await Client.WaitPost(() =>
            {
                var manager = Client.ResolveDependency<MarkingManager>();
                var profile = HumanoidCharacterProfile.DefaultWithSpecies(species);
                var model = new MarkingsViewModel();
                picker = new MarkingPicker { SetSize = new Vector2(900, 600) };
                Client.ResolveDependency<IUserInterfaceManager>().StateRoot.AddChild(picker);
                picker.SetModel(model);
                model.OrganProfileData = manager.GetProfileData(species, profile.Sex,
                    profile.Appearance.SkinColor, profile.Appearance.EyeColor);
                model.OrganData = manager.GetMarkingData(species);
                model.Markings = profile.Appearance.Markings;
                model.ValidateMarkings();
            });

            await ChooseOption(Named<OptionButton>(picker!, "OrganSelector"), "markings-organ-Torso");
            await Client.WaitAssertion(() => AssertTorsoTabs(picker!, hasTail));

            if (hasTail)
            {
                await ChooseOption(Named<OptionButton>(picker!, "OrganSelector"), "markings-organ-group-arms");
                await Client.WaitAssertion(() =>
                {
                    var titles = TabTitles(VisibleOrganTabs(picker!));
                    Assert.That(titles, Does.Contain(Loc.GetString("markings-organ-ArmLeft")));
                    Assert.That(titles, Does.Contain(Loc.GetString("markings-organ-ArmRight")));
                    Assert.That(titles, Is.Unique, "Limb tabs must retain individual organ names.");
                });
            }
        }
        finally
        {
            await Client.WaitPost(() => picker?.Parent?.RemoveChild(picker));
        }
    }

    [Test]
    public async Task SpeciesDropdownRecreatesTailAndItsColorEditsTheTailOrgan()
    {
        HumanoidProfileEditor? editor = null;
        try
        {
            await Client.WaitPost(() =>
            {
                editor = new HumanoidProfileEditor(
                    Client.ResolveDependency<IClientPreferencesManager>(),
                    Client.CfgMan,
                    CEntMan,
                    Client.ResolveDependency<IFileDialogManager>(),
                    Client.ResolveDependency<ILogManager>(),
                    Client.ResolveDependency<Robust.Client.Player.IPlayerManager>(),
                    CProtoMan,
                    Client.ResolveDependency<IResourceCache>(),
                    Client.ResolveDependency<JobRequirementsManager>(),
                    Client.ResolveDependency<MarkingManager>())
                {
                    SetSize = new Vector2(1100, 650),
                };
                Client.ResolveDependency<IUserInterfaceManager>().StateRoot.AddChild(editor);
                editor.SetProfile(HumanoidCharacterProfile.DefaultWithSpecies("Human"), null);
            });

            var picker = Descendants<MarkingPicker>(editor!).Single();
            var speciesSelector = Named<OptionButton>(editor!, "SpeciesButton");
            foreach (var species in new[] { "Draconid", "Human", "Draconid" })
            {
                await Client.WaitPost(() => Named<TabContainer>(editor!, "TabContainer").CurrentTab = 0);
                await ChooseOption(speciesSelector, species == "Human" ? "species-name-human" : "species-name-draconid");
                await Client.WaitPost(() =>
                {
                    var tabs = Named<TabContainer>(editor!, "TabContainer");
                    tabs.CurrentTab = Enumerable.Range(0, tabs.ChildCount)
                        .Single(index => tabs.GetChild(index).Name == "MarkingsTab");
                });
                await ChooseOption(Named<OptionButton>(picker, "OrganSelector"), "markings-organ-Torso");
                await Client.WaitAssertion(() =>
                {
                    Assert.That(editor!.Profile!.Species.Id, Is.EqualTo(species));
                    AssertTorsoTabs(picker, species == "Draconid");
                });
            }

            Button selectedMarking = null!;
            await Client.WaitPost(() =>
            {
                var tabs = VisibleOrganTabs(picker);
                tabs.CurrentTab = Enumerable.Range(0, tabs.ChildCount)
                    .Single(index => tabs.GetActualTabTitle(index) == Loc.GetString("markings-layer-Tail"));
                var tail = (LayerMarkingPicker) tabs.GetChild(tabs.CurrentTab);
                selectedMarking = Descendants<Button>(Named<BoxContainer>(tail, "SelectedList"))
                    .Single(button => button.Text == Loc.GetString($"marking-{TailMarking}"));
            });
            await ClickControl(selectedMarking);

            var customColor = Color.FromHex("#3A7BCD");
            await Client.WaitPost(() =>
            {
                var tabs = VisibleOrganTabs(picker);
                var selector = Descendants<ColorSelectorSliders>(tabs.GetChild(tabs.CurrentTab)).First();
                var hexInput = Descendants<LineEdit>(selector).Single(input =>
                    input.Parent!.Children.OfType<Label>().Any(label => label.Text == Loc.GetString("color-selector-input-hex")));
                hexInput.Text = customColor.ToHex();
                hexInput.ForceSubmitText();
            });
            await Client.WaitAssertion(() =>
            {
                var markings = editor!.Profile!.Appearance.Markings;
                var tail = markings["Tail"][HumanoidVisualLayers.Tail].Single();
                Assert.That(tail.MarkingId.Id, Is.EqualTo(TailMarking));
                Assert.That(tail.MarkingColors[0], Is.EqualTo(customColor));
                Assert.That(markings["Torso"].Values.SelectMany(layer => layer).Any(marking => marking.MarkingId.Id == TailMarking),
                    Is.False, "Grouping the controls must not move the saved marking into the torso organ.");
            });

            // Reload the edited profile through the public editor API, as when reopening a saved character.
            await Client.WaitPost(() => editor!.SetProfile(editor.Profile, null));
            await ChooseOption(Named<OptionButton>(picker, "OrganSelector"), "markings-organ-Torso");
            await Client.WaitAssertion(() =>
            {
                AssertTorsoTabs(picker, true);
                Assert.That(editor!.Profile!.Appearance.Markings["Tail"][HumanoidVisualLayers.Tail].Single().MarkingColors[0],
                    Is.EqualTo(customColor));
            });
        }
        catch (Exception exception)
        {
            // Preserve the cause if GameTest's dirty-dispose warning replaces the original failure.
            TestContext.Progress.WriteLine(exception.ToString());
            throw;
        }
        finally
        {
            await Client.WaitPost(() => editor?.Parent?.RemoveChild(editor));
        }
    }

    private static void AssertTorsoTabs(MarkingPicker picker, bool hasTail)
    {
        var categories = Descendants<Button>(Named<OptionButton>(picker, "OrganSelector").OptionsScroll)
            .Select(button => button.Text).ToArray();
        Assert.That(categories.Count(title => title == Loc.GetString("markings-organ-Torso")), Is.EqualTo(1));
        Assert.That(categories, Does.Not.Contain(Loc.GetString("markings-organ-Tail")));

        var tabs = VisibleOrganTabs(picker);
        var titles = TabTitles(tabs);
        Assert.That(titles.Count(title => title == Loc.GetString("markings-layer-Tail")), Is.EqualTo(hasTail ? 1 : 0),
            $"Visible torso tabs: {string.Join(", ", titles)}");
        Assert.That(titles, Does.Contain(Loc.GetString("markings-layer-Chest")));
        Assert.That(titles, Is.Unique, "Torso tabs must name distinct layers instead of repeating the organ name.");
        foreach (var tab in tabs.Children)
            Assert.That(Descendants<LayerMarkingItem>(tab).Any(), Is.True, "Empty marking tabs should not be displayed.");

        if (!hasTail)
            return;

        var tailIndex = Enumerable.Range(0, tabs.ChildCount)
            .Single(index => tabs.GetActualTabTitle(index) == Loc.GetString("markings-layer-Tail"));
        var tailItem = Descendants<LayerMarkingItem>(tabs.GetChild(tailIndex))
            .Single(item => item.MarkingId.Id == TailMarking);
        Assert.That(Named<Button>(tailItem, "SelectButton").Pressed, Is.True);
    }

    private async Task ChooseOption(OptionButton selector, string labelKey)
    {
        await ClickControl(selector);
        Button option = null!;
        await Client.WaitPost(() => option = Descendants<Button>(selector.OptionsScroll)
            .Single(button => button.Text == Loc.GetString(labelKey)));
        await ClickControl(option);
    }

    private async Task ClickControl(Control control)
    {
        foreach (var state in new[] { BoundKeyState.Down, BoundKeyState.Up })
        {
            var screen = new ScreenCoordinates(control.GlobalPixelPosition + control.PixelSize / 2, control.Window?.Id ?? default);
            var args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, screen, default,
                screen.Position / control.UIScale - control.GlobalPosition, screen.Position - control.GlobalPixelPosition);
            await Client.DoGuiEvent(control, args);
            await Pair.RunTicksSync(1);
        }
    }

    private static TabContainer VisibleOrganTabs(MarkingPicker picker) =>
        Descendants<TabContainer>(Named<Control>(picker, "OrganPanel").Children.Single(child => child.Visible)).Single();

    private static string[] TabTitles(TabContainer tabs) =>
        Enumerable.Range(0, tabs.ChildCount).Select(tabs.GetActualTabTitle).ToArray();

    private static T Named<T>(Control root, string name) where T : Control =>
        Descendants<T>(root).Single(control => control.Name == name);

    private static IEnumerable<T> Descendants<T>(Control root) where T : Control
    {
        if (root is T match)
            yield return match;
        foreach (var child in root.Children)
        {
            foreach (var descendant in Descendants<T>(child))
                yield return descendant;
        }
    }
}
