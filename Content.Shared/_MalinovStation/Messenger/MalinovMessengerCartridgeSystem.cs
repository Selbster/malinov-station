using Content.Shared.CartridgeLoader;

namespace Content.Shared._MalinovStation.Messenger;

/// <summary>
/// Handles the Malinov Messenger cartridge lifecycle and UI state updates.
/// </summary>
public sealed partial class MalinovMessengerCartridgeSystem : EntitySystem
{
    [Dependency] private CartridgeLoaderSystem _cartridgeLoaderSystem = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<MalinovMessengerCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
    }

    private void OnUiReady(EntityUid uid, MalinovMessengerCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader);
    }

    private void UpdateUiState(EntityUid uid, EntityUid loaderUid)
    {
        var state = new MalinovMessengerUiState();
        _cartridgeLoaderSystem.UpdateCartridgeUiState(loaderUid, state);
    }
}
