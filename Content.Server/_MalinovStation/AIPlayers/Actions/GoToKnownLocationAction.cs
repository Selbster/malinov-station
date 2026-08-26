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
    public string Description => "Дойти до места, которое ты помнишь по названию, прервав рутинное поведение до прибытия.";
    public string Category => AiActionCategories.Movement;
    public bool IsExtended => true;

    public bool IsEligible(EntityUid uid)
    {
        return _entManager.HasComponent<HTNComponent>(uid) &&
            !_mobState.IsIncapacitated(uid) &&
            _memory.HasAnyKnownDestination(uid);
    }

    public bool CanDo(EntityUid uid, IAiActionParams parameters, [NotNullWhen(false)] out string? failReason)
    {
        if (parameters is not GoToKnownLocationActionParams goTo)
        {
            failReason = $"{Name} требует {nameof(GoToKnownLocationActionParams)}.";
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

        if (_memory.FindKnownLocation(uid, goTo.LocationHint) is null)
        {
            failReason = $"Я не знаю места под названием «{goTo.LocationHint}».";
            return false;
        }

        failReason = null;
        return true;
    }

    public AiActionResult Do(EntityUid uid, IAiActionParams parameters)
    {
        var goTo = (GoToKnownLocationActionParams)parameters;

        // Re-resolved rather than smuggled through from CanDo, same convention PursueGoalAction already
        // established - CanDo/Do are only ever called back-to-back by AiActionRegistrySystem.TryDoAction, so
        // this can't observe a different result than CanDo just confirmed.
        if (_memory.FindKnownLocation(uid, goTo.LocationHint) is not { } destination)
            return AiActionResult.NoTarget($"Ты не помнишь, где находится «{goTo.LocationHint}».");

        var htn = _entManager.GetComponent<HTNComponent>(uid);
        htn.Blackboard.SetValue(MoveToAction.ForcedDestinationKey, destination);
        return AiActionResult.Started($"Ты направляешься к «{goTo.LocationHint}».");
    }
}
