using Robust.Shared.GameStates;

namespace Content.Shared._MalinovStation.Surgery.OrganEffects;

/// <summary>
/// Marks an organ as a heart: removing it from a body applies <see cref="NoHeartComponent"/>, which puts
/// the (now heartless) body into cardiac arrest.
/// </summary>
[RegisterComponent, NetworkedComponent]
[Access(typeof(OrganEffectsSystem))]
public sealed partial class HeartOrganComponent : Component;
