using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.NPC.HTN;
using Content.Shared.Mobs.Systems;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Starts a coordinate-based journey through <see cref="AiBusyStateSystem"/>. ForcedMoveCompound hands it
/// to vanilla steering; the busy-state system owns cancellation and the final result.
/// </summary>
/// <remarks>
/// Takes its dependencies via constructor rather than [Dependency] fields - see <see cref="TalkAction"/>'s
/// remarks for why.
/// </remarks>
public sealed class MoveToAction : IAiAction
{
    private readonly IEntityManager _entManager;
    private readonly MobStateSystem _mobState;

    /// <summary>
    /// Blackboard key the HTN branch and this action agree on. Distinct from MoveToOperator's default
    /// "TargetCoordinates" key so a forced move never collides with whatever routine movement (e.g. Rest,
    /// Idle wandering) is already using that key.
    /// </summary>
    public const string ForcedDestinationKey = "ForcedDestination";

    public MoveToAction(IEntityManager entManager, MobStateSystem mobState)
    {
        _entManager = entManager;
        _mobState = mobState;
    }

    public string Name => "MoveTo";
    public string Description => "Дойти до конкретного места, прервав рутинное поведение до прибытия.";
    public string Category => AiActionCategories.Movement;
    public bool IsExtended => true;

    /// <summary>Driven programmatically (tests, other systems) with real coordinates - never a sensible
    /// thing for a language model to name. See <see cref="IAiAction.IsLlmSelectable"/>.</summary>
    public bool IsLlmSelectable => false;

    public bool IsEligible(EntityUid uid)
    {
        return _entManager.HasComponent<HTNComponent>(uid) && !_mobState.IsIncapacitated(uid);
    }

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not MoveToActionParams)
        {
            failReason = $"{Name} требует {nameof(MoveToActionParams)}.";
            return false;
        }

        if (!_entManager.TryGetComponent<HTNComponent>(uid, out _))
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

    public AiActionResult Do(EntityUid uid, IAiActionParams parameters)
    {
        var move = (MoveToActionParams)parameters;
        _entManager.System<AiBusyStateSystem>().StartJourney(uid, Name, move.Destination);
        return AiActionResult.Started();
    }
}
