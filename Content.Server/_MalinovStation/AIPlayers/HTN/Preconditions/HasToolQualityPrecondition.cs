using Content.Server.NPC;
using Content.Server.NPC.HTN.Preconditions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Storage;
using Content.Shared.Tools.Systems;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Preconditions;

/// <summary>
/// IsMet when the owner has an item with the given tool <see cref="Quality"/> (e.g. "Welding") somewhere it
/// could plausibly retrieve it from: any hand, any directly-equipped inventory slot (belt, back, pockets,
/// ...), or inside a storage-capable item equipped in one of those slots (backpack/belt/pocket contents).
/// Only looks one container deep - a box inside the backpack containing the tool wouldn't be found, but a
/// normal engineer loadout (welder loose in the backpack/belt/a pocket) is. Gates RepairMachineCompound so an
/// AI player without the right tool anywhere doesn't walk up to a damaged machine and attempt (and silently
/// fail) an interaction it has no way to complete. See <see cref="Operators.ActivateHeldToolOperator"/> for
/// the matching fetch-into-hand-and-activate step.
/// </summary>
public sealed partial class HasToolQualityPrecondition : HTNPrecondition
{
    [Dependency] private IEntityManager _entManager = default!;
    private SharedHandsSystem _hands = default!;
    private InventorySystem _inventory = default!;
    private SharedToolSystem _tools = default!;

    [DataField(required: true)]
    public string Quality = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _hands = sysManager.GetEntitySystem<SharedHandsSystem>();
        _inventory = sysManager.GetEntitySystem<InventorySystem>();
        _tools = sysManager.GetEntitySystem<SharedToolSystem>();
    }

    public override bool IsMet(NPCBlackboard blackboard)
    {
        if (!blackboard.TryGetValue<EntityUid>(NPCBlackboard.Owner, out var owner, _entManager))
            return false;

        foreach (var handName in _hands.EnumerateHands(owner))
        {
            if (_hands.GetHeldItem(owner, handName) is { } held && _tools.HasQuality(held, Quality))
                return true;
        }

        if (!_entManager.TryGetComponent<InventoryComponent>(owner, out var inventory))
            return false;

        var slots = _inventory.GetSlotEnumerator((owner, inventory));
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } equipped)
                continue;

            if (_tools.HasQuality(equipped, Quality))
                return true;

            if (_entManager.TryGetComponent<StorageComponent>(equipped, out var storage))
            {
                foreach (var stored in storage.Container.ContainedEntities)
                {
                    if (_tools.HasQuality(stored, Quality))
                        return true;
                }
            }
        }

        return false;
    }
}
