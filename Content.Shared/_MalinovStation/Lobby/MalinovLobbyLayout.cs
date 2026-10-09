using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._MalinovStation.Lobby;

/// <summary>
/// A player's own arrangement of the lobby board. Tiles without a place were added after the player arranged
/// the board, so they go below the others.
/// </summary>
[Serializable, NetSerializable]
public sealed class MalinovLobbyLayout
{
    /// <summary>
    /// Where the player put each tile, for the whole board at once.
    /// </summary>
    public Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> Places { get; }

    /// <summary>
    /// Tiles the player hid. They keep their place, which stays empty.
    /// </summary>
    public List<ProtoId<MalinovLobbyTilePrototype>> Hidden { get; }

    /// <summary>
    /// The player has not changed anything, so the default layout applies.
    /// </summary>
    public bool IsDefault => Places.Count == 0 && Hidden.Count == 0;

    public MalinovLobbyLayout() : this(new(), new())
    {
    }

    public MalinovLobbyLayout(
        Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement> places,
        List<ProtoId<MalinovLobbyTilePrototype>> hidden)
    {
        Places = places;
        Hidden = hidden;
    }
}

/// <summary>
/// Sent by the server once the player's account data is loaded: their saved lobby layout.
/// </summary>
[Serializable, NetSerializable]
public sealed class MalinovLobbyLayoutEvent : EntityEventArgs
{
    public MalinovLobbyLayout Layout { get; }

    public MalinovLobbyLayoutEvent(MalinovLobbyLayout layout)
    {
        Layout = layout;
    }
}

/// <summary>
/// Sent by the client when the player changes their lobby layout. A default layout resets it.
/// </summary>
[Serializable, NetSerializable]
public sealed class MalinovLobbyLayoutChangedEvent : EntityEventArgs
{
    public MalinovLobbyLayout Layout { get; }

    /// <summary>
    /// Grows with every change the client sends. Changes sent within one client tick reach the server
    /// in no particular order, so the server keeps only the newest one.
    /// </summary>
    public uint Version { get; }

    public MalinovLobbyLayoutChangedEvent(MalinovLobbyLayout layout, uint version)
    {
        Layout = layout;
        Version = version;
    }
}
