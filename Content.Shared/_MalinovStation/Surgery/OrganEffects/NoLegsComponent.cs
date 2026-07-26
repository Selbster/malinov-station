using Robust.Shared.GameStates;

namespace Content.Shared._MalinovStation.Surgery.OrganEffects;

/// <summary>
/// Applied to a body with no leg organs at all - can't stand back up and crawls at
/// <see cref="SpeedModifier"/> until at least one leg is reattached. Self-contained (doesn't touch
/// the Stunnable/Knockdown system) so it can't conflict with unrelated stuns/knockdowns.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class NoLegsComponent : Component
{
    [DataField, AutoNetworkedField]
    public float SpeedModifier = 0.4f;
}
