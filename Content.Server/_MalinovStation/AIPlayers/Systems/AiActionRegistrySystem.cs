using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
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
    /// AI Players 0.4 Milestone 2: every registered action whose <see cref="IAiAction.IsEligible"/> currently
    /// returns true for <paramref name="uid"/> - the deterministic pre-filter run before the Cognitive LLM
    /// role is even asked anything, so an actor is never offered (and never has to discover post-hoc via a
    /// CanDo rejection) an action it plainly cannot attempt right now.
    /// </summary>
    public IReadOnlyList<IAiAction> GetEligibleActions(EntityUid uid)
    {
        var eligible = new List<IAiAction>();
        foreach (var action in _actions.Values)
        {
            if (action.IsEligible(uid))
                eligible.Add(action);
        }

        return eligible;
    }

    /// <summary>
    /// The distinct <see cref="AiActionCategories"/> with at least one currently-eligible action for
    /// <paramref name="uid"/> - what the Cognitive LLM role's prompt is built from
    /// (see <see cref="LLM.PromptBuilder.BuildCognitiveSystemPrompt"/>), instead of the fixed "all categories
    /// always" set.
    /// </summary>
    public IReadOnlySet<string> GetEligibleCategories(EntityUid uid)
    {
        var categories = new HashSet<string>();
        foreach (var action in GetEligibleActions(uid))
            categories.Add(action.Category);

        return categories;
    }

    /// <summary>
    /// Looks up <paramref name="actionName"/>, runs its CanDo check, and performs it if that passes.
    /// Returns false (with a reason) for an unknown action name, a failed precondition, or mismatched
    /// parameters - the caller is never able to bypass validation.
    /// </summary>
    public bool TryDoAction(EntityUid uid, string actionName, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason) =>
        TryDoAction(uid, actionName, parameters, reason: string.Empty, out failReason);

    /// <summary>
    /// AI Players 0.4 Milestone 5: same as the 4-argument overload, but also records <paramref name="reason"/>
    /// onto <see cref="AiBusyStateComponent"/> for an <see cref="IAiAction.IsExtended"/> action that succeeds -
    /// the LLM's own stated reason for this specific commitment (e.g. "need it for the repair"), not just the
    /// action's static <see cref="IAiAction.Description"/>, so <see cref="Systems.ContextBuilderSystem"/> can
    /// surface "I am currently doing X because Y" back to the Cognitive LLM role.
    /// </summary>
    public bool TryDoAction(EntityUid uid, string actionName, IAiActionParams parameters, string reason, [NotNullWhen(false)] out string? failReason)
    {
        if (!_actions.TryGetValue(actionName, out var action))
        {
            failReason = $"Unknown action \"{actionName}\".";
            return false;
        }

        if (!action.CanDo(uid, parameters, out failReason))
            return false;

        action.Do(uid, parameters);

        if (action.IsExtended && _entManager.TryGetComponent<AiBusyStateComponent>(uid, out var busy))
        {
            busy.CurrentAction = action.Name;
            busy.StartedAt = _timing.CurTime;
            busy.Reason = reason;
            busy.InterruptionNoticed = false;
        }

        failReason = null;
        return true;
    }
}
