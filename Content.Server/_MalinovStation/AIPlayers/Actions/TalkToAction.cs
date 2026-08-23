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
    public string Description => "Начать настоящий разговор с тем, кого ты сейчас видишь.";
    public string Category => AiActionCategories.Social;
    public bool IsExtended => false;

    public bool IsEligible(EntityUid uid)
    {
        if (_mobState.IsIncapacitated(uid))
            return false;

        if (!_entManager.TryGetComponent<ConversationComponent>(uid, out var ownConversation) ||
            ownConversation.State != ConversationState.None || ownConversation.Partner != null)
        {
            return false;
        }

        if (!_entManager.TryGetComponent<PerceptionComponent>(uid, out var perception) || perception.LastObservation is not { } observation)
            return false;

        // Not just "can I see anyone" - at least one visible character must actually be free to talk, same
        // check CanDo/FindTarget would eventually make for a specific pick, done here without a name hint.
        foreach (var candidate in observation.VisibleCharacters)
        {
            if (_entManager.Deleted(candidate))
                continue;

            if (_entManager.TryGetComponent<ConversationComponent>(candidate, out var targetConversation) &&
                targetConversation.State == ConversationState.None && targetConversation.Partner == null)
            {
                return true;
            }
        }

        return false;
    }

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not TalkToActionParams talkTo)
        {
            failReason = $"{Name} требует {nameof(TalkToActionParams)}.";
            return false;
        }

        if (_mobState.IsIncapacitated(uid))
        {
            failReason = "Сущность недееспособна.";
            return false;
        }

        if (!_entManager.TryGetComponent<ConversationComponent>(uid, out var ownConversation))
        {
            failReason = "У сущности нет возможности вести разговор.";
            return false;
        }

        if (ownConversation.State != ConversationState.None || ownConversation.Partner != null)
        {
            failReason = "Я уже веду разговор.";
            return false;
        }

        if (!_entManager.TryGetComponent<PerceptionComponent>(uid, out var perception) || perception.LastObservation is not { } observation)
        {
            failReason = "Мне не с кем поговорить - я никого не вижу.";
            return false;
        }

        if (FindTarget(observation, talkTo.Target) is not { } target)
        {
            failReason = $"Я не вижу поблизости никого под именем «{talkTo.Target}».";
            return false;
        }

        if (!_entManager.TryGetComponent<ConversationComponent>(target, out var targetConversation))
        {
            failReason = $"С {_entManager.GetComponent<MetaDataComponent>(target).EntityName} так поговорить не получится.";
            return false;
        }

        if (targetConversation.State != ConversationState.None || targetConversation.Partner != null)
        {
            failReason = $"{_entManager.GetComponent<MetaDataComponent>(target).EntityName} сейчас занят(а).";
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
