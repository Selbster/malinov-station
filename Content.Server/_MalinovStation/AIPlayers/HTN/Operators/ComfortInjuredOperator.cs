using Content.Server._MalinovStation.AIPlayers.Actions;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server._MalinovStation.AIPlayers.Systems;
using Content.Server.NPC;
using Content.Server.NPC.HTN;
using Content.Server.NPC.HTN.PrimitiveTasks;
using Content.Shared.Dataset;
using Content.Shared.Random.Helpers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.HTN.Operators;

/// <summary>
/// Says a concerned line to the owner's <see cref="DangerComponent.NearbyInjured"/> character (via
/// Milestone 5's Talk action) and records the memory. Self-throttled via
/// <see cref="DangerComponent.ComfortedAt"/> so the same person doesn't get repeated concern every
/// replan cycle while the AI player stands next to them.
/// </summary>
public sealed partial class ComfortInjuredOperator : HTNOperator
{
    [Dependency] private IEntityManager _entManager = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IGameTiming _timing = default!;
    private AiActionRegistrySystem _actions = default!;
    private MemorySystem _memory = default!;

    private static readonly ProtoId<LocalizedDatasetPrototype> ComfortLines = "AIPlayerComfortLines";

    public override void Initialize(IEntitySystemManager sysManager)
    {
        base.Initialize(sysManager);
        _actions = sysManager.GetEntitySystem<AiActionRegistrySystem>();
        _memory = sysManager.GetEntitySystem<MemorySystem>();
    }

    public override HTNOperatorStatus Update(NPCBlackboard blackboard, float frameTime)
    {
        var owner = blackboard.GetValue<EntityUid>(NPCBlackboard.Owner);

        if (!_entManager.TryGetComponent<DangerComponent>(owner, out var danger) ||
            danger.NearbyInjured is not { } injured ||
            _entManager.Deleted(injured))
        {
            return HTNOperatorStatus.Finished;
        }

        if (danger.ComfortedAt.TryGetValue(injured, out var last) &&
            _timing.CurTime - last < TimeSpan.FromSeconds(danger.ComfortCooldownSeconds))
        {
            return HTNOperatorStatus.Finished;
        }

        danger.ComfortedAt[injured] = _timing.CurTime;

        var line = _random.Pick(_proto.Index(ComfortLines));
        if (_actions.TryDoAction(owner, "Talk", new TalkActionParams(line), out _))
        {
            _memory.AddMemory(
                owner,
                content: $"Checked on {_entManager.GetComponent<MetaDataComponent>(injured).EntityName}, who was badly hurt.",
                importance: 0.3f,
                source: "danger",
                participants: new[] { injured },
                emotionalWeight: 0.1f);
        }

        return HTNOperatorStatus.Finished;
    }
}
