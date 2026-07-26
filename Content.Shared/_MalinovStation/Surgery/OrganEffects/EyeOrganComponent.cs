using Robust.Shared.GameStates;

namespace Content.Shared._MalinovStation.Surgery.OrganEffects;

/// <summary>
/// Marks an organ as a pair of eyes: removing it from a body blinds that body, using the existing
/// <see cref="Content.Shared.Eye.Blinding.Components.BlindableComponent"/> already present on every humanoid.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(OrganEffectsSystem))]
public sealed partial class EyeOrganComponent : Component
{
    /// <summary>Snapshot of the body's eye damage/threshold at the moment the eyes were removed, so an
    /// unrelated pre-existing injury (e.g. from a welder) survives an eye transplant intact.</summary>
    [DataField]
    public int? SavedEyeDamage;

    [DataField]
    public int? SavedMinDamage;
}
