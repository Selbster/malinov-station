using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Shared._MalinovStation.Lobby;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

// ReSharper disable once CheckNamespace
namespace Content.Server.Database;

public abstract partial class ServerDbBase
{
    /*
     * Malinov part: players' own lobby layouts (MalinovPlayerLobbyLayout and its MalinovPlayerLobbyTile places).
     */

    /// <returns>The player's saved layout, or <c>null</c> when they keep the default one.</returns>
    public async Task<MalinovLobbyLayout?> GetMalinovLobbyLayoutAsync(NetUserId userId, CancellationToken cancel)
    {
        await using var db = await GetDb(cancel);
        var row = await db.DbContext.MalinovPlayerLobbyLayout
            .Include(layout => layout.Tiles)
            .AsNoTracking()
            .SingleOrDefaultAsync(layout => layout.UserId == userId.UserId, cancel);

        if (row == null)
            return null;

        var places = new Dictionary<ProtoId<MalinovLobbyTilePrototype>, MalinovLobbyTilePlacement>(row.Tiles.Count);
        foreach (var tile in row.Tiles)
        {
            places[tile.TileId] = new MalinovLobbyTilePlacement(tile.Column, tile.Row, tile.Width, tile.Height);
        }

        return new MalinovLobbyLayout(
            places,
            row.HiddenTiles.Select(id => new ProtoId<MalinovLobbyTilePrototype>(id)).ToList());
    }

    /// <summary>
    /// Saves the player's layout; a default layout removes the saved one.
    /// </summary>
    public async Task SetMalinovLobbyLayoutAsync(NetUserId userId, MalinovLobbyLayout layout)
    {
        await using var db = await GetDb();
        var row = await db.DbContext.MalinovPlayerLobbyLayout
            .Include(saved => saved.Tiles)
            .SingleOrDefaultAsync(saved => saved.UserId == userId.UserId);

        if (layout.IsDefault)
        {
            if (row == null)
                return;

            // The places of the tiles go with it.
            db.DbContext.MalinovPlayerLobbyLayout.Remove(row);
            await db.DbContext.SaveChangesAsync();
            return;
        }

        if (row == null)
        {
            row = new MalinovPlayerLobbyLayout { UserId = userId.UserId };
            db.DbContext.MalinovPlayerLobbyLayout.Add(row);
        }

        // A place is keyed by its tile, so the rows of tiles that stay are changed where they are
        // instead of being removed and added again with the same key.
        row.Tiles.RemoveAll(tile => !layout.Places.ContainsKey(tile.TileId));
        foreach (var (id, placement) in layout.Places)
        {
            var tile = row.Tiles.Find(saved => saved.TileId == id.Id);
            if (tile == null)
            {
                tile = new MalinovPlayerLobbyTile { TileId = id.Id };
                row.Tiles.Add(tile);
            }

            tile.Column = placement.Column;
            tile.Row = placement.Row;
            tile.Width = placement.Width;
            tile.Height = placement.Height;
        }

        row.HiddenTiles = layout.Hidden.Select(id => id.Id).ToList();
        await db.DbContext.SaveChangesAsync();
    }
}
