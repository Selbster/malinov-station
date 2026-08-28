using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._MalinovStation.Messenger;
using Robust.Shared.Console;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Admin command that mutes a messenger sender name for the rest of the round.
///     The target is the ID card owner name of the messenger account.
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed class MessengerMuteCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;

    public string Command => "messengermute";

    public string Description => Loc.GetString("malinov-messenger-mute-command-description");

    public string Help => Loc.GetString("malinov-messenger-mute-command-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Loc.GetString("malinov-messenger-command-wrong-arguments-number"));
            return;
        }

        var targetName = args[0];
        var system = _entManager.System<MalinovMessengerServerSystem>();
        var found = false;

        var query = _entManager.EntityQueryEnumerator<MalinovMessengerServerComponent>();
        while (query.MoveNext(out var serverUid, out _))
        {
            system.Mute(serverUid, targetName);
            found = true;
        }

        if (!found)
        {
            shell.WriteError(Loc.GetString("malinov-messenger-command-no-server"));
            return;
        }

        shell.WriteLine(Loc.GetString("malinov-messenger-mute-command-success", ("name", targetName)));
    }
}
