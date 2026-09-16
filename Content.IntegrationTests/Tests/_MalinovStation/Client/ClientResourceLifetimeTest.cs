using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Content.Client.Audio.Midi;
using Content.Client.Instruments;
using Content.Client.Instruments.UI;
using Content.Client.Light;
using Content.IntegrationTests.Fixtures;
using Content.Shared.CCVar;
using Robust.Client.Graphics;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._MalinovStation.Client;

[TestFixture]
public sealed class ClientResourceLifetimeTest : GameTest
{
    [Test]
    public async Task DisabledAmbientOcclusionCanBeCollected()
    {
        var previous = Client.CfgMan.GetCVar(CCVars.AmbientOcclusion);
        var overlays = new List<WeakReference<AmbientOcclusionOverlay>>();
        try
        {
            await Client.WaitPost(() =>
            {
                Client.CfgMan.SetCVar(CCVars.AmbientOcclusion, false);
                for (var i = 0; i < 3; i++)
                    overlays.Add(CreateRemovedOverlay());
            });

            await Client.WaitAssertion(() =>
            {
                GC.Collect();
                GC.WaitForPendingFinalizers();
                GC.Collect();
                foreach (var overlay in overlays)
                    Assert.That(overlay.TryGetTarget(out _), Is.False,
                        "A disabled overlay must not remain rooted by configuration or viewport callbacks.");
            });
        }
        finally
        {
            await Client.WaitPost(() => Client.CfgMan.SetCVar(CCVars.AmbientOcclusion, previous));
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private WeakReference<AmbientOcclusionOverlay> CreateRemovedOverlay()
    {
        Client.CfgMan.SetCVar(CCVars.AmbientOcclusion, true);
        var overlay = Client.ResolveDependency<IOverlayManager>().GetOverlay<AmbientOcclusionOverlay>();
        var result = new WeakReference<AmbientOcclusionOverlay>(overlay);
        Client.CfgMan.SetCVar(CCVars.AmbientOcclusion, false);
        return result;
    }

    [Test]
    public async Task MidiControlsStopObservingTheLibraryWhenClosed()
    {
        FileMidiSource source = null!;
        MidiCollectionUtilsControl utility = null!;
        var library = Client.ResolveDependency<MidiFileCollectionManager>();
        var resources = Client.ResolveDependency<IResourceManager>();
        var ui = Client.ResolveDependency<IUserInterfaceManager>();
        var path = new ResPath("MalinovLifetimeTest.mid");
        var initialCount = 0;
        var initialLabel = string.Empty;

        try
        {
            await Client.WaitPost(() =>
            {
                source = new FileMidiSource { Instrument = (EntityUid.Invalid, new InstrumentComponent()) };
                utility = new MidiCollectionUtilsControl();
                ui.StateRoot.AddChild(source);
                ui.StateRoot.AddChild(utility);
                initialCount = source.FindControl<ItemList>("TrackList").Count;
                initialLabel = utility.FindControl<Label>("MidiCountLabel").Text;
                ui.StateRoot.RemoveChild(source);
                ui.StateRoot.RemoveChild(utility);

                // No playback is requested; only library notifications and control lifetime are exercised.
                using (resources.UserData.OpenWrite(MidiFileCollectionManager.UserMidiDirectory / path))
                {
                }
                library.ReloadLibrary();
            });

            await Client.WaitAssertion(() =>
            {
                Assert.That(source.FindControl<ItemList>("TrackList").Count, Is.EqualTo(initialCount));
                Assert.That(utility.FindControl<Label>("MidiCountLabel").Text, Is.EqualTo(initialLabel));
            });

            await Client.WaitPost(() =>
            {
                ui.StateRoot.AddChild(source);
                ui.StateRoot.AddChild(utility);
            });
            await Client.WaitAssertion(() =>
            {
                Assert.That(source.FindControl<ItemList>("TrackList").Count, Is.EqualTo(initialCount + 1));
                Assert.That(utility.FindControl<Label>("MidiCountLabel").Text, Is.Not.EqualTo(initialLabel));
            });

            await Client.WaitPost(() =>
            {
                source.FindControl<LineEdit>("FilterBar").SetText("no-matching-midi-track-5b0324", invokeEvent: true);
            });
            await Client.WaitAssertion(() =>
            {
                var shuffle = source.FindControl<Button>("ShuffleButton");
                Assert.That(source.FindControl<ItemList>("TrackList").Count, Is.Zero);
                Assert.That(shuffle.Disabled, Is.True);
            });

            Exception shuffleError = null;
            await Client.WaitPost(() =>
            {
                // A pending click must also be safe if the library emptied after input was queued.
                var shuffle = source.FindControl<Button>("ShuffleButton");
                shuffle.Pressed = true;
                var handler = typeof(FileMidiSource).GetMethod("OnShuffleButtonPressed",
                    BindingFlags.Instance | BindingFlags.NonPublic)!;
                try
                {
                    handler.Invoke(source, [new BaseButton.ButtonEventArgs(shuffle, null!)]);
                }
                catch (Exception e)
                {
                    shuffleError = e;
                }
            });
            Assert.That(shuffleError, Is.Null);
        }
        finally
        {
            await Client.WaitPost(() =>
            {
                source?.Parent?.RemoveChild(source);
                utility?.Parent?.RemoveChild(utility);
                library.RemoveMidiFile(path);
            });
        }
    }
}
