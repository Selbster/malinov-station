using Content.Shared._MalinovStation.Lobby;

namespace Content.Client._MalinovStation.Lobby;

/// <summary>
/// Holds the player's own lobby layout: the one the server sent for their account and the changes they make,
/// which go back to the server to be kept.
/// </summary>
public sealed class MalinovLobbyLayoutSystem : EntitySystem
{
    // Numbers the changes sent to the server, which keeps only the newest of them.
    private uint _version;

    /// <summary>
    /// The player's layout, or <c>null</c> until the server sends it; the default layout applies meanwhile.
    /// </summary>
    public MalinovLobbyLayout? Layout { get; private set; }

    /// <summary>
    /// Raised whenever <see cref="Layout"/> changes, whether the server sent it or the player changed it.
    /// </summary>
    public event Action? LayoutUpdated;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<MalinovLobbyLayoutEvent>(OnLayoutReceived);
    }

    public override void Shutdown()
    {
        base.Shutdown();

        Layout = null;
        _version = 0;
    }

    private void OnLayoutReceived(MalinovLobbyLayoutEvent ev)
    {
        Layout = ev.Layout;
        LayoutUpdated?.Invoke();
    }

    /// <summary>
    /// Changes the player's layout and sends it to the server, which keeps it for their account.
    /// </summary>
    public void SetLayout(MalinovLobbyLayout layout)
    {
        Layout = layout;
        RaiseNetworkEvent(new MalinovLobbyLayoutChangedEvent(layout, ++_version));
        LayoutUpdated?.Invoke();
    }
}
