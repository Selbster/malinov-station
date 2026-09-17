using Content.Shared._MalinovStation.Messenger;
using Content.Shared.CartridgeLoader;
using Content.Shared.PDA;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Automatically installs the Malinov Messenger program on every PDA spawned at runtime,
///     except for off-station factions (CentCom, Syndicate, ERT/CBRN), visitors, and non-functional PDAs.
/// </summary>
public sealed partial class MalinovMessengerInstallerSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;

    /// <summary>
    ///     PDA prototypes that should not receive the messenger program.
    /// </summary>
    private static readonly HashSet<EntProtoId> ExcludedPdaPrototypes = new()
    {
        // CentCom / admin
        "CentcomPDA",
        "AdminPDA",
        "DeathsquadPDA",

        // Syndicate antagonists
        "SyndiPDA",
        "SyndiOperativePDA",
        "SyndiCorpsmanPDA",
        "SyndiCommanderPDA",
        "PiratePDA",
        "NinjaPDA",

        // ERT / CBRN
        "ERTLeaderPDA",
        "ERTChaplainPDA",
        "ERTEngineerPDA",
        "ERTJanitorPDA",
        "ERTMedicPDA",
        "ERTSecurityPDA",
        "CBURNPDA",

        // Visitors (no station records, messenger is useless)
        "VisitorPDA",
        "VisitorClownPDA",
        "VisitorChaplainPDA",
        "VisitorLibrarianPDA",
        "VisitorLawyerPDA",
        "VisitorMedicalPDA",
        "VisitorMusicianPDA",

        // Off-station magic / disguise
        "WizardPDA",
        "ChameleonPDA",
        "ChameleonAgentPDA",

        // Fun / non-functional
        "CluwnePDA",
        "ScurretPDA",
    };

    private static readonly EntProtoId MessengerProgramPrototype = "MalinovMessengerCartridge";

    public override void Initialize()
    {
        base.Initialize();

        foreach (var id in ExcludedPdaPrototypes)
        {
            if (!_prototypeManager.HasIndex(id))
                Log.Warning("Malinov Messenger installer: excluded PDA prototype '{Proto}' not found", id);
        }

        SubscribeLocalEvent<CartridgeLoaderComponent, MapInitEvent>(OnMapInit);
    }

    private void OnMapInit(Entity<CartridgeLoaderComponent> ent, ref MapInitEvent args)
    {
        // Only PDAs receive the messenger; other CartridgeLoader devices (consoles, etc.) do not.
        if (!HasComp<PdaComponent>(ent.Owner))
            return;

        var prototype = MetaData(ent.Owner).EntityPrototype?.ID;
        if (prototype is null || IsExcluded(prototype))
            return;

        if (_cartridgeLoader.HasProgram<MalinovMessengerCartridgeComponent>(ent.AsNullable()))
            return;

        _cartridgeLoader.InstallProgram(ent, MessengerProgramPrototype, deinstallable: false);
    }

    /// <summary>
    ///     Checks the prototype itself and every parent against the exclusion set, so a child
    ///     variant of an excluded PDA (e.g. <c>parent: SyndiPDA</c>) is excluded as well.
    /// </summary>
    private bool IsExcluded(string id)
    {
        foreach (var parent in _prototypeManager.EnumerateParents<EntityPrototype>(id, includeSelf: true))
        {
            if (ExcludedPdaPrototypes.Contains(parent.ID))
                return true;
        }

        return false;
    }
}
