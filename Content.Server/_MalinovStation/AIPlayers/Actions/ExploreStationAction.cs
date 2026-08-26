using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.NPC.HTN;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// AI Players 0.6.2, spec section 3: the missing executable half of exploration.
///
/// Everything upstream of this already worked - boredom grew, a Restlessness desire was raised, the LLM could
/// say "исследовать станцию" - but that intention was only ever a string written to
/// <see cref="Components.IntentComponent"/>; nothing dispatched on it, so the only way an AI could actually
/// travel was to separately pick <see cref="GoToKnownLocationAction"/> *and* name a place it remembered. This
/// action closes that gap: the motivation now has something concrete to become.
///
/// Deliberately parameterless. The model decides *whether* exploring is what it wants; where to go is
/// <see cref="ExplorationControllerSystem"/>'s deterministic job, so no coordinates, tiles or route details
/// are ever exposed to the LLM (spec sections 9 and 11). Execution reuses the exact
/// <see cref="MoveToAction.ForcedDestinationKey"/> HTN rails <see cref="GoToKnownLocationAction"/> uses - no
/// second navigation path (spec sections 18 and 34.6).
/// </summary>
public sealed class ExploreStationAction : IAiAction
{
    public const string ActionName = "ExploreStation";

    private readonly IEntityManager _entManager;
    private readonly MobStateSystem _mobState;
    private readonly ExplorationControllerSystem _exploration;
    private readonly IGameTiming _timing;

    public ExploreStationAction(
        IEntityManager entManager,
        MobStateSystem mobState,
        ExplorationControllerSystem exploration,
        IGameTiming timing)
    {
        _entManager = entManager;
        _mobState = mobState;
        _exploration = exploration;
        _timing = timing;
    }

    public string Name => ActionName;
    public string Description => "Исследовать станцию и найти новое или мало знакомое место.";
    public string Category => AiActionCategories.Movement;
    public bool IsExtended => true;

    /// <summary>
    /// Ineligible when the actor cannot act at all, when it is not HTN-driven (nothing would carry out the
    /// journey), while a recent fruitless attempt is still cooling down, and - the important one - when the
    /// controller genuinely cannot name anywhere to go. Spec section 28 requires that last case: an action the
    /// AI could not actually carry out must never reach the Action Selection prompt in the first place.
    /// </summary>
    public bool IsEligible(EntityUid uid)
    {
        if (!_entManager.HasComponent<HTNComponent>(uid) || _mobState.IsIncapacitated(uid))
            return false;

        if (_entManager.TryGetComponent<ExplorationComponent>(uid, out var exploration) &&
            _timing.CurTime < exploration.NoTargetCooldownUntil)
        {
            return false;
        }

        return _exploration.HasAnyTarget(uid);
    }

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not ExploreStationActionParams)
        {
            failReason = $"{Name} требует {nameof(ExploreStationActionParams)}.";
            return false;
        }

        if (!_entManager.HasComponent<HTNComponent>(uid))
        {
            failReason = "Сущность не управляется через HTN.";
            return false;
        }

        if (_mobState.IsIncapacitated(uid))
        {
            failReason = "Сущность недееспособна.";
            return false;
        }

        failReason = null;
        return true;
    }

    /// <summary>
    /// Picks a target and commits to walking there. Note that unlike most actions here, "CanDo passed" is
    /// genuinely no guarantee of success: the target is only chosen now, and there may be nowhere worth going.
    /// That is reported honestly as <see cref="AiActionOutcome.NoTarget"/> (spec section 15) and arms a short
    /// cooldown so the AI reconsiders something else instead of re-deciding to explore immediately (section 16).
    /// </summary>
    public AiActionResult Do(EntityUid uid, IAiActionParams parameters)
    {
        var exploration = _entManager.EnsureComponent<ExplorationComponent>(uid);

        if (!_exploration.TryGetTarget(uid, out var target))
        {
            exploration.NoTargetCooldownUntil = _timing.CurTime + TimeSpan.FromSeconds(exploration.NoTargetCooldownSeconds);
            exploration.CurrentTargetName = null;
            return AiActionResult.NoTarget("Ты не смог(ла) придумать, куда сейчас стоит пойти.");
        }

        exploration.CurrentTargetName = target.PlaceName;

        var htn = _entManager.GetComponent<HTNComponent>(uid);
        htn.Blackboard.SetValue(MoveToAction.ForcedDestinationKey, target.Coordinates);

        return AiActionResult.Started(target.Reason);
    }
}
