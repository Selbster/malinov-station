using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared.Roles;
using Robust.Shared.Console;
using Robust.Shared.Prototypes;

namespace Content.Server._MalinovStation.AIPlayers.Commands;

/// <summary>
/// AI Players 2.0 Milestone 1: debug command to spawn a single AI player in cognitive mode - the deliberate,
/// explicit opt-in mechanism for "the one AI engineer" the milestone's vertical slice targets (spec section
/// 34). Every other AI player, including ones spawned via the plain <c>aiplayer_spawn</c>, is completely
/// unaffected. Also requires <c>ai_players.cognitive.enabled</c> to actually request cognitive decisions - see
/// <see cref="Components.CognitiveModeComponent"/>.
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class SpawnCognitiveAiPlayerCommand : LocalizedEntityCommands
{
    [Dependency] private AIPlayerSystem _aiPlayers = default!;
    [Dependency] private IPrototypeManager _proto = default!;

    public override string Command => "aiplayer_spawn_cognitive";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        var jobId = args.Length > 0 ? args[0] : "Passenger";

        if (!_proto.HasIndex<JobPrototype>(jobId))
        {
            shell.WriteError(Loc.GetString("cmd-aiplayer_spawn_cognitive-invalid-job", ("job", jobId)));
            return;
        }

        var mob = _aiPlayers.SpawnAiPlayer(jobId, cognitiveMode: true);
        if (mob is null)
        {
            shell.WriteError(Loc.GetString("cmd-aiplayer_spawn_cognitive-failed"));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-aiplayer_spawn_cognitive-success", ("entity", EntityManager.ToPrettyString(mob.Value))));
    }

    public override CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            return CompletionResult.FromHintOptions(
                CompletionHelper.PrototypeIDs<JobPrototype>(proto: _proto),
                Loc.GetString("cmd-aiplayer_spawn_cognitive-arg-job"));
        }

        return CompletionResult.Empty;
    }
}
