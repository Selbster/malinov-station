using Robust.Shared.Configuration;

namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Maximum length of a single P2P messenger message.
    /// </summary>
    public static readonly CVarDef<int> MalinovMessengerMaxMessageLength =
        CVarDef.Create("malinov.messenger.max-message-length", 512, CVar.REPLICATED);

    /// <summary>
    ///     How long (in seconds) a peer remains "online" after its last announce.
    /// </summary>
    public static readonly CVarDef<int> MalinovMessengerPeerTtlSeconds =
        CVarDef.Create("malinov.messenger.peer-ttl-seconds", 30, CVar.REPLICATED);
}
