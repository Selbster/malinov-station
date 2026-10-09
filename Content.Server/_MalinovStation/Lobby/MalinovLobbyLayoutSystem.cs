using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Shared._MalinovStation.Lobby;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.Lobby;

/// <summary>
/// Keeps each player's own lobby layout: loads it from the database when they connect, sends it to their client
/// and saves the changes their client sends, after checking them.
/// </summary>
/// <remarks>
/// Saves of one player are at least <see cref="SaveInterval"/> apart; changes arriving sooner are merged into
/// the next save, so a client cannot flood the database and the newest layout is never lost.
/// Guests get a new account id on every connection, so their layout lives only until they leave.
/// </remarks>
public sealed partial class MalinovLobbyLayoutSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IPrototypeManager _prototype = default!;
    [Dependency] private UserDbDataManager _userDb = default!;

    /// <summary>
    /// Shortest time between two saves of one player's layout.
    /// </summary>
    public static readonly TimeSpan SaveInterval = TimeSpan.FromSeconds(2);

    private readonly Dictionary<NetUserId, PlayerLayout> _layouts = new();
    // Players whose newest layout waits for its save; usually empty, so Update stays cheap.
    private readonly HashSet<NetUserId> _waitingSaves = new();
    private readonly List<NetUserId> _dueSaves = new();

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MalinovLobbyLayoutChangedEvent>(OnLayoutChanged);

        _userDb.AddOnLoadPlayer(LoadLayout);
        _userDb.AddOnFinishLoad(SendLayout);
        _userDb.AddOnPlayerDisconnect(OnPlayerDisconnect);
    }

    private void OnLayoutChanged(MalinovLobbyLayoutChangedEvent ev, EntitySessionEventArgs args)
    {
        TrySetLayout(args.SenderSession, ev.Layout, ev.Version);
    }

    private void OnPlayerDisconnect(ICommonSession session)
    {
        if (!_layouts.Remove(session.UserId, out var data))
            return;

        // The player is gone, so the newest layout is saved right away instead of after the interval.
        if (_waitingSaves.Remove(session.UserId))
            Save(session.UserId, data);
    }

    /// <summary>
    /// Replaces the player's layout with what the server can accept of <paramref name="layout"/> and saves it.
    /// </summary>
    /// <param name="version">Number of the change from the player's client; older changes than the last one are dropped.</param>
    public bool TrySetLayout(ICommonSession session, MalinovLobbyLayout? layout, uint version)
    {
        if (layout == null || !CanSetLayout(session, version))
            return false;

        var sanitized = MalinovLobbyLayouts.Sanitize(layout, _prototype);
        if (sanitized.Places.Count != layout.Places?.Count || sanitized.Hidden.Count != layout.Hidden?.Count)
            Log.Warning($"{session} sent a lobby layout with unknown, overlapping, repeated or locked tiles.");

        SetLayout(session, sanitized, version);
        return true;
    }

    /// <summary>
    /// A layout can change once the saved one has loaded, as earlier changes would be overwritten by it,
    /// and only by a newer change than the last one: changes sent together arrive in no particular order.
    /// </summary>
    public bool CanSetLayout(ICommonSession session, uint version)
    {
        return _layouts.TryGetValue(session.UserId, out var data) && version > data.Version;
    }

    /// <returns>The player's layout, or <c>null</c> until it has loaded.</returns>
    public MalinovLobbyLayout? GetLayout(NetUserId user)
    {
        return _layouts.TryGetValue(user, out var data) ? data.Layout : null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_waitingSaves.Count == 0)
            return;

        _dueSaves.Clear();
        foreach (var user in _waitingSaves)
        {
            if (_layouts.TryGetValue(user, out var data) && data.NextSave <= _timing.CurTime)
                _dueSaves.Add(user);
        }

        foreach (var user in _dueSaves)
        {
            _waitingSaves.Remove(user);
            Save(user, _layouts[user]);
        }
    }

    private async Task LoadLayout(ICommonSession session, CancellationToken cancel)
    {
        var layout = session.Channel.AuthType.HasStaticUserId()
            ? await _db.GetMalinovLobbyLayoutAsync(session.UserId, cancel)
            : null;
        cancel.ThrowIfCancellationRequested();

        _layouts[session.UserId] = new PlayerLayout
        {
            Layout = layout ?? new MalinovLobbyLayout(),
            Stored = session.Channel.AuthType.HasStaticUserId(),
        };
    }

    private void SendLayout(ICommonSession session)
    {
        if (_layouts.TryGetValue(session.UserId, out var data))
            RaiseNetworkEvent(new MalinovLobbyLayoutEvent(data.Layout), session);
    }

    private void SetLayout(ICommonSession session, MalinovLobbyLayout layout, uint version)
    {
        var data = _layouts[session.UserId];
        data.Layout = layout;
        data.Version = version;
        if (!data.Stored)
            return;

        if (data.NextSave <= _timing.CurTime)
        {
            _waitingSaves.Remove(session.UserId);
            Save(session.UserId, data);
            return;
        }

        _waitingSaves.Add(session.UserId);
    }

    /// <summary>
    /// Starts saving the newest layout of the player. Saves of one player run one after another,
    /// so an older layout never lands after a newer one.
    /// </summary>
    private void Save(NetUserId user, PlayerLayout data)
    {
        data.NextSave = _timing.CurTime + SaveInterval;
        data.LastSave = SaveAfter(data.LastSave, user, data.Layout);
    }

    private async Task SaveAfter(Task previous, NetUserId user, MalinovLobbyLayout layout)
    {
        await previous;

        try
        {
            await _db.SetMalinovLobbyLayoutAsync(user, layout);
        }
        catch (Exception e)
        {
            Log.Error($"Failed to save the lobby layout of {user}: {e}");
        }
    }

    private sealed class PlayerLayout
    {
        public MalinovLobbyLayout Layout = new();

        /// <summary>
        /// Number of the last change applied from the player's client.
        /// </summary>
        public uint Version;

        /// <summary>
        /// Whether the layout is kept in the database; guests have no lasting account.
        /// </summary>
        public bool Stored;

        /// <summary>
        /// Earliest time of the next save.
        /// </summary>
        public TimeSpan NextSave;

        /// <summary>
        /// The last save started; never fails, so the next one can wait for it.
        /// </summary>
        public Task LastSave = Task.CompletedTask;
    }
}
