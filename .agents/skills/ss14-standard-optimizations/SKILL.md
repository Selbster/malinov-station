---
name: ss14-standard-optimizations
description: Practical skill in standard optimizations of Space Station 14: caching, reduction of allocations, abandonment of LINQ in hot-path, ActiveComponent approach, EntityQuery, order of components in EntityQueryEnumerator, ByRef events, DirtyField, staggered update timers, early exits and cleaning of unnecessary components. Use it when designing, reviewing and optimizing ECS code in server/shared/client.
---

# SS14 Standard Optimizations

Goal: pick the correct optimization for ECS code fast — measurable wins only, no readability damage, no outdated techniques :)
On any docs-vs-code conflict the current fork code wins; reconciliation rules live in `references/docs-reconciliation.md`.

## Mental model

Hot path = `Update`, frequent event handlers, visual overlays, mass checks.
Optimize only a measured symptom (frame time, GC spikes, network delta size) and pick the cheapest technique first:
narrow the iteration set (ActiveComponent marker, rare-first query order) → cut repeated work (precomputed invariants, incremental aggregates, early exits, staggered timers) → cut allocations (buffer reuse, no LINQ) → shrink network deltas (`DirtyField`).

## When to use / not use

Use when: optimizing the hot path above; a review shows `TryComp/HasComp` spam, LINQ chains or inactive-entity sweeps; hunting lags/GC spikes.
Do not use when: the code is rarely executed; complexity grows faster than benefit; there is no measurable symptom.

## Reading order of resources

1. `references/optimization-patterns.md` — signal → decision → risk router for reviews and refactors.
2. `references/annotated-code-examples.md` — live examples from this fork, each with a `// Verify:` recipe.
3. `references/docs-reconciliation.md` — how to resolve docs/code conflicts and judge freshness.

## Workflow

1. Find the hot path and record the symptom. 2. Pick the minimum technique from the cards. 3. Compare before/after: iterations, allocations, network deltas, behavior. 4. Explain in PR "what got faster" and "why it is safe".

## Optimization cards

Each card: pattern (with the failure mode it prevents) → anti-pattern → limits. Full annotated snippets with verify recipes live in `references/annotated-code-examples.md`.

### 1) Cache invariants and aggregates before the hot loop ⏱️

**Pattern**
1. Precompute expensive invariants before nested loops (shapes, angle sets, fast-path flags) — prevents O(n·m) recomputation inside the innermost loop.
2. Maintain aggregates incrementally (`TargetCount`, `BurstShotsCount`) instead of rescanning collections per tick — prevents full-collection walks for one number.
3. Cache frequent component checks as `EntityQuery<T>` fields — prevents dictionary lookups on every call.

**Anti-pattern**
1. Expensive computation inside the inner loop.
2. Recounting "how many are done" by walking collections each tick.

**Limits**: biggest effect at 2+ nested loop levels; for rare code prefer simplicity over micro-optimization.

### 2) Cut allocations ♻️

**Pattern**
1. Create working collections once as system/UI fields and `Clear()` them per use — prevents gen0 garbage storms and GC-spike frame freezes.
2. Pass existing lists/spans into methods instead of rebuilding collections per call.
3. Rent temporary hot-path buffers from `ArrayPool<T>` with a symmetric `Return` in `finally`.
4. Never share one scratch buffer across reentrant calls: if `RemComp` or an event can synchronously re-enter this system mid-iteration, use a local list or defer work — prevents silent buffer corruption.

**Anti-pattern**
1. `new List<T>()` per frame/call.
2. Copying large collections for a single pass.
3. Pool rent without guaranteed return (leaks; dirty data when `Clear` is skipped).

```csharp
private ValueList<EntityUid> _toRemove = new(); // One field, reused every tick.

public override void Update(float frameTime)
{
    var query = AllEntityQuery<ColorFlashEffectComponent>();
    _toRemove.Clear(); // Reuse without allocation; safe because RemComp happens after the sweep.

    while (query.MoveNext(out var uid, out _))
    {
        if (_animation.HasRunningAnimation(uid, AnimationKey))
            continue;

        _toRemove.Add(uid);
    }

    foreach (var ent in _toRemove)
        RemComp<ColorFlashEffectComponent>(ent);
}
// Verify: ColorFlashEffectSystem.cs — grep RemComp<ColorFlashEffectComponent>
```

