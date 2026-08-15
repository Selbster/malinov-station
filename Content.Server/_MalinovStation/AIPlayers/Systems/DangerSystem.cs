using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Shared.Damage.Systems;
using Content.Shared.Mobs.Systems;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Detects the two "dynamic danger events" AI players react to (spec Milestone 7):
/// <list type="bullet">
/// <item>Being attacked (via <see cref="DamageChangedEvent"/>) - raises Safety, records a memory, and turns
/// the attacker's relationship negative (this is where Milestone 6's "only ever positive" relationship
/// nudges get a reason to go the other way).</item>
/// <item>Seeing another character incapacitated nearby (a periodic scan of what Perception already
/// recorded) - drives the HelpInjured goal.</item>
/// </list>
/// Populates <see cref="DangerComponent"/>; <see cref="GoalSystem"/> reads it to prioritize Flee/HelpInjured.
/// </summary>
public sealed partial class DangerSystem : EntitySystem
{
    [Dependency] private NeedsSystem _needs = default!;
    [Dependency] private MemorySystem _memory = default!;
    [Dependency] private RelationshipSystem _relationships = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private AiLodSystem _lod = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = _logManager.GetSawmill("aiplayers.danger");

        SubscribeLocalEvent<AIPlayerComponent, DamageChangedEvent>(OnDamaged);
    }

    private void OnDamaged(EntityUid uid, AIPlayerComponent component, DamageChangedEvent args)
    {
        if (!args.DamageIncreased)
            return;

        if (!TryComp<DangerComponent>(uid, out var danger) || !TryComp<GoalComponent>(uid, out var goal))
            return;

        _needs.ModifySafety(uid, 0.6f);

        var origin = args.Origin;
        var isSelfInflicted = origin == uid;

        _memory.AddMemory(
            uid,
            content: origin is { } attacker && !isSelfInflicted
                ? $"Was attacked by {Comp<MetaDataComponent>(attacker).EntityName}!"
                : "Was hurt!",
            importance: 0.7f,
            source: "danger",
            participants: origin is { } o ? new[] { o } : null,
            emotionalWeight: -0.8f);

        if (origin is { } attackerUid && !isSelfInflicted && !Deleted(attackerUid))
        {
            danger.ThreatSource = attackerUid;
            danger.ThreatExpiresAt = _timing.CurTime + TimeSpan.FromSeconds(danger.ThreatDurationSeconds);

            _relationships.ModifyRelationship(
                uid,
                attackerUid,
                trustDelta: -0.3f,
                friendshipDelta: -0.2f,
                fearDelta: 0.3f,
                angerDelta: 0.2f);

            _sawmill.Info($"[AI:{ToPrettyString(uid)}] Danger: attacked by {ToPrettyString(attackerUid)}.");
        }

        // Being attacked overrides whatever the LLM last told this AI player to do - self-preservation
        // takes priority over a stale directive, and the next GoalSystem tick should react immediately
        // rather than waiting out the routine reconsider cooldown.
        goal.IsLlmOverride = false;
        goal.ReconsiderAccumulator = 0f;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<DangerComponent, PerceptionComponent>();
        while (query.MoveNext(out var uid, out var danger, out var perception))
        {
            if (danger.ThreatSource is not null && _timing.CurTime >= danger.ThreatExpiresAt)
                danger.ThreatSource = null;

            danger.ScanAccumulator -= frameTime;
            if (danger.ScanAccumulator > 0f)
                continue;

            // Only the routine "look for someone hurt nearby" scan is LOD-scaled - the attack reaction in
            // OnDamaged above is event-driven and always fires immediately regardless of LOD.
            danger.ScanAccumulator = danger.ScanCooldown * _lod.GetMultiplier(uid);
            ScanForInjured(uid, danger, perception);
        }
    }

    private void ScanForInjured(EntityUid uid, DangerComponent danger, PerceptionComponent perception)
    {
        danger.NearbyInjured = null;

        if (perception.LastObservation is not { } observation)
            return;

        foreach (var other in observation.VisibleCharacters)
        {
            if (other == uid || Deleted(other))
                continue;

            if (!_mobState.IsIncapacitated(other))
                continue;

            danger.NearbyInjured = other;
            return;
        }
    }
}
