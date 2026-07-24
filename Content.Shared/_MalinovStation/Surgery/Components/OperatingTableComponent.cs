using Robust.Shared.GameStates;

namespace Content.Shared._MalinovStation.Surgery.Components;

/// <summary>
/// Marks furniture as a proper operating table. Being buckled to an entity with this component while
/// undergoing surgery grants a speed/success bonus - it is not a hard requirement to perform surgery.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class OperatingTableComponent : Component
{
    /// <summary>Multiplier applied to step duration when the patient is buckled to this table.</summary>
    [DataField]
    public float SpeedMultiplier = 0.85f;

    /// <summary>Flat bonus added to the tool's success rate when the patient is buckled to this table.</summary>
    [DataField]
    public float SuccessRateBonus = 0.1f;
}
