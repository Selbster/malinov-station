#pragma warning disable IDE0130
namespace Content.Client.Instruments.UI;

public sealed partial class InstrumentBoundUserInterface
{
    private void UnsubscribeInstrumentEvents()
    {
        _instruments.OnChannelsUpdated -= OnChannelsUpdated;
        _channelsControl.ChannelsUpdateRequest -= OnChannelsUpdateRequest;
        _channelsControl.SwitchFilteredChannel -= OnSwitchFilteredChannel;
        _minVolumeControl.MinVolumeChanged -= OnMinVolumeChanged;

        if (_fileSource.Instrument.Comp is { } instrument)
            instrument.OnMidiPlaybackEnded -= OnMidiPlaybackEnded;
    }
}