**Limits**: clear at the start of every pass — including paths that bail out early — so a half-filled buffer never leaks into the next run; keep rent/return strictly symmetric.

### 3) No LINQ in the hot path 🚫

**Pattern**
1. Rewrite hot spots as `for/foreach` with explicit early exits — prevents per-call delegate/enumerator/result-list allocations that cause jitter.
2. Keep LINQ for rare/offline code where compactness matters more.

**Anti-pattern**
1. `Where/Select/Any/Count` inside `Update` and frequent handlers.
2. LINQ chains for simple filters inside loops.

**Limits**: do not turn cold code into micro-optimized noise; readability wins off the hot path.

### 4) Component + ActiveComponent pair 🔋

**Pattern**
1. The base component holds configuration; a separate `Active...Component` marks "currently active" so `Update` sweeps only active entities — prevents iterating huge sets of inactive ones.
2. Add/remove the marker exactly at state transitions so queries stay consistent.

**Anti-pattern**
1. One sweep over all entities with `if (!IsActive) continue`.
2. An activity flag buried in the base component without a queryable marker.

**Limits**: needs transition discipline; logic must remain consistent through shutdown/removal.

```csharp
// Verify: TriggerSystem.Timer.cs — grep RemComp<ActiveTimerTriggerComponent>
```

### 5) `EntityQuery<T>` for repeated TryComp/HasComp/Resolve 🔎

**Pattern**
1. Cache `EntityQuery<T>` in `Initialize()` and use its typed methods — prevents repeated general-purpose lookups during mass checks.

**Anti-pattern**
1. Uncached checks repeated back-to-back in high-frequency code.

**Limits**: gain shows only under frequency; isolated calls are fine uncached.

### 6) Order components in `EntityQueryEnumerator` 📉

**Pattern**
1. Rarest component first, then rarer-to-massive — prevents building intersections over the larger set; the engine itself notes trait1 should be the smaller set of components.

**Anti-pattern**
1. Massive component first ("as it came to mind").

```csharp
// Active* is far rarer than the base timer component:
var query = EntityQueryEnumerator<ActiveTimerTriggerComponent, TimerTriggerComponent>();
// Verify: TriggerSystem.Timer.cs — grep EntityQueryEnumerator<ActiveTimerTriggerComponent
```

**Limits**: estimate real cardinality in your subsystem; recheck after gameplay data/prototype changes.

### 7) `[ByRefEvent] record struct` events ⚡

**Pattern**
1. High-frequency local events: `[ByRefEvent] public record struct ...` raised via `RaiseLocalEvent(uid, ref ev)` — prevents per-event heap allocations and struct copies.

**Anti-pattern**
1. Reference-type event classes for frequent gameplay-loop events.
2. Dropping `ref` on raise/handler of a by-ref event.

**Limits**: negligible gain for rare events; keep handler signatures uniform. Mutations through `ref` roll back with prediction states on the client — gate accordingly.

### 8) `DirtyField` over `Dirty` for large networked components 📡

**Pattern**
1. With `[AutoGenerateComponentState(fieldDeltas: true)]`, dirty only the actually changed field(s) — prevents resending full state for one-field updates.
2. Layer split: on the server `DirtyField` decides which deltas go out to clients. In shared/client systems that tick predicted delta components, also dirty the changed field locally so client rollback can reroll it (per-entity timers like `NextUpdate` are the classic case).
3. Prefer overloads that bind uid and component together: the `(Entity<T>, comp, name)` form or the two-arg `Entity<T?>` form via `ent.AsNullable()`. `nameof` field names are checked at compile time by the `[ValidateMember]` analyzer; the runtime only logs an error if a name still misses the lookup.

**Anti-pattern**
1. `Dirty(uid, comp)` for every small change of a large component.
2. A bare string instead of `nameof(...)` — silently bypasses the analyzer.
3. Mutating an `[AutoNetworkedField]` without a dirty call afterwards — server and client drift apart until a full-state resync.

