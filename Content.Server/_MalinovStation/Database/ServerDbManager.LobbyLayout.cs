using System.Threading;
using System.Threading.Tasks;
using Content.Shared._MalinovStation.Lobby;
using Robust.Shared.Network;

// ReSharper disable once CheckNamespace
namespace Content.Server.Database;

public partial interface IServerDbManager
{
    /*
     * Malinov part: players' own lobby layouts.
     */

    /// <returns>The player's saved lobby layout, or <c>null</c> when they keep the default one.</returns>
    Task<MalinovLobbyLayout?> GetMalinovLobbyLayoutAsync(NetUserId userId, CancellationToken cancel = default);

    /// <summary>
    /// Saves the player's lobby layout; a default layout removes the saved one.
    /// </summary>
    Task SetMalinovLobbyLayoutAsync(NetUserId userId, MalinovLobbyLayout layout);
}

public sealed partial class ServerDbManager
{
    public Task<MalinovLobbyLayout?> GetMalinovLobbyLayoutAsync(NetUserId userId, CancellationToken cancel = default)
    {
        DbReadOpsMetric.Inc();
        return RunDbCommand(() => _db.GetMalinovLobbyLayoutAsync(userId, cancel));
    }

    public Task SetMalinovLobbyLayoutAsync(NetUserId userId, MalinovLobbyLayout layout)
    {
        DbWriteOpsMetric.Inc();
        return RunDbCommand(() => _db.SetMalinovLobbyLayoutAsync(userId, layout));
    }
}
