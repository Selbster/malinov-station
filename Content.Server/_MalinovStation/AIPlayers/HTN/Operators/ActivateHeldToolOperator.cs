using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Storage;
using Content.Shared.Tools.Systems;
using Robust.Shared.Containers;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>
/// Single-shot operator: finds an item with the given tool <see cref="Quality"/> - checking hands first,
/// then equipped inventory slots (belt, back, pockets, ...) and one level into the contents of any
/// storage-capable item equipped there (backpack/belt/pocket contents, matching
/// <see cref="Preconditions.HasToolQualityPrecondition"/>'s own search) - fetches it into a hand if it
/// wasn't already in one, switches the owner's active hand to it if needed, and activates it if it's
/// toggleable and not already on. A freshly spawned/picked-up welder starts unlit, same as it would for a
/// real player, so RepairMachineCompound needs this before InteractWithOperator (which only ever acts
/// through the active hand) or the repair attempt would fail silently against an unlit tool, one sitting in
/// the wrong hand, or one still packed away. Always finishes (never fails the branch) - if nothing is found,
/// or retrieval/activation doesn't succeed (e.g. hands full, no fuel), the following InteractWithOperator
/// step will simply fail to repair anything, which is a legitimate outcome, not a bug.
/// </summary>
public sealed partial class ActivateHeldToolOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private SharedHandsSystem _hands = default!;
    private InventorySystem _inventory = default!;
    private SharedToolSystem _tools = default!;
    private ItemToggleSystem _toggle = default!;
    private SharedContainerSystem _container = default!;

    [DataField(required: true)]
    public string Quality = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _hands = sysManager.GetEntitySystem<SharedHandsSystem>();
        _inventory = sysManager.GetEntitySystem<InventorySystem>();
        _tools = sysManager.GetEntitySystem<SharedToolSystem>();
        _toggle = sysManager.GetEntitySystem<ItemToggleSystem>();
        _container = sysManager.GetEntitySystem<SharedContainerSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        var item = FindInAnyHand(owner);
        if (item is null && FindStored(owner) is { } found)
        {
            _container.Remove(found.Item, found.Container);
            if (_hands.TryPickup(owner, found.Item))
                item = found.Item;
        }

        if (item is not { } toolItem)
            return HTNOperatorStatus.Finished;

        if (TryFindHand(owner, toolItem, out var handName) && _hands.GetActiveHand(owner) != handName)
            _hands.TrySetActiveHand(owner, handName);

        if (!_toggle.IsActivated(toolItem))
            _toggle.TryActivate(toolItem, owner, predicted: false);

        return HTNOperatorStatus.Finished;
    }

    private EntityUid? FindInAnyHand(EntityUid owner)
    {
        foreach (var handName in _hands.EnumerateHands(owner))
        {
            if (_hands.GetHeldItem(owner, handName) is { } held && _tools.HasQuality(held, Quality))
                return held;
        }

        return null;
    }

    private bool TryFindHand(EntityUid owner, EntityUid item, out string handName)
    {
        foreach (var name in _hands.EnumerateHands(owner))
        {
            if (_hands.GetHeldItem(owner, name) == item)
            {
                handName = name;
                return true;
            }
        }

        handName = string.Empty;
        return false;
    }

    private (EntityUid Item, BaseContainer Container)? FindStored(EntityUid owner)
    {
        if (!_entManager.TryGetComponent<InventoryComponent>(owner, out var inventory))
            return null;

        var slots = _inventory.GetSlotEnumerator((owner, inventory));
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } equipped)
                continue;

            if (_tools.HasQuality(equipped, Quality))
                return (equipped, slot);

            if (_entManager.TryGetComponent<StorageComponent>(equipped, out var storage))
            {
                foreach (var stored in storage.Container.ContainedEntities)
                {
                    if (_tools.HasQuality(stored, Quality))
                        return (stored, storage.Container);
                }
            }
        }

        return null;
    }
}