```csharp
component.NextUpdate += component.UpdateCooldown;
DirtyField(uid, component, nameof(ProximityDetectorComponent.NextUpdate));
// Verify: EntityManager.ComponentDeltas.cs + EntitySystem.Proxy.cs — grep DirtyField
```

**Limits**: if almost the whole state changes at once, plain `Dirty` is acceptable.

### 9) Cheap filters first, early exits 🚪

**Pattern**
1. Order checks cheap→expensive with early `return/continue` — prevents paying expensive work for entities that fail trivial guards.

**Anti-pattern**
1. Expensive computation before simple guards.
2. Deep nesting instead of flat filter chains.

**Limits**: keep filters predictable and readable.

### 10) Remove finished-state components 🧹

**Pattern**
1. Remove temporary/active markers immediately after their state completes — prevents dead entities from entering future queries and inflating networked state.

**Anti-pattern**
1. Keeping temporary components around "just in case".

**Limits**: removal must not break expected subscriptions or visual transitions.

```csharp
// Verify: trigger/timed systems — grep RemComp<ActiveTimerTriggerComponent>
```

### 11) Stagger per-entity work behind `NextUpdate` timers ⏱️

**Pattern**
1. Gate expensive per-entity logic behind a `NextUpdate` timestamp stored on the component and advance it by the entity's own interval (`NextUpdate += interval`) — prevents the whole population from doing its expensive work on the same tick.
2. Combine with card 8: when the timer advances, dirty only the timestamp field.
3. Entities created at different moments tick out of phase naturally; add randomized offsets on init only if mass spawns still align.

**Anti-pattern**
1. Every entity runs its full expensive body every tick because "it is simpler".
2. Resetting from absolute time (`NextUpdate = CurTime + interval`) in steady-state ticking — reserve resets for init/pause transitions, advance otherwise.

**Limits**: only for work whose result may lag by up to one interval; ordering-sensitive or combat-critical checks stay immediate.

```csharp
var query = EntityQueryEnumerator<ThirstComponent>();
while (query.MoveNext(out var uid, out var thirst))
{
    if (_timing.CurTime < thirst.NextUpdateTime)
        continue;

    thirst.NextUpdateTime += thirst.UpdateRate; // Advance, don't reset: cadence stays stable under lag.
    // ...throttled expensive body...
}
// Verify: ThirstSystem.cs — grep NextUpdateTime +=
```

## Checklist before PR

1. Changes are tied to a measured hot path; no blind micro-optimization.
2. No LINQ in hot loops without written rationale.
3. Query order checked: rare → massive.
4. Large networked components use `DirtyField` with `nameof`; shared/client ticks of predicted delta components re-dirty changed fields locally.
5. Scratch buffers cleared before reuse and safe against reentry; pool rents returned.
6. Temporary components removed after their state completes.
7. Text/comments carry no hard-coded repo paths; snippets carry verify recipes.

## SS14 dimensions

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating (`InPrediction` / `IsPredictionEnabled`) | ☑ | Scratch buffers clear at Update start each call; incremental counters live on components so rollback restores them; `DirtyField`: server drives outgoing deltas, predicted shared/client ticks re-dirty locally (card 8) |
| Server / Client / Shared split + `[NetworkedComponent]` | N/A | Techniques are layer-agnostic; apply in whichever layer owns the hot path |
| Event and `UpdatesBefore`/`UpdatesAfter` ordering | N/A | Orthogonal to these techniques |
| Component lifecycle (Add/Remove/Initialize/Shutdown) | ☑ | Active-marker add/remove discipline (card 4); cleanup on completion (card 10) |
| Hot path and allocations (`EntityQuery`, by-ref events, `DirtyField`) | ☑ | Core topic: cards 1–3, 5–11 |
| PVS / network visibility of client-side logic | ☑ | Smaller deltas shrink per-client state sends (card 8); client overlay buffers reuse fields (card 2) |

## Rule for extending this skill

1. Add only architecture-wide optimizations that repeat across several subsystems.
2. Narrow topics (prediction-specific, atmos-specific, …) go to specialized skills.
3. Every new card ships pattern + anti-pattern + limits + example with a verify recipe.
4. After edits update the marker below, re-run the self-check and sync the bridge.

Verified against code state: 2026-08-22
