using Robust.Shared.Player;

namespace Content.Server._MalinovStation.Construction;

/// <summary>
/// Tracks only active structure requests, without retaining sessions after their last request completes.
/// </summary>
public sealed class MalinovConstructionRequests
{
    private readonly Dictionary<ICommonSession, HashSet<int>> _requests = new();

    public int SessionCount => _requests.Count;

    public Request? TryBegin(ICommonSession session, int ack)
    {
        if (!_requests.TryGetValue(session, out var pending))
        {
            pending = new HashSet<int>();
            _requests.Add(session, pending);
        }

        return pending.Add(ack) ? new Request(this, session, pending, ack) : null;
    }

    public void Remove(ICommonSession session) => _requests.Remove(session);

    public void Clear() => _requests.Clear();

    public sealed class Request : IDisposable
    {
        private MalinovConstructionRequests? _owner;
        private readonly ICommonSession _session;
        private readonly HashSet<int> _pending;
        private readonly int _ack;

        internal Request(MalinovConstructionRequests owner, ICommonSession session, HashSet<int> pending, int ack)
        {
            _owner = owner;
            _session = session;
            _pending = pending;
            _ack = ack;
        }

        // An old continuation must not acknowledge or remove a new round's request with the same ACK.
        public bool IsActive => _owner != null &&
                                _owner._requests.TryGetValue(_session, out var current) &&
                                ReferenceEquals(current, _pending) && _pending.Contains(_ack);

        public void Dispose()
        {
            if (!IsActive)
            {
                _owner = null;
                return;
            }

            _pending.Remove(_ack);
            if (_pending.Count == 0)
                _owner!._requests.Remove(_session);
            _owner = null;
        }
    }
}
