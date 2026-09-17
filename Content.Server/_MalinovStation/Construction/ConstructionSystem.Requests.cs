using Content.Shared.Construction;
using Content.Shared.GameTicking;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;

// ReSharper disable once CheckNamespace
namespace Content.Server.Construction;

public sealed partial class ConstructionSystem
{
    [Dependency] private IPlayerManager _malinovConstructionPlayers = default!;

    private void MalinovInitializeConstructionRequests()
    {
        _malinovConstructionPlayers.PlayerStatusChanged += MalinovOnConstructionPlayerStatus;
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => _beingBuilt.Clear());
    }

    public override void Shutdown()
    {
        _malinovConstructionPlayers.PlayerStatusChanged -= MalinovOnConstructionPlayerStatus;
        _beingBuilt.Clear();
        base.Shutdown();
    }

    private void MalinovOnConstructionPlayerStatus(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus is SessionStatus.Disconnected or SessionStatus.Zombie)
            _beingBuilt.Remove(args.Session);
    }

    private async void MalinovHandleStartStructureConstruction(TryStartStructureConstructionMessage ev, EntitySessionEventArgs args)
    {
        try
        {
            await HandleStartStructureConstruction(ev, args);
        }
        catch (Exception exception)
        {
            // The request scope releases its ACK even when legacy construction fails after a do-after.
            Log.Error($"Failed to start structure construction '{ev.PrototypeName}': {exception}");
        }
    }
}
