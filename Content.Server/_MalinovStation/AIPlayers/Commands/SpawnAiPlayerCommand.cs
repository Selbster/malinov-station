using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Roles;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._MalinovStation.AIPlayers.Commands;

/// <summary>
/// Debug command to spawn a single AI player. Optionally takes a PersistentId (Milestone 9) so an admin can
/// spawn the same "character" again across rounds/restarts, provided <c>ai_players.persistence.enabled</c>
/// is on.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class SpawnAiPlayerCommand : LocalizedEntityCommands
{
    [Dependency] private AIPlayerSystem _aiPlayers = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override string Command => "aiplayer_spawn";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var jobId = args.Length > 0 ? args[0] : "Passenger";
        var persistentId = args.Length > 1 ? args[1] : null;

        if (!_proto.HasIndex<JobPrototype>(jobId))
        {
            shell.WriteError(Loc.GetString("cmd-aiplayer_spawn-invalid-job", ("job", jobId)));
            return;
        }

        var mob = _aiPlayers.SpawnAiPlayer(jobId, persistentId: persistentId);
        if (mob is null)
        {
            shell.WriteError(Loc.GetString("cmd-aiplayer_spawn-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-aiplayer_spawn-success", ("entity", EntityManager.ToPrettyString(mob.Value))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                CompletionHelper.PrototypeIDs<JobPrototype>(proto: _proto),
                Loc.GetString("cmd-aiplayer_spawn-arg-job"));
        }

        return CompletionResult.Empty;
    }
}
