using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Shared.Mobs.Systems;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Deliberately starts a real conversation with someone the AI can currently see (spec section 15's AI Social
/// slice) - e.g. "I want to ask Sarah what happened" (spec section 4's own worked example). Calls
/// <see cref="SocialSystem.TryInitiateConversation"/>, the exact same entry point
/// <see cref="SocialSystem"/>'s own reactive small-talk trigger uses, so line generation, the localized
/// fallback, memory/relationship write-back and rumor spreading all keep working identically - this action
/// only adds a deliberate, LLM-chosen "who and why" in front of that existing pipeline, never reimplementing
/// it.
/// </summary>
public sealed class TalkToAction : IAiAction
{
    public const string ActionName = "TalkTo";

    private readonly IEntityManager _entManager;
    private readonly SocialSystem _social;
    private readonly MobStateSystem _mobState;

    public TalkToAction(IEntityManager entManager, SocialSystem social, MobStateSystem mobState)
    {
        _entManager = entManager;
        _social = social;
        _mobState = mobState;
    }

    public string Name => ActionName;
    public string Description => "Start a real conversation with someone you can currently see.";

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not TalkToActionParams talkTo)
        {
            failReason = $"{Name} requires {nameof(TalkToActionParams)}.";
            return false;
        }

        if (_mobState.IsIncapacitated(uid))
        {
            failReason = "Entity is incapacitated.";
            return false;
        }

        if (!_entManager.TryGetComponent<ConversationComponent>(uid, out var ownConversation))
        {
            failReason = "Entity has no way to hold a conversation.";
            return false;
        }

        if (ownConversation.State != ConversationState.None || ownConversation.Partner != null)
        {
            failReason = "I'm already in the middle of a conversation.";
            return false;
        }

        if (!_entManager.TryGetComponent<PerceptionComponent>(uid, out var perception) || perception.LastObservation is not { } observation)
        {
            failReason = "I don't see anyone to talk to.";
            return false;
        }

        if (FindTarget(observation, talkTo.Target) is not { } target)
        {
            failReason = $"I don't see anyone nearby called \"{talkTo.Target}\".";
            return false;
        }

        if (!_entManager.TryGetComponent<ConversationComponent>(target, out var targetConversation))
        {
            failReason = $"{_entManager.GetComponent<MetaDataComponent>(target).EntityName} isn't someone I can talk to like that.";
            return false;
        }

        if (targetConversation.State != ConversationState.None || targetConversation.Partner != null)
        {
            failReason = $"{_entManager.GetComponent<MetaDataComponent>(target).EntityName} is busy right now.";
            return false;
        }

        failReason = null;
        return true;
    }

    public void Do(EntityUid uid, IAiActionParams parameters)
    {
        var talkTo = (TalkToActionParams)parameters;

        // Re-resolved rather than smuggled through from CanDo, same convention every other action here
        // already established.
        if (!_entManager.TryGetComponent<PerceptionComponent>(uid, out var perception) || perception.LastObservation is not { } observation)
            return;

        if (FindTarget(observation, talkTo.Target) is not { } target)
            return;

        _social.TryInitiateConversation(uid, target, talkTo.Reason);
    }

    /// <summary>Case-insensitive substring match against each currently-visible character's live entity
    /// name - same either-direction <c>Contains</c> idiom every other action's <c>FindCandidate</c> uses,
    /// reusing the already-existing passive character perception rather than a new opportunity component.</summary>
    private EntityUid? FindTarget(Perception.WorldObservation observation, string nameHint)
    {
        foreach (var candidate in observation.VisibleCharacters)
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
