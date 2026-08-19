using System.Diagnostics.CodeAnalysis;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.NPC.HTN;
using Content.Shared.Mobs.Systems;

namespace Content.Server._MalinovStation.AIPlayers.Actions;

/// <summary>
/// AI Navigation Controller v1: sends an AI player to a place it actually remembers by name (resolved via
/// <see cref="MemorySystem.FindKnownLocation"/> - never literal coordinates the LLM invented), reusing
/// <see cref="MoveToAction"/>'s exact execution mechanism (<see cref="MoveToAction.ForcedDestinationKey"/> /
/// <c>ForcedMoveCompound</c>) once a destination is resolved. This is what makes <see cref="MoveToAction"/>
/// itself LLM-selectable in spirit without exposing raw coordinates to the LLM: the AI can only ever go
/// somewhere it has personally perceived (see <see cref="Systems.LandmarkPerceptionSystem"/>).
/// </summary>
/// <remarks>
/// Takes its dependencies via constructor rather than [Dependency] fields - see <see cref="TalkAction"/>'s
/// remarks for why.
/// </remarks>
public sealed class GoToKnownLocationAction : IAiAction
{
    public const string ActionName = "GoToKnownLocation";

    private readonly IEntityManager _entManager;
    private readonly MobStateSystem _mobState;
    private readonly MemorySystem _memory;

    public GoToKnownLocationAction(IEntityManager entManager, MobStateSystem mobState, MemorySystem memory)
    {
        _entManager = entManager;
        _mobState = mobState;
        _memory = memory;
    }

    public string Name => ActionName;
    public string Description => "Walk to a place you remember by name, interrupting routine behaviour until arrival.";

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not GoToKnownLocationActionParams goTo)
        {
            failReason = $"{Name} requires {nameof(GoToKnownLocationActionParams)}.";
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

        if (_memory.FindKnownLocation(uid, goTo.LocationHint) is null)
        {
            failReason = $"I don't know of any place called \"{goTo.LocationHint}\".";
            return false;
        }

        failReason = null;
        return true;
    }

    public void Do(EntityUid uid, IAiActionParams parameters)
    {
        var goTo = (GoToKnownLocationActionParams)parameters;

        // Re-resolved rather than smuggled through from CanDo, same convention PursueGoalAction already
        // established - CanDo/Do are only ever called back-to-back by AiActionRegistrySystem.TryDoAction, so
        // this can't observe a different result than CanDo just confirmed.
        if (_memory.FindKnownLocation(uid, goTo.LocationHint) is not { } destination)
            return;

        var htn = _entManager.GetComponent<HTNComponent>(uid);
        htn.Blackboard.SetValue(MoveToAction.ForcedDestinationKey, destination);
    }
}
