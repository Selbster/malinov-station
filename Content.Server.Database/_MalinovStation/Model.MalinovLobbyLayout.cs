using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using Microsoft.EntityFrameworkCore;

namespace Content.Server.Database;

/// <summary>
/// A player's own arrangement of the lobby board. One row per account; without a row the default layout applies.
/// </summary>
public class MalinovPlayerLobbyLayout
{
    [Key]
    public Guid UserId { get; set; }

    /// <summary>
    /// Where the player put each lobby tile.
    /// </summary>
    public List<MalinovPlayerLobbyTile> Tiles { get; set; } = new();

    /// <summary>
    /// Ids of the lobby tiles the player hid.
    /// </summary>
    public List<string> HiddenTiles { get; set; } = new();
}

/// <summary>
/// Where a player put one lobby tile: its top left cell on the board and its size in cells.
/// </summary>
/// <remarks>
/// Found by EF through <see cref="MalinovPlayerLobbyLayout.Tiles"/>, like the jobs of a profile, so it needs no DbSet.
/// The cell columns are not named column and row, as both are SQL keywords.
/// </remarks>
[PrimaryKey(nameof(LayoutUserId), nameof(TileId))]
public class MalinovPlayerLobbyTile
{
    public Guid LayoutUserId { get; set; }

    [ForeignKey(nameof(LayoutUserId))]
    public MalinovPlayerLobbyLayout Layout { get; set; } = null!;

    public string TileId { get; set; } = null!;

    [Column("board_column")]
    public int Column { get; set; }

    [Column("board_row")]
    public int Row { get; set; }

    public int Width { get; set; }

    public int Height { get; set; }
}
