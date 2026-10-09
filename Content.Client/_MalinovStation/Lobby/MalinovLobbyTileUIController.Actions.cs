using System.Diagnostics.CodeAnalysis;
using Content.Client.LateJoin;
using Content.Client.Lobby;
using Content.Client.Lobby.UI;
using Robust.Client.Console;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._MalinovStation.Lobby;

public sealed partial class MalinovLobbyTileUIController
{
    /*
     * Lobby actions part: readiness, late join, character setup and layout editing,
     * asked for by tiles and lobby buttons.
     */
    [Dependency] private IClientConsoleHost _consoleHost = default!;

    private void SubscribeLobbyActions(LobbyGui lobby)
    {
        lobby.CharacterPreview.CharacterSetupButton.OnPressed += OnCharacterSetupPressed;
        lobby.CustomizeButton.OnPressed += OnCustomizePressed;
        lobby.ResetLayoutButton.OnPressed += OnResetLayoutPressed;
    }

    /// <remarks>
    /// Runs before the tiles are cleared, while <see cref="_lobby"/> still points at the lobby being left.
    /// </remarks>
    private void UnsubscribeLobbyActions()
    {
        if (_lobby == null)
            return;

        _lobby.CharacterPreview.CharacterSetupButton.OnPressed -= OnCharacterSetupPressed;
        _lobby.CustomizeButton.OnPressed -= OnCustomizePressed;
        _lobby.ResetLayoutButton.OnPressed -= OnResetLayoutPressed;
    }

    private void OnCharacterSetupPressed(BaseButton.ButtonEventArgs args)
    {
        TryOpenCharacterSetup();
    }

    private void OnCustomizePressed(BaseButton.ButtonEventArgs args)
    {
        TrySetEditing(!_editing);
    }

    private void OnResetLayoutPressed(BaseButton.ButtonEventArgs args)
    {
        TryResetLayout();
    }

    /// <inheritdoc/>
    public bool TrySetReady(bool ready)
    {
        if (!CanSetReady())
            return false;

        SetReady(ready);
        return true;
    }

    /// <summary>
    /// Readiness only counts before the round starts.
    /// </summary>
    public bool CanSetReady()
    {
        return _gameTicker is { IsGameStarted: false };
    }

    /// <inheritdoc/>
    public bool TryJoinGame()
    {
        if (!CanJoinGame())
            return false;

        JoinGame();
        return true;
    }

    /// <summary>
    /// Late joining needs a running round that allows it.
    /// </summary>
    public bool CanJoinGame()
    {
        return _gameTicker is { IsGameStarted: true, DisallowedLateJoin: false };
    }

    /// <summary>
    /// Switches the lobby to character setup. A ready player stops being ready while editing.
    /// </summary>
    public bool TryOpenCharacterSetup()
    {
        if (!CanOpenCharacterSetup(out var state))
            return false;

        TrySetReady(false);
        TrySetEditing(false);
        state.SwitchState(LobbyGui.LobbyGuiState.CharacterSetup);
        return true;
    }

    /// <summary>
    /// Character setup opens from the lobby screen only.
    /// </summary>
    public bool CanOpenCharacterSetup([NotNullWhen(true)] out LobbyState? state)
    {
        state = _lobbyState;
        return state?.Lobby != null;
    }

    private void SetReady(bool ready)
    {
        _consoleHost.ExecuteCommand($"toggleready {ready}");
    }

    private static void JoinGame()
    {
        new LateJoinGui().OpenCentered();
    }
}
