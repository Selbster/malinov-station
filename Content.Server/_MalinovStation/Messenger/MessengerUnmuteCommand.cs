using System.Linq;
using Content.Server.Administration;
using Content.Shared.Administration;
using Content.Shared._MalinovStation.Messenger;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Admin command that unmutes a previously muted messenger account (SS14 launcher username).
/// </summary>
[AdminCommand(AdminFlags.Admin)]
public sealed partial class MessengerUnmuteCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IPlayerManager _playerManager = default!;

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

        if (!_playerManager.TryGetSessionByUsername(args[0], out var target))
        {
            shell.WriteError(Loc.GetString("malinov-messenger-command-player-not-found", ("name", args[0])));
            return;
        }

        var system = _entManager.System<MalinovMessengerServerSystem>();
        var adminName = shell.Player?.Name;
        var found = false;

        var query = _entManager.EntityQueryEnumerator<MalinovMessengerServerComponent>();
        while (query.MoveNext(out var serverUid, out _))
        {
            system.Unmute(serverUid, target.Name, adminName);
            found = true;
        }

        if (!found)
        {
            shell.WriteError(Loc.GetString("malinov-messenger-command-no-server"));
            return;
        }

        shell.WriteLine(Loc.GetString("malinov-messenger-unmute-command-success", ("name", target.Name)));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
    {
        if (args.Length == 1)
        {
            var options = _playerManager.Sessions.Select(c => c.Name).OrderBy(c => c).ToArray();
            return CompletionResult.FromHintOptions(options, Loc.GetString("malinov-messenger-unmute-command-arg"));
        }

        return CompletionResult.Empty;
    }
}