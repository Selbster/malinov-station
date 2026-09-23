using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Nutrition.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Server._MalinovStation.Nutrition;

/// <summary>
/// Produces a milkable reagent using the owner's hunger. The reservoir is a separate solution entity.
/// </summary>
[RegisterComponent, Access(typeof(MilkProducerSystem)), AutoGenerateComponentPause]
public sealed partial class MilkProducerComponent : Component
{
    /// <summary>
    /// ID of the existing solution managed by the owner.
    /// </summary>
    [DataField]
    public string SolutionName = "milk";

    /// <summary>
    /// Reagent added to the reservoir during production.
    /// </summary>
    [DataField]
    public ProtoId<ReagentPrototype> ReagentId = "Milk";

    /// <summary>
    /// Maximum volume produced each interval, limited by the reservoir's free space.
    /// </summary>
    [DataField]
    public FixedPoint2 QuantityPerUpdate = 5;

    /// <summary>
    /// Interval between production attempts, including attempts skipped while full or hungry.
    /// </summary>
    [DataField]
    public TimeSpan GrowthDelay = TimeSpan.FromSeconds(60);

    /// <summary>
    /// Hunger spent per unit actually added to the reservoir.
    /// </summary>
    [DataField]
    public float HungerPerUnit = 1f;

    /// <summary>
    /// Hunger must remain strictly above this threshold after paying for production.
    /// </summary>
    [DataField]
    public SatiationValue MinHungerThreshold = "Peckish";

    /// <summary>
    /// Time needed to transfer the available stock into a held container.
    /// </summary>
    [DataField]
    public TimeSpan MilkingDelay = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Next production attempt. Initialized when the component starts and shifted on unpause.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly), AutoPausedField]
    public TimeSpan NextGrowth;

    /// <summary>
    /// Whether the configuration passed validation when this component started.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool ConfigurationValid;

    /// <summary>
    /// Suppresses repeated diagnostics if the configured reservoir is missing.
    /// </summary>
    [ViewVariables(VVAccess.ReadOnly)]
    public bool MissingSolutionLogged;
}
