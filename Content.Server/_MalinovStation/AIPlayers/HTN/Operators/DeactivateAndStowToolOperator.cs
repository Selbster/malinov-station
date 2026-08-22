using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Tools.Systems;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>
/// Single-shot operator: the inverse of <see cref="ActivateHeldToolOperator"/>, run after a repair finishes -
/// turns the held tool back off (if toggleable and still on) and tries to put it away into whatever
/// storage-capable slot the AI already has equipped (belt/backpack/pocket - the same containers
/// <see cref="ActivateHeldToolOperator.FindStored"/> searches when fetching one), instead of leaving it lit
/// and in-hand indefinitely. Always finishes - failing to find the tool in a hand, or nowhere free to stow it,
/// just leaves it as-is, a legitimate outcome, not a bug.
/// </summary>
public sealed partial class DeactivateAndStowToolOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    private SharedHandsSystem _hands = default!;
    private InventorySystem _inventory = default!;
    private SharedToolSystem _tools = default!;
    private ItemToggleSystem _toggle = default!;
    private SharedStorageSystem _storage = default!;

    [DataField(required: true)]
    public string Quality = default!;

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _hands = sysManager.GetEntitySystem<SharedHandsSystem>();
        _inventory = sysManager.GetEntitySystem<InventorySystem>();
        _tools = sysManager.GetEntitySystem<SharedToolSystem>();
        _toggle = sysManager.GetEntitySystem<ItemToggleSystem>();
        _storage = sysManager.GetEntitySystem<SharedStorageSystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (FindInAnyHand(owner) is not { } toolItem)
            return HTNOperatorStatus.Finished;

        if (_toggle.IsActivated(toolItem))
            _toggle.TryDeactivate(toolItem, owner, predicted: false);

        TryStow(owner, toolItem);

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

    /// <summary>Mirrors ActivateHeldToolOperator.FindStored's own search scope (equipped slots' own
    /// StorageComponent contents), just inserting instead of retrieving.</summary>
    private void TryStow(EntityUid owner, EntityUid toolItem)
    {
        if (!_entManager.TryGetComponent<InventoryComponent>(owner, out var inventory))
            return;

        var slots = _inventory.GetSlotEnumerator((owner, inventory));
        while (slots.MoveNext(out var slot))
        {
            if (slot.ContainedEntity is not { } equipped ||
                !_entManager.HasComponent<StorageComponent>(equipped))
            {
                continue;
            }

            if (_storage.CanInsert(equipped, toolItem, out _) &&
                _storage.Insert(equipped, toolItem, out _))
            {
                return;
            }
        }
    }
}
