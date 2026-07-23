namespace Content.Shared._MalinovStation.Audio;

/// <summary>
/// IDs of MalinovStation-specific soundCollection prototypes that get mixed in
/// alongside their vanilla counterpart by <see cref="MalinovSoundCollectionHelper"/>,
/// so custom tracks can ship in their own prototype file instead of the vanilla one.
/// </summary>
public static class MalinovSoundCollections
{
    public const string LobbyMusic = "LobbyMusicMalinov";
    public const string NukeMusic = "NukeMusicMalinov";
}
