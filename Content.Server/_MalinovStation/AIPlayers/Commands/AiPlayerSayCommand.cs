using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.Administration;
using Content.Shared.Administration;
using Robust.Shared.Console;

namespace Content.Server._MalinovStation.AIPlayers.Commands;

/// <summary>
/// Debug command exercising the "Talk" action end-to-end (spec section 18/29: action registry + debug tools).
/// </summary>
[AdminCommand(AdminFlags.Debug)]
public sealed partial class AiPlayerSayCommand : LocalizedEntityCommands
{
    [Dependency] private AiActionRegistrySystem _actions = default!;

    public override string Command => "aiplayer_say";

    public override void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length < 2)
        {
            shell.WriteLine(Loc.GetString("shell-need-minimum-one-argument"));
            return;
        }

        if (!NetEntity.TryParse(args[0], out var entNet) || !EntityManager.TryGetEntity(entNet, out var uid) || !EntityManager.EntityExists(uid))
        {
            shell.WriteLine(Loc.GetString("shell-could-not-find-entity-with-uid", ("uid", args[0])));
            return;
        }

        var text = string.Join(' ', args[1..]);

        if (!_actions.TryDoAction(uid.Value, "Talk", new TalkActionParams(text), out var failReason))
        {
            shell.WriteError(Loc.GetString("cmd-aiplayer_say-failed", ("reason", failReason ?? "unknown")));
            return;
        }

        shell.WriteLine(Loc.GetString("cmd-aiplayer_say-success"));
    }
}
