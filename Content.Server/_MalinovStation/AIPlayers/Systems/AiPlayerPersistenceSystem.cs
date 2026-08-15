using System.Linq;
using System.Threading.Tasks;
using Content.Server._MalinovStation.AIPlayers.Components;
using Content.Server.Database;
using Content.Shared._MalinovStation.AIPlayers;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Server._MalinovStation.AIPlayers.Systems;

/// <summary>
/// Optional cross-round persistence for an AI player's personality and most important memories (spec
/// section 26/Milestone 9), keyed by an admin-chosen <see cref="AIPlayerComponent.PersistentId"/> rather
/// than a player account. Fully opt-in on two axes: the <c>ai_players.persistence.enabled</c> CVar (default
/// off) and per-entity (an AI player spawned without a PersistentId never touches the database at all,
/// exactly like every milestone before this one). Relationships and Needs are deliberately NOT persisted -
/// see the class remarks below for why.
/// </summary>
/// <remarks>
/// Relationships aren't persisted because they're keyed by EntityUid, which isn't stable across rounds -
/// there's no meaningful "the same other character" to reattach a saved relationship to once the round
/// ends. Needs/Goals/Danger/Conversation state aren't persisted because they're intentionally transient:
/// a returning AI player should start each round rested and fed, not still exhausted from three rounds ago.
/// </remarks>
public sealed partial class AiPlayerPersistenceSystem : EntitySystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;

    private bool _enabled;
    private int _maxMemories;

    private readonly Dictionary<EntityUid, Task<AiPlayerPersistedData?>> _pendingLoads = new();
    private readonly List<EntityUid> _finishedLoadsBuffer = new();
    private readonly HashSet<string> _pendingSaves = new();

    /// <summary>Whether a load requested via <see cref="RequestLoad"/> for this entity hasn't resolved yet.</summary>
    public bool HasPendingLoad(EntityUid uid) => _pendingLoads.ContainsKey(uid);

    /// <summary>Whether a save for this PersistentId (triggered by the entity terminating) hasn't finished yet.</summary>
    public bool HasPendingSave(string persistentId) => _pendingSaves.Contains(persistentId);

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("aiplayers.persistence");

        SubscribeLocalEvent<AIPlayerComponent, EntityTerminatingEvent>(OnTerminating);

        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersPersistenceEnabled, v => _enabled = v, true);
        Subs.CVar(_cfg, MalinovAiPlayerCVars.AiPlayersPersistenceMaxMemories, v => _maxMemories = v, true);
    }

    /// <summary>
    /// Kicks off an async load for a just-spawned AI player. If persistence is disabled this is a no-op and
    /// the entity keeps whatever fresh, randomized state it was spawned with. Otherwise, once the (async) DB
    /// read completes, a saved record (if any) overwrites the entity's personality and restores its
    /// memories - see <see cref="Update"/>.
    /// </summary>
    public void RequestLoad(EntityUid uid, string persistentId)
    {
        if (!_enabled)
            return;

        _pendingLoads[uid] = _db.GetAiPlayerDataAsync(persistentId);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_pendingLoads.Count == 0)
            return;

        _finishedLoadsBuffer.Clear();
        foreach (var (uid, task) in _pendingLoads)
        {
            if (task.IsCompleted)
                _finishedLoadsBuffer.Add(uid);
        }

        foreach (var uid in _finishedLoadsBuffer)
        {
            var task = _pendingLoads[uid];
            _pendingLoads.Remove(uid);
            HandleLoadCompleted(uid, task);
        }
    }

    private void HandleLoadCompleted(EntityUid uid, Task<AiPlayerPersistedData?> task)
    {
        if (task.IsFaulted)
        {
            _sawmill.Error($"Failed to load persisted data for {ToPrettyString(uid)}: {task.Exception?.GetBaseException().Message}");
            return;
        }

        if (task.IsCanceled || Deleted(uid))
            return;

        // Safe: only reached once task.IsCompleted is true and IsFaulted/IsCanceled are excluded above, so
        // the task has already run to completion - this cannot block.
#pragma warning disable RA0004
        var data = task.Result;
#pragma warning restore RA0004

        if (data is null)
            return; // Nothing saved yet under this PersistentId - keep the freshly-randomized personality.

        ApplyPersistedData(uid, data);
    }

    private void ApplyPersistedData(EntityUid uid, AiPlayerPersistedData data)
    {
        if (TryComp<PersonalityComponent>(uid, out var personality))
        {
            personality.Sociability = data.Sociability;
            personality.Courage = data.Courage;
            personality.Curiosity = data.Curiosity;
            personality.Laziness = data.Laziness;
            personality.Greed = data.Greed;
            personality.Aggression = data.Aggression;
            personality.Loyalty = data.Loyalty;
            personality.RiskTolerance = data.RiskTolerance;
            personality.AuthorityRespect = data.AuthorityRespect;
            personality.Professionalism = data.Professionalism;
            personality.Empathy = data.Empathy;
            personality.Honesty = data.Honesty;
            personality.Impulsiveness = data.Impulsiveness;
        }

        if (TryComp<MemoryComponent>(uid, out var memory))
        {
            memory.Memories.Clear();
            foreach (var saved in data.Memories)
            {
                memory.Memories.Add(new AiMemory
                {
                    // The in-game clock has no meaning across rounds/restarts, so restored memories are
                    // anchored to "now" rather than their original (now-meaningless) timestamp.
                    Timestamp = _timing.CurTime,
                    Importance = saved.Importance,
                    EmotionalWeight = saved.EmotionalWeight,
                    Source = saved.Source,
                    Content = saved.Content,
                });
            }
        }

        _sawmill.Info($"[AI:{ToPrettyString(uid)}] Loaded persisted data for \"{data.PersistentId}\" ({data.Memories.Count} memories).");
    }

    private void OnTerminating(EntityUid uid, AIPlayerComponent component, ref EntityTerminatingEvent args)
    {
        if (!_enabled || component.PersistentId is not { } persistentId)
            return;

        if (!TryComp<PersonalityComponent>(uid, out var personality))
            return;

        var memories = TryComp<MemoryComponent>(uid, out var memoryComp)
            ? memoryComp.Memories
                .OrderByDescending(m => m.Importance)
                .Take(_maxMemories)
                .Select(m => new AiPlayerPersistedMemory(DateTime.UtcNow, m.Importance, m.EmotionalWeight, m.Source, m.Content))
                .ToList()
            : new List<AiPlayerPersistedMemory>();

        var data = new AiPlayerPersistedData(
            persistentId,
            personality.Sociability,
            personality.Courage,
            personality.Curiosity,
            personality.Laziness,
            personality.Greed,
            personality.Aggression,
            personality.Loyalty,
            personality.RiskTolerance,
            personality.AuthorityRespect,
            personality.Professionalism,
            personality.Empathy,
            personality.Honesty,
            personality.Impulsiveness,
            memories);

        // The entity is already being torn down, so there's no live state left to apply a result to -
        // fire-and-forget, but still logged so a broken save doesn't fail silently, and tracked in
        // _pendingSaves so tests (and any future "is my save done" tooling) can wait on it deterministically.
        _pendingSaves.Add(persistentId);
        SaveAsync(data);
    }

    private async void SaveAsync(AiPlayerPersistedData data)
    {
        try
        {
            await _db.SaveAiPlayerDataAsync(data);
            _sawmill.Info($"Saved persisted data for AI player \"{data.PersistentId}\" ({data.Memories.Count} memories).");
        }
        catch (Exception e)
        {
            _sawmill.Error($"Failed to save persisted AI player data for \"{data.PersistentId}\": {e}");
        }
        finally
        {
            _pendingSaves.Remove(data.PersistentId);
        }
    }
}
