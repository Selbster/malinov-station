using System.Diagnostics.CodeAnalysis;
using Content.Server.NPC.HTN;
using Content.Shared.Mobs.Systems;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// Sends an AI player to a specific location right now, interrupting its routine HTN behaviour until it
/// arrives (see <c>ForcedMoveCompound</c> in Resources/Prototypes/_MalinovStation/AIPlayers/htn.yml, gated
/// on the <see cref="ForcedDestinationKey"/> blackboard key). Reuses the vanilla MoveToOperator/steering
/// stack entirely - this action only ever writes one blackboard value and lets HTN do the rest, including
/// automatically clearing that key on arrival (MoveToOperator's RemoveKeyOnFinish default).
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
    public string Description => "Walk to a specific location, interrupting routine behaviour until arrival.";

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not MoveToActionParams)
        {
            failReason = $"{Name} requires {nameof(MoveToActionParams)}.";
            return false;
        }

        if (!_entManager.TryGetComponent<HTNComponent>(uid, out _))
        {
            failReason = "Entity is not HTN-driven.";
            return false;
        }

        if (_mobState.IsIncapacitated(uid))
        {
            failReason = "Entity is incapacitated.";
            return false;
        }

        failReason = null;
        return true;
    }

    public void Do(EntityUid uid, IAiActionParams parameters)
    {
        var move = (MoveToActionParams)parameters;
        var htn = _entManager.GetComponent<HTNComponent>(uid);
        htn.Blackboard.SetValue(ForcedDestinationKey, move.Destination);
    }
}
