using Robust.Shared.Utility;

#pragma warning disable IDE0130
namespace Content.Client.Instruments.UI;

public sealed partial class MidiCollectionUtilsControl
{
    protected override void EnteredTree()
    {
        base.EnteredTree();
        _midiCollection.MidiFileAdded += OnMidiFileChanged;
        _midiCollection.MidiFileRemoved += OnMidiFileChanged;
        _midiCollection.MidiFilesReset += UpdateCount;
        UpdateCount();
    }

    protected override void ExitedTree()
    {
        _midiCollection.MidiFileAdded -= OnMidiFileChanged;
        _midiCollection.MidiFileRemoved -= OnMidiFileChanged;
        _midiCollection.MidiFilesReset -= UpdateCount;
        base.ExitedTree();
    }

    private void OnMidiFileChanged(ResPath path)
    {
        UpdateCount();
    }
}
