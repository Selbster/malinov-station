using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._MalinovStation.Messenger;
using Robust.Shared.Console;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Admin command that unmutes a previously muted messenger sender name.
///     The target is the ID card owner name of the messenger account.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class MessengerUnmuteCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;

    public string Command => "messengerunmute";

    public string Description => Loc.GetString("malinov-messenger-unmute-command-description");

    public string Help => Loc.GetString("malinov-messenger-unmute-command-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("malinov-messenger-command-wrong-arguments-number"));
            return;
        }

        var targetName = args[0];
        var system = _entManager.System<MalinovMessengerServerSystem>();
        var adminName = shell.Player?.Name;
        var found = false;

        var query = _entManager.EntityQueryEnumerator<MalinovMessengerServerComponent>();
        while (query.MoveNext(out var serverUid, out _))
        {
            system.Unmute(serverUid, targetName, adminName);
            found = true;
        }

        if (!found)
        {
            shell.WriteError(Loc.GetString("malinov-messenger-command-no-server"));
            return;
        }

        shell.WriteLine(Loc.GetString("malinov-messenger-unmute-command-success", ("name", targetName)));
    }
}
