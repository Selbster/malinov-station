using Robust.Client.Audio.Midi;

namespace Content.Client._MalinovStation.Entry;

/// <summary>
/// Closes native MIDI players while the engine's FluidSynth settings are still alive.
/// </summary>
public static class MalinovMidiShutdown
{
    public static void ClosePlayers(IMidiManager manager)
    {
        // Renderers returns a snapshot without initializing MIDI on clients that never used it.
        foreach (var renderer in manager.Renderers)
        {
            if (renderer.Disposed)
                continue;

            // Close through the same synchronized APIs used by the instrument UI. Do not dispose the
            // renderer: instrument component shutdown still needs its synth and sequencer afterwards.
            switch (renderer.Status)
            {
                case MidiRendererStatus.File:
                    renderer.CloseMidi();
                    break;
                case MidiRendererStatus.Input:
                    renderer.CloseInput();
                    break;
            }
        }
    }
}
