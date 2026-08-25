using System.Diagnostics.CodeAnalysis;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// AI Players 0.5.1: eats or drinks whatever's currently in the AI's active hand - the spec's Hunger
/// end-to-end scenario needs "found food -&gt; grabbed it -&gt; ate it -&gt; hunger drops" to actually be
/// possible, and until now nothing in the registry could resolve that last step. Fires the exact same
/// <see cref="SharedHandsSystem.TryUseItemInHand"/> path a real player's "use in hand" key drives, which
/// <see cref="IngestionSystem"/> already picks up on its own (<c>OnUseEdibleInHand</c> -&gt; the real ingestion
/// <c>DoAfter</c>) - no eating logic reimplemented here.
///
/// Deliberately doesn't rely on vanilla's own HTN <c>FoodCompound</c>/<c>AltInteractOperator</c> path instead -
/// <c>NeedRecoveryTests.Hunger_ResolvesWhenEdibleFoodIsActuallyReachable</c> already found (and left
/// <c>[Explicit]</c>-disabled as a known, documented failure) that path doesn't reliably fire during real async
/// HTN planning even when food is reachable. A cognitive AI needs its own deterministic way to resolve hunger
/// and thirst instead of inheriting that legacy gap. <see cref="EdibleComponent"/> is this fork's unified
/// food/drink marker (see its own doc comment / <see cref="IngestionSystem"/>'s remarks), so this one action
/// closes the loop for both.
/// </summary>
/// <remarks>
/// Takes its dependencies via constructor rather than [Dependency] fields - see <see cref="TalkAction"/>'s
/// remarks for why.
/// </remarks>
public sealed class EatOrDrinkAction : IAiAction
{
    public const string ActionName = "EatOrDrink";

    private readonly IEntityManager _entManager;
    private readonly SharedHandsSystem _hands;
    private readonly IngestionSystem _ingestion;
    private readonly MobStateSystem _mobState;

    public EatOrDrinkAction(IEntityManager entManager, SharedHandsSystem hands, IngestionSystem ingestion, MobStateSystem mobState)
    {
        _entManager = entManager;
        _hands = hands;
        _ingestion = ingestion;
        _mobState = mobState;
    }

    public string Name => ActionName;
    public string Description => "Съесть или выпить то, что сейчас у тебя в активной руке.";
    public string Category => AiActionCategories.Work;
    public bool IsExtended => false;

    public bool IsEligible(EntityUid uid)
    {
        return !_mobState.IsIncapacitated(uid) &&
            _hands.GetActiveItem(uid) is { } held &&
            _entManager.HasComponent<EdibleComponent>(held);
    }

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not EatOrDrinkActionParams)
        {
            failReason = $"{Name} требует {nameof(EatOrDrinkActionParams)}.";
            return false;
        }

        if (_mobState.IsIncapacitated(uid))
        {
            failReason = "Сущность недееспособна.";
            return false;
        }

        if (_hands.GetActiveItem(uid) is not { } held || !_entManager.HasComponent<EdibleComponent>(held))
        {
            failReason = "У меня в руке нет ничего съедобного или питьевого.";
            return false;
        }

        if (!_ingestion.CanConsume(uid, held))
        {
            failReason = "Сейчас я не могу это съесть или выпить.";
            return false;
        }

        failReason = null;
        return true;
    }

    public void Do(EntityUid uid, IAiActionParams parameters)
    {
        // Re-resolved rather than smuggled through from CanDo, same convention every other action here
        // already established - CanDo/Do are only ever called back-to-back by
        // AiActionRegistrySystem.TryDoAction, so this can't observe a different result than CanDo just
        // confirmed. Fire-and-forget: the actual nutrient transfer happens via vanilla's own DoAfter,
        // exactly like a real player's "use in hand" key press.
        _hands.TryUseItemInHand(uid);
    }
}
