#pragma warning disable IDE0130
namespace Content.Client.Instruments.UI;

public sealed partial class FileMidiSource
{
    protected override void EnteredTree()
    {
        base.EnteredTree();
        _midiCollection.MidiFileAdded += AddTrack;
        _midiCollection.MidiFileRemoved += RemoveTrack;
        _midiCollection.MidiFilesReset += ReloadTrackList;
        ReloadTrackList();
    }

    protected override void ExitedTree()
    {
        _midiCollection.MidiFileAdded -= AddTrack;
        _midiCollection.MidiFileRemoved -= RemoveTrack;
        _midiCollection.MidiFilesReset -= ReloadTrackList;
        base.ExitedTree();
    }

    private void UpdateShuffleAvailability()
    {
        ShuffleButton.Disabled = TrackList.Count == 0;
        if (ShuffleButton.Disabled)
            ShuffleButton.Pressed = false;
    }
}
