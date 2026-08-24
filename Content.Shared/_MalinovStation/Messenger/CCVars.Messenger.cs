using Robust.Shared.Configuration;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Whether the Malinov Messenger PDA cartridge is enabled.
    /// </summary>
    public static readonly CVarDef<bool> MalinovMessengerEnabled =
        CVarDef.Create("malinov.messenger.enabled", false, CVar.SERVER);
}
