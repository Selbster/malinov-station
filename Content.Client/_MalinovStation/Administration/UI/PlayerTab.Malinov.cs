using Content.Client.Administration;
using Content.Shared.CCVar;
using Robust.Client.Graphics;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Content.Client.Administration.UI.Tabs.PlayerTab;

public sealed partial class PlayerTab
{
    protected override void EnteredTree()
    {
        base.EnteredTree();
        _adminSystem.PlayerListChanged += RefreshPlayerList;
        _adminSystem.OverlayEnabled += OverlayEnabled;
        _adminSystem.OverlayDisabled += OverlayDisabled;
        _config.OnValueChanged(CCVars.AdminPlayerTabRoleSetting, RoleSettingChanged, true);
        _config.OnValueChanged(CCVars.AdminPlayerTabColorSetting, ColorSettingChanged, true);
        _config.OnValueChanged(CCVars.AdminPlayerTabSymbolSetting, SymbolSettingChanged, true);
        OverlayButton.Pressed = IoCManager.Resolve<IOverlayManager>().HasOverlay<AdminNameOverlay>();
    }

    protected override void ExitedTree()
    {
        _adminSystem.PlayerListChanged -= RefreshPlayerList;
        _adminSystem.OverlayEnabled -= OverlayEnabled;
        _adminSystem.OverlayDisabled -= OverlayDisabled;
        _config.UnsubValueChanged(CCVars.AdminPlayerTabRoleSetting, RoleSettingChanged);
        _config.UnsubValueChanged(CCVars.AdminPlayerTabColorSetting, ColorSettingChanged);
        _config.UnsubValueChanged(CCVars.AdminPlayerTabSymbolSetting, SymbolSettingChanged);
        base.ExitedTree();
    }
}
