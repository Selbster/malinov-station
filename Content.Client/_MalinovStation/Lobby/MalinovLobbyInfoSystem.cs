using Content.Shared._MalinovStation.Lobby;

namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// Keeps the last <see cref="MalinovLobbyInfoEvent"/> the server sent for the lobby tiles.
/// </summary>
public sealed class MalinovLobbyInfoSystem : EntitySystem
{
    /// <summary>
    /// Latest round information, or <c>null</c> until the server sends it.
    /// </summary>
    public MalinovLobbyInfoEvent? Info { get; private set; }

    /// <summary>
    /// Raised when <see cref="Info"/> changes.
    /// </summary>
    public event Action? InfoUpdated;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeNetworkEvent<MalinovLobbyInfoEvent>(OnLobbyInfo);
    }

    public override void Shutdown()
    {
        base.Shutdown();
        Info = null;
    }

    private void OnLobbyInfo(MalinovLobbyInfoEvent args)
    {
        Info = args;
        InfoUpdated?.Invoke();
    }
}
