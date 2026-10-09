using Robust.Shared.Prototypes;

namespace Content.Shared._MalinovStation.Lobby;

/// <summary>
/// A tile of the lobby screen: what it shows, how large it is, where it goes and when it is visible.
/// </summary>
[Prototype]
public sealed partial class MalinovLobbyTilePrototype : IPrototype
{
    /// <inheritdoc/>
    [IdDataField]
    public string ID { get; private set; } = default!;

    /// <summary>
    /// Title shown in the tile header; a tile without one has no header,
    /// for content that already carries its own heading.
    /// </summary>
    [DataField]
    public LocId? Title;

    /// <summary>
    /// Key of the client widget that builds the tile content.
    /// </summary>
    [DataField(required: true)]
    public string Widget = string.Empty;

    /// <summary>
    /// Where the tile goes on the default board: tiles are placed by this value, left to right and top to bottom.
    /// </summary>
    [DataField]
    public int Order;

    /// <summary>
    /// Size of the tile in board cells (columns, rows) until the player resizes it.
    /// </summary>
    [DataField]
    public Vector2i Size = new(1, 1);

    /// <summary>
    /// Smallest size the player may give the tile; without it the tile cannot shrink below <see cref="Size"/>.
    /// </summary>
    [DataField]
    public Vector2i? MinSize;

    /// <summary>
    /// Largest size the player may give the tile; without it the tile cannot grow beyond <see cref="Size"/>.
    /// </summary>
    [DataField]
    public Vector2i? MaxSize;

    /// <summary>
    /// Round phases in which the tile is shown.
    /// </summary>
    [DataField]
    public HashSet<MalinovLobbyPhase> Phases = new()
    {
        MalinovLobbyPhase.PreRound,
        MalinovLobbyPhase.Countdown,
        MalinovLobbyPhase.InRound,
    };

    /// <summary>
    /// Whether the player may hide the tile. Tiles the lobby cannot work without stay visible.
    /// </summary>
    [DataField]
    public bool Hideable = true;

    public Vector2i SmallestSize => MinSize ?? Size;

    public Vector2i LargestSize => MaxSize ?? Size;

    /// <summary>
    /// Whether the player may change the size of the tile.
    /// </summary>
    public bool Resizable => SmallestSize != LargestSize;
}
