using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server.Chat.Systems;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// The AI Players action registry (spec section 18): a whitelist of named, validated actions an AI player
/// can be told to perform right now. This is the security boundary from spec section 19 - nothing outside
/// this registry can make an AI player do anything, an unknown name is always rejected, and every action's
/// own CanDo runs before Do. Distinct from the Goal System (Milestone 2-4): goals are continuous priorities
/// ("what should I be doing"), actions are one-shot imperative commands ("do this specific thing now").
/// </summary>
public sealed partial class AiActionRegistrySystem : EntitySystem
{
    [Dependency] private ChatSystem _chat = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private GoalSystem _goal = default!;
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedContainerSystem _container = default!;
    [Dependency] private SocialSystem _social = default!;

    private readonly Dictionary<string, IAiAction> _actions = new();

    public IReadOnlyCollection<IAiAction> AllActions => _actions.Values;

    public override void Initialize()
    {
        base.Initialize();

        // Actions are plain POCOs (not EntitySystems), so they take their system dependencies via
        // constructor here rather than [Dependency] fields of their own - see TalkAction's remarks.
        Register(new TalkAction(_entManager, _chat, _mobState, _timing));
        Register(new MoveToAction(_entManager, _mobState));
        Register(new PursueGoalAction(_entManager, _goal));
        Register(new ContinueActivityAction());
        Register(new GoToKnownLocationAction(_entManager, _mobState, _memory));
        Register(new UseInteractableAction(_entManager, _interaction, _mobState));
        Register(new PickUpItemAction(_entManager, _hands, _interaction, _mobState));
        Register(new SearchAreaAction(_entManager, _lookup, _interaction, _container, _memory, _mobState));
        Register(new TalkToAction(_entManager, _social, _mobState));
    }

    private void Register(IAiAction action)
    {
        _actions[action.Name] = action;
    }

    /// <summary>
    /// Looks up <paramref name="actionName"/>, runs its CanDo check, and performs it if that passes.
    /// Returns false (with a reason) for an unknown action name, a failed precondition, or mismatched
    /// parameters - the caller is never able to bypass validation.
    /// </summary>
    public bool TryDoAction(EntityUid uid, string actionName, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (!_actions.TryGetValue(actionName, out var action))
        {
            failReason = $"Unknown action \"{actionName}\".";
            return false;
        }

        if (!action.CanDo(uid, parameters, out failReason))
            return false;

        action.Do(uid, parameters);
        failReason = null;
        return true;
    }
}
