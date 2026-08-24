using Content.Shared._MalinovStation.Messenger;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Automatically installs the Malinov Messenger program on every PDA spawned at runtime,
///     except for off-station factions (CentCom, nuclear operatives, ERT/CBRN).
/// </summary>
public sealed partial class MalinovMessengerInstallerSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;

    /// <summary>
    ///     PDA prototypes that should not receive the messenger program.
    /// </summary>
    private static readonly HashSet<EntProtoId> ExcludedPdaPrototypes = new()
    {
        // CentCom / admin
        "CentcomPDA",
        "AdminPDA",
        "DeathsquadPDA",

        // Nuclear operatives
        "SyndiOperativePDA",
        "SyndiCorpsmanPDA",
        "SyndiCommanderPDA",

        // ERT / CBRN
        "ERTLeaderPDA",
        "ERTChaplainPDA",
        "ERTEngineerPDA",
        "ERTJanitorPDA",
        "ERTMedicPDA",
        "ERTSecurityPDA",
        "CBURNPDA"
    };

    private static readonly EntProtoId MessengerProgramPrototype = "MalinovMessengerCartridge";

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<CartridgeLoaderComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<CartridgeLoaderComponent> ent, ref MapInitEvent args)
    {
        // Only PDAs receive the messenger; other CartridgeLoader devices (consoles, etc.) do not.
        if (!HasComp<PdaComponent>(ent.Owner))
            return;

        var prototype = MetaData(ent.Owner).EntityPrototype?.ID;
        if (prototype is null || ExcludedPdaPrototypes.Contains(prototype))
            return;

        if (_cartridgeLoader.HasProgram<MalinovMessengerCartridgeComponent>(ent.AsNullable()))
            return;

        _cartridgeLoader.InstallProgram(ent, MessengerProgramPrototype, deinstallable: false);
    }
}
