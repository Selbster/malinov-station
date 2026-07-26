using Content.Shared.Damage;
using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._MalinovStation.Surgery.OrganEffects;

/// <summary>
/// Applied to a body with no heart organ. A server-side system periodically deals damage while this is
/// present, representing a lack of circulation - see Content.Server's CardiacArrestSystem.
/// </summary>
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentPause]
public sealed partial class NoHeartComponent : Component
{
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan NextUpdate;

    [DataField]
    public TimeSpan UpdateInterval = TimeSpan.FromSeconds(2);

    [DataField]
    public DamageSpecifier Damage = new()
    {
        DamageDict =
        {
            ["Asphyxiation"] = 3,
        },
    };
}
