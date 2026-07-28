using System.Linq;
using Content.Shared._MalinovStation.Surgery;
using Content.Shared.Body;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.Damage.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._MalinovStation.Combat;

/// <summary>
/// Gives ordinary combat damage a chance to also wound one specific body part, on top of (not instead of) the
/// existing whole-body damage pool - so <see cref="BodyPartThresholdSystem"/>'s per-part amputation, previously
/// only reachable through a targeted surgical mishap (see SharedSurgerySystem/SurgerySystem.Steps.ApplyPartDamage),
/// can also happen from a bad enough beating. Deliberately additive and modest: total damage-per-hit to the
/// character is unchanged, this only decides whether some of it *also* lands on one random part.
/// </summary>
public sealed partial class BodyPartDamageRoutingSystem : EntitySystem
{
    [Dependency] private BodySystem _body = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private IRobustRandom _random = default!;

    /// <summary>
    /// Fraction of a hit's Brute (Blunt/Slash/Piercing) damage that's *also* dealt to whichever part gets
    /// randomly picked, on top of the unmodified whole-body damage. A part's own Damageable is an entirely
    /// separate pool from the character's overall one (nothing sums them back together), so this doesn't
    /// make combat any more lethal overall - it only controls how fast repeated hits on the same part add
    /// up toward its own amputation threshold. Set to 1.0 (not diluted) to land in the same rough ballpark
    /// as a surgical mishap - see SurgerySystem.Steps.ApplyPartDamage, whose SawLimb/SawNeck mishaps (~16
    /// each) take about 4 failures to cross the 60 arm/leg threshold; a ~15-20 damage melee weapon landing
    /// repeatedly on the same limb should cost about as many hits, not an order of magnitude more.
    /// </summary>
    private const float RoutedFraction = 1.0f;

    private static readonly ProtoId<DamageTypePrototype>[] BruteTypes = { "Blunt", "Slash", "Piercing" };

    /// <summary>Relative odds of a hit landing on each structural part - the big, unarmored torso takes the
    /// brunt of it; the head is the smallest, most instinctively-guarded target.</summary>
    private static readonly (ProtoId<OrganCategoryPrototype> Category, float Weight)[] PartWeights =
    {
        (OrganCategoryIds.Torso, 40f),
        (OrganCategoryIds.Head, 10f),
        (OrganCategoryIds.ArmLeft, 12.5f),
        (OrganCategoryIds.ArmRight, 12.5f),
        (OrganCategoryIds.LegLeft, 12.5f),
        (OrganCategoryIds.LegRight, 12.5f),
    };

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BodyComponent, DamageChangedEvent>(OnBodyDamaged);
    }

    private void OnBodyDamaged(EntityUid uid, BodyComponent component, DamageChangedEvent args)
    {
        // Surgery (and anything else that already knows exactly which part it's affecting) marks its own
        // part-targeted damage with interruptsDoAfters: false - only re-route damage that doesn't already
        // know where it's going, so e.g. a surgical mishap's own ApplyPartDamage isn't randomly doubled here.
        if (!args.DamageIncreased || !args.InterruptsDoAfters || args.DamageDelta is not { } delta)
            return;

        var routed = new DamageSpecifier();
        foreach (var type in BruteTypes)
        {
            if (delta.DamageDict.TryGetValue(type, out var amount) && amount > 0)
                routed.DamageDict[type] = amount;
        }

        if (routed.Empty)
            return;

        if (TryPickHitPart(uid, out var part))
            _damageable.TryChangeDamage(part, routed * RoutedFraction, ignoreResistances: true, interruptsDoAfters: false);
    }

    /// <summary>Randomly picks one of the body's currently-attached structural parts, weighted by <see cref="PartWeights"/>.</summary>
    private bool TryPickHitPart(EntityUid body, out EntityUid part)
    {
        part = default;

        var candidates = new List<(EntityUid Part, float Weight)>();
        foreach (var candidate in _body.EnumerateParts(body))
        {
            if (!TryComp<OrganComponent>(candidate, out var organ) || organ.Category is not { } category)
                continue;

            foreach (var (weightCategory, weight) in PartWeights)
            {
                if (weightCategory != category)
                    continue;

                candidates.Add((candidate, weight));
                break;
            }
        }

        if (candidates.Count == 0)
            return false;

        var roll = _random.NextFloat() * candidates.Sum(c => c.Weight);

        foreach (var (candidatePart, weight) in candidates)
        {
            if (roll < weight)
            {
                part = candidatePart;
                return true;
            }

            roll -= weight;
        }

        part = candidates[^1].Part;
        return true;
    }
}
