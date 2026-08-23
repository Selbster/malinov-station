using System.Diagnostics.CodeAnalysis;
using Content.Server.Chat.Systems;
using Content.Shared.Chat;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Makes an AI player say a line in character, the same way HTN's SpeakOperator does for scripted mobs
/// (<see cref="ChatSystem.TrySendInGameICMessage"/>) - except the text isn't restricted to a localized
/// dataset, so this is what free-form/LLM-generated dialogue will go through in a later milestone.
/// </summary>
/// <remarks>
/// Takes its system dependencies via constructor (from <see cref="Systems.AiActionRegistrySystem"/>, which
/// as an EntitySystem can inject them normally) rather than [Dependency] fields: this is a plain POCO, not
/// an EntitySystem/HTNOperator, so it isn't part of the EntitySystemManager's own injection scope.
/// </remarks>
public sealed class TalkAction : IAiAction
{
    private readonly IEntityManager _entManager;
    private readonly ChatSystem _chat;
    private readonly MobStateSystem _mobState;
    private readonly IGameTiming _timing;

    private const float CooldownSeconds = 2f;
    private const int MaxLength = 500;

    public TalkAction(IEntityManager entManager, ChatSystem chat, MobStateSystem mobState, IGameTiming timing)
    {
        _entManager = entManager;
        _chat = chat;
        _mobState = mobState;
        _timing = timing;
    }

    public string Name => "Talk";
    public string Description => "Сказать что-то вслух, в характере персонажа.";
    public string Category => AiActionCategories.Social;
    public bool IsExtended => false;

    /// <summary>
    /// AI Players 0.5: always false. "Talk" is invoked internally by <see cref="Systems.SocialSystem"/> (via
    /// a direct <see cref="Systems.AiActionRegistrySystem.TryDoAction"/> call, bypassing eligibility entirely)
    /// and was never one of the actions <see cref="LLM.ActionProposalResolver"/> accepts from the LLM - see
    /// <see cref="Actions.TalkToAction"/> for the LLM-selectable equivalent. Before this fix it could still
    /// pass eligibility and appear as a real choice in the Action Selection prompt's Social category, so the
    /// LLM could pick "Talk" and have it rejected downstream as an invalid action proposal - confirmed live
    /// against a real Ollama model during this milestone's runtime validation.
    /// </summary>
    public bool IsEligible(EntityUid uid) => false;

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not TalkActionParams talk)
        {
            failReason = $"{Name} требует {nameof(TalkActionParams)}.";
            return false;
        }

        if (string.IsNullOrWhiteSpace(talk.Text))
        {
            failReason = "Текст не должен быть пустым.";
            return false;
        }

        if (talk.Text.Length > MaxLength)
        {
            failReason = $"Текст превышает лимит в {MaxLength} символов.";
            return false;
        }

        if (_mobState.IsIncapacitated(uid))
        {
            failReason = "Сущность недееспособна.";
            return false;
        }

        // Cooldown state lives on the entity (TalkCooldownComponent), not in a dictionary here - see
        // TalkCooldownComponent's doc comment for why that matters.
        if (_entManager.TryGetComponent<TalkCooldownComponent>(uid, out var cooldown) &&
            _timing.CurTime - cooldown.LastTalkAt < TimeSpan.FromSeconds(CooldownSeconds))
        {
            failReason = "Слишком рано для новой реплики после предыдущей.";
            return false;
        }

        failReason = null;
        return true;
    }

    public void Do(EntityUid uid, IAiActionParams parameters)
    {
        var talk = (TalkActionParams)parameters;
        _entManager.EnsureComponent<TalkCooldownComponent>(uid).LastTalkAt = _timing.CurTime;
        _chat.TrySendInGameICMessage(uid, talk.Text, InGameICChatType.Speak, hideChat: false);
    }
}
