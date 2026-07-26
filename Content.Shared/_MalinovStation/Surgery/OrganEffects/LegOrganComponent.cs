using Robust.Shared.GameStates;

namespace Content.Shared._MalinovStation.Surgery.OrganEffects;

/// <summary>
/// Marks an organ as a leg: how many of these a body currently has determines its movement penalty -
/// see <see cref="OneLegComponent"/>/<see cref="NoLegsComponent"/> and <see cref="OrganEffectsSystem"/>.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class LegOrganComponent : Component;
