using Content.Server.Database;
using Content.Shared.CCVar;
using Robust.Server.Upload;
using Robust.Shared.Configuration;
using Robust.Shared.Player;
using Robust.Shared.Utility; // Malinov edit - current resource upload event uses resource paths directly.

namespace Content.Server.Administration;

public sealed partial class ContentNetworkResourceManager
{
    [Dependency] private IServerDbManager _serverDb = default!;
    [Dependency] private NetworkResourceManager _netRes = default!;
    [Dependency] private IConfigurationManager _cfgManager = default!;

    [ViewVariables] public bool StoreUploaded { get; set; } = true;

    public void Initialize()
    {
        _cfgManager.OnValueChanged(CCVars.ResourceUploadingStoreEnabled, value => StoreUploaded = value, true);
        AutoDelete(_cfgManager.GetCVar(CCVars.ResourceUploadingStoreDeletionDays));
        _netRes.ResourcesUploaded += OnResourcesUploaded; // Malinov edit - current batched upload event.
    }

    // Malinov added - retain one independently scheduled database write per uploaded file.
    private void OnResourcesUploaded(NetworkResourcesUploadedEvent args)
    {
        foreach (var (relativePath, data) in args.Files)
            OnUploadResource(args.Session, relativePath, data);
    }

    private async void OnUploadResource(ICommonSession session, ResPath relativePath, byte[] data) // Malinov edit
    {
        if (StoreUploaded)
            await _serverDb.AddUploadedResourceLogAsync(session.UserId, DateTime.Now, relativePath.ToString(), data); // Malinov edit
    }

    private async void AutoDelete(int days)
    {
        if (days > 0)
            await _serverDb.PurgeUploadedResourceLogAsync(days);
    }
}
