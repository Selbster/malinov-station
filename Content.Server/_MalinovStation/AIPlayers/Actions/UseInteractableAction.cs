using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Physics;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Uses a single-step, no-UI interactable object the AI can currently see (spec section 16's first AI
/// Interaction slice) - e.g. flips a wall light switch. Calls the exact same vanilla entry point
/// <c>Content.Server.NPC.HTN.PrimitiveTasks.Operators.Interactions.InteractWithOperator</c> already uses for
/// scripted NPCs (<see cref="SharedInteractionSystem.UserInteraction"/>), never reimplementing interaction
/// logic of its own.
/// </summary>
public sealed class UseInteractableAction : IAiAction
{
    public const string ActionName = "UseInteractable";

    private readonly IEntityManager _entManager;
    private readonly SharedInteractionSystem _interaction;
    private readonly MobStateSystem _mobState;

    public UseInteractableAction(IEntityManager entManager, SharedInteractionSystem interaction, MobStateSystem mobState)
    {
        _entManager = entManager;
        _interaction = interaction;
        _mobState = mobState;
    }

    public string Name => ActionName;
    public string Description => "Use a nearby interactable object you can currently see, e.g. a switch.";

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not UseInteractableActionParams use)
        {
            failReason = $"{Name} requires {nameof(UseInteractableActionParams)}.";
            return false;
        }

        if (_mobState.IsIncapacitated(uid))
        {
            failReason = "Entity is incapacitated.";
            return false;
        }

        if (!_entManager.TryGetComponent<InteractionOpportunityComponent>(uid, out var opportunity))
        {
            failReason = "Entity has no awareness of nearby interactables.";
            return false;
        }

        if (FindCandidate(uid, opportunity, use.Target) is not { } target)
        {
            failReason = $"There's nothing nearby called \"{use.Target}\" I could interact with.";
            return false;
        }

        if (_entManager.Deleted(target) ||
            !_interaction.InRangeUnobstructed(uid, target, opportunity.ScanRadius, CollisionGroup.Opaque))
        {
            failReason = "That's not close enough anymore.";
            return false;
        }

        failReason = null;
        return true;
    }

    public void Do(EntityUid uid, IAiActionParams parameters)
    {
        var use = (UseInteractableActionParams)parameters;

        // Re-resolved rather than smuggled through from CanDo, same convention PursueGoalAction/
        // GoToKnownLocationAction already established - CanDo/Do are only ever called back-to-back by
        // AiActionRegistrySystem.TryDoAction, so this can't observe a different result than CanDo just
        // confirmed.
        if (!_entManager.TryGetComponent<InteractionOpportunityComponent>(uid, out var opportunity))
            return;

        if (FindCandidate(uid, opportunity, use.Target) is not { } target)
            return;

        var coordinates = _entManager.GetComponent<TransformComponent>(uid).Coordinates;
        _interaction.UserInteraction(uid, coordinates, target);
    }

    /// <summary>Case-insensitive substring match against each currently-visible candidate's live entity
    /// name - same either-direction <c>Contains</c> idiom <see cref="Systems.MemorySystem.FindKnownLocation"/>
    /// uses, but read fresh off the entity itself (never cached), since this list is transient and unlike a
    /// remembered landmark's name, nothing here is meant to be stable over time.</summary>
    private EntityUid? FindCandidate(EntityUid uid, InteractionOpportunityComponent opportunity, string nameHint)
    {
        foreach (var candidate in opportunity.NearbyInteractables)
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
