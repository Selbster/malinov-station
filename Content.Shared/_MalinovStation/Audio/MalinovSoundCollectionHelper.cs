using System.Collections.Generic;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Shared._MalinovStation.Audio;

/// <summary>
/// The engine's soundCollection prototype is a single object holding one flat file list,
/// so a custom track can't just be dropped into a second prototype file sharing the same ID -
/// it would either fail to load as a duplicate or silently overwrite the vanilla one.
/// This lets a MalinovStation-specific collection be kept in its own prototype file while still
/// being picked from alongside the vanilla collection at runtime.
/// </summary>
public static class MalinovSoundCollectionHelper
{
    /// <summary>
    /// Returns every file from <paramref name="vanilla"/> plus, if it exists, every file from
    /// the MalinovStation collection <paramref name="customCollectionId"/>.
    /// </summary>
    public static List<ResPath> GetMergedFiles(
        IPrototypeManager protoMan,
        SoundCollectionPrototype vanilla,
        string customCollectionId)
    {
        var files = new List<ResPath>(vanilla.PickFiles);

        if (protoMan.TryIndex<SoundCollectionPrototype>(customCollectionId, out var custom))
            files.AddRange(custom.PickFiles);

        return files;
    }

    /// <summary>
    /// Resolves <paramref name="specifier"/> like <see cref="SharedAudioSystem.ResolveSound"/> would,
    /// except that if it points at a soundCollection, the MalinovStation collection
    /// <paramref name="customCollectionId"/> (if it exists) is mixed into the pool it picks from.
    /// </summary>
    public static ResolvedSoundSpecifier ResolveWithCustomCollection(
        SharedAudioSystem audio,
        IPrototypeManager protoMan,
        IRobustRandom random,
        SoundSpecifier specifier,
        string customCollectionId)
    {
        if (specifier is not SoundCollectionSpecifier { Collection: { } vanillaId } ||
            !protoMan.TryIndex<SoundCollectionPrototype>(vanillaId, out var vanilla))
        {
            return audio.ResolveSound(specifier);
        }

        var files = GetMergedFiles(protoMan, vanilla, customCollectionId);
        if (files.Count == 0)
            return audio.ResolveSound(specifier);

        ResolvedSoundSpecifier resolved = random.Pick(files);
        return resolved;
    }
}
