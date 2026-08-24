using Content.Server.CrewManifest;
using Content.Server.Station.Systems;
using Content.Shared._MalinovStation.Messenger;
using Content.Shared.CartridgeLoader;

namespace Content.Server._MalinovStation.Messenger;

/// <summary>
///     Server-side logic for the Malinov Messenger cartridge.
///     Builds the contact catalog from the owning station's crew manifest.
/// </summary>
public sealed partial class MalinovMessengerCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private CrewManifestSystem _crewManifest = default!;
    [Dependency] private StationSystem _stationSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
    }

    private void OnUiReady(EntityUid uid, MalinovMessengerCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader, component);
    }

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid, MalinovMessengerCartridgeComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return;

        var owningStation = _stationSystem.GetOwningStation(uid);

        if (owningStation is null)
        {
            var unavailableState = new MalinovMessengerUiState(status: "malinov-messenger-manifest-unavailable");
            _cartridgeLoader.UpdateCartridgeUiState(loaderUid, unavailableState);
            return;
        }

        var (_, entries) = _crewManifest.GetCrewManifest(owningStation.Value);
        var contacts = new List<MalinovMessengerContact>();

        if (entries != null)
        {
            foreach (var entry in entries.Entries)
            {
                contacts.Add(new MalinovMessengerContact(entry.Name, entry.JobTitle));
            }
        }

        var state = new MalinovMessengerUiState(contacts);
        _cartridgeLoader.UpdateCartridgeUiState(loaderUid, state);
    }
}
