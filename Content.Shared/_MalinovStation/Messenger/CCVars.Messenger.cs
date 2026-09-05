using Robust.Shared.Configuration;

#pragma warning disable IDE0130 // Namespace does not match folder structure
namespace Content.Shared.CCVar;

public sealed partial class CCVars
{
    /// <summary>
    ///     Maximum length of a single messenger message.
    /// </summary>
    public static readonly CVarDef<int> MalinovMessengerMaxMessageLength =
        CVarDef.Create("malinov.messenger.max-message-length", 100, CVar.REPLICATED);

    /// <summary>
    ///     How long (in seconds) a peer remains "online" after its last announce.
    /// </summary>
    public static readonly CVarDef<int> MalinovMessengerPeerTtlSeconds =
        CVarDef.Create("malinov.messenger.peer-ttl-seconds", 30, CVar.REPLICATED);

    /// <summary>
    ///     Maximum number of messages kept per contact in the round-local history.
    /// </summary>
    public static readonly CVarDef<int> MalinovMessengerHistoryPerContact =
        CVarDef.Create("malinov.messenger.history_per_contact", 50, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Whether incoming messenger notifications play a sound.
    /// </summary>
    public static readonly CVarDef<bool> MalinovMessengerSoundEnabled =
        CVarDef.Create("malinov.messenger.sound_enabled", true, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Length of the sliding rate-limit window (in seconds) measured per sender.
    /// </summary>
    public static readonly CVarDef<float> MalinovMessengerRateWindowSeconds =
        CVarDef.Create("malinov.messenger.rate_window_sec", 10f, CVar.SERVER | CVar.REPLICATED);

    /// <summary>
    ///     Maximum number of messages a single sender may send within the rate-limit window.
    /// </summary>
    public static readonly CVarDef<int> MalinovMessengerRateMaxMessages =
        CVarDef.Create("malinov.messenger.rate_max_msgs", 5, CVar.SERVER);
}