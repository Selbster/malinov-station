using Content.Shared._MalinovStation.Surgery.OrganEffects;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.Surgery.OrganEffects;

/// <summary>
/// Periodically hurts bodies with <see cref="NoHeartComponent"/> - a body missing its heart organ.
/// Stops once the patient is already dead - a corpse has no circulation to fail further.
/// </summary>
public sealed partial class CardiacArrestSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<NoHeartComponent>();
        while (query.MoveNext(out var uid, out var noHeart))
        {
            if (_timing.CurTime < noHeart.NextUpdate)
                continue;

            noHeart.NextUpdate = _timing.CurTime + noHeart.UpdateInterval;

            if (_mobState.IsDead(uid))
                continue;

            _damageable.TryChangeDamage(uid, noHeart.Damage, true);
        }
    }
}
