using Content.Shared.FixedPoint;

namespace Content.Shared.Body;

/// <summary>
/// Marks a <see cref="BodyPartComponent"/> as severable once its own <see cref="Content.Shared.Damage.Components.DamageableComponent"/>
/// crosses <see cref="AmputationThreshold"/>. Both fields are static prototype data set once at spawn and
/// never mutated at runtime, so unlike <see cref="Content.Shared.Mobs.Components.MobThresholdsComponent"/>
/// this doesn't need to be networked - client and server both read the same prototype.
/// </summary>
/// <seealso cref="BodyPartThresholdSystem" />
[RegisterComponent]
[Access(typeof(BodyPartThresholdSystem))]
public sealed partial class BodyPartThresholdsComponent : Component
{
    [DataField(required: true)]
    public FixedPoint2 AmputationThreshold;

    /// <summary>Bleeding applied to the body when this part is severed.</summary>
    [DataField]
    public float AmputationBleedAmount = 8f;
}
