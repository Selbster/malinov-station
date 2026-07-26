using Robust.Shared.GameStates;

namespace Content.Shared._MalinovStation.Surgery.OrganEffects;

/// <summary>
/// Applied to a body with exactly one leg organ - still able to stand and move, just slower.
/// Mirrors the pattern of Content.Shared/Traits/Assorted/ImpairedMobilityComponent.cs.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class OneLegComponent : Component
{
    [DataField, AutoNetworkedField]
    public float SpeedModifier = 0.5f;
}
