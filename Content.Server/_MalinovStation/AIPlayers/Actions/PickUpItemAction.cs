using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Picks up a loose item the AI can currently see (spec section 15/18's first AI Inventory slice) into an
/// empty hand. Calls vanilla's own <see cref="SharedHandsSystem.TryPickupAnyHand"/> - the same "no swap/drop
/// to make room" pickup vanilla NPCs use (see e.g. <c>EquipOperator</c>), never reimplementing pickup logic
/// of its own. Fails cleanly (no swap/drop) if both hands are already full - see
/// <see cref="ItemOpportunityComponent"/>'s own doc comment for why the broader "manage a full inventory"
/// mechanic is deliberately out of scope for this slice.
/// </summary>
public sealed class PickUpItemAction : IAiAction
{
    public const string ActionName = "PickUpItem";

    private readonly IEntityManager _entManager;
    private readonly SharedHandsSystem _hands;
    private readonly SharedInteractionSystem _interaction;
    private readonly MobStateSystem _mobState;

    public PickUpItemAction(IEntityManager entManager, SharedHandsSystem hands, SharedInteractionSystem interaction, MobStateSystem mobState)
    {
        _entManager = entManager;
        _hands = hands;
        _interaction = interaction;
        _mobState = mobState;
    }

    public string Name => ActionName;
    public string Description => "Pick up a nearby loose item you can currently see into an empty hand.";

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not PickUpItemActionParams pickUp)
        {
            failReason = $"{Name} requires {nameof(PickUpItemActionParams)}.";
            return false;
        }

        if (_mobState.IsIncapacitated(uid))
        {
            failReason = "Entity is incapacitated.";
            return false;
        }

        if (!_entManager.TryGetComponent<ItemOpportunityComponent>(uid, out var opportunity))
        {
            failReason = "Entity has no awareness of nearby items.";
            return false;
        }

        if (FindCandidate(uid, opportunity, pickUp.Target) is not { } target)
        {
            failReason = $"There's nothing nearby called \"{pickUp.Target}\" I could pick up.";
            return false;
        }

        if (_entManager.Deleted(target) ||
            !_interaction.InRangeUnobstructed(uid, target, opportunity.ScanRadius, CollisionGroup.Opaque))
        {
            failReason = "That's not close enough anymore.";
            return false;
        }

        if (!_hands.TryGetEmptyHand(uid, out _))
        {
            failReason = "My hands are full.";
            return false;
        }

        if (!_hands.CanPickupAnyHand(uid, target, checkActionBlocker: true, showPopup: false))
        {
            failReason = "I can't pick that up.";
            return false;
        }

        failReason = null;
        return true;
    }

    public void Do(EntityUid uid, IAiActionParams parameters)
    {
        var pickUp = (PickUpItemActionParams)parameters;

        // Re-resolved rather than smuggled through from CanDo, same convention UseInteractableAction already
        // established - CanDo/Do are only ever called back-to-back by AiActionRegistrySystem.TryDoAction, so
        // this can't observe a different result than CanDo just confirmed.
        if (!_entManager.TryGetComponent<ItemOpportunityComponent>(uid, out var opportunity))
            return;

        if (FindCandidate(uid, opportunity, pickUp.Target) is not { } target)
            return;

        _hands.TryPickupAnyHand(uid, target);
    }

    /// <summary>Case-insensitive substring match against each currently-visible candidate's live entity
    /// name - same either-direction <c>Contains</c> idiom <see cref="UseInteractableAction.FindCandidate"/>
    /// uses, read fresh off the entity itself since this list is transient.</summary>
    private EntityUid? FindCandidate(EntityUid uid, ItemOpportunityComponent opportunity, string nameHint)
    {
        foreach (var candidate in opportunity.NearbyItems)
        {
            if (_entManager.Deleted(candidate))
                continue;

            var name = _entManager.GetComponent<MetaDataComponent>(candidate).EntityName;
            if (name.Contains(nameHint, StringComparison.OrdinalIgnoreCase) ||
                nameHint.Contains(name, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }
}
