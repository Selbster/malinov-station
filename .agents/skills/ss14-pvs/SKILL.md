---
name: ss14-pvs
description: PVS (Potentially Visible Sets) in Space Station 14 — chunk spatial partitioning, overrides (ForceSend / GlobalOverride / SessionOverride), per-tick budgets, LoD, leave mechanics, visibility masks, and ExpandPvsEvent. Use it to design always-visible entities, debug pop-in or desyncs, tune budgets, or reason about what a client can and cannot see.
---

# PVS — Potentially Visible Set

## What to read first

1. `references/fresh-pattern-catalog.md` — confirmed, verified API recipes.
2. `references/rejected-snippets.md` — legacy CVar names and wrong mental models to avoid.

## Source of truth

1. The fork codebase is the ground truth: `Robust.Server/GameStates/PvsSystem*.cs`, `Robust.Server/GameStates/PvsOverrideSystem.cs`, `Robust.Server/GameStates/PvsChunk.cs`, `Robust.Server/GameStates/PvsData.cs`.
2. CVar names and defaults drift between upstream syncs — re-verify against `Robust.Shared/CVars.cs` by name.
3. Anything not re-verified against current code is marked `[unverified]`.

## PVS mental model

PVS is a server-side system that decides which entities each client receives. The world is split into **8×8 unit chunks**, each attached to a root entity (a map or a grid). Every tick, for every client, PVS collects visible chunks, applies overrides and budgets, and composes a `GameState`.

```
SendGameStates(players)
├─ ProcessDisconnections()
├─ CacheSessionData(players)
├─ BeforeSerializeStates()      // ProcessQueuedAcks → GetVisibleChunks → ProcessVisibleChunks
├─ SerializeStates()            // per client:
│  ├─ UpdateSession()           // VisMask (union), viewers, sort chunks by distance
│  └─ GetEntityStates():
│     ├─ AddForcedEntities()    // ForceSend + viewers — ignores budget and masks
│     ├─ (apply real budget limits)
│     ├─ AddAllOverrides()      // raises ExpandPvsEvent, then global + session overrides
│     └─ AddPvsChunks()         // budget-respecting, LoD-limited
├─ SendStates()
├─ AfterSerializeStates()
└─ ProcessLeavePvs()
```

The single most important pattern: **an override is not free — `GlobalOverride`/`ForceSend` sets are re-traversed every tick**, so keep them small and shallow.

## Patterns

1. Use `AddSessionOverride` for per-player visibility (UI entities, personal markers); `GlobalOverride` only when every client must see it.
2. Reserve `ForceSend` for maps/grids and truly critical entities — it ignores budget and masks but **never sends children**.
3. Extend visibility dynamically with `ExpandPvsEvent` — it still respects budgets and masks.
4. Check visibility masks consistently: `(eyeMask & entityMask) == entityMask`; the client's effective mask is the **union** of all its viewers.
5. Expect re-entries to be cheap: a returning entity gets a delta state from its last acked tick, not a full state.
6. Tune the two burst budgets separately: `net.pvs_budget` (first-time spawns) vs `net.pvs_enter_budget` (mass re-entry after teleports/pops).
7. Clean up overrides when the entity is deleted — the engine auto-clears them (`OnDeleted`), so design systems to be re-added per round.

## Anti-patterns

1. Using stale CVar names (`net.maxupdaterange`, `net.pvs_entity_budget`, `net.pvs_entity_enter_budget`) — they do not exist; the current names are `net.pvs_range`, `net.pvs_budget`, `net.pvs_enter_budget`.
2. `ForceSend` for a container — children (contents) are not sent; use `GlobalOverride`/`SessionOverride` when the hierarchy must arrive whole.
3. Treating the 5 `LodCounts` as distance levels — at runtime only **two** tiers are used (whole chunk vs `LodCounts[0]`).
4. Adding entities via `ExpandPvsEvent` and forgetting the `VisMask` side effect — the event's mask overrides the session mask for **all** overrides that tick.
5. Mass `GlobalOverride` on deep hierarchies — `CacheGlobalOverrides` re-traverses parents and children every tick.
6. Ignoring `net.pvs_exit_budget` on the client — detach is budgeted; mass exits cause detach lag.
7. Relying on `net.pvs false` for "send everything every tick" — after the first full dump it switches to delta-only.

## Code examples

### 1) Always-visible entity for one client

```csharp
// Server system: keeps a UI/ghost marker in PVS for exactly one player.
_pvsOverrides.AddSessionOverride(uid, session);
// Verify: PvsOverrideSystem.AddSessionOverride(EntityUid, ICommonSession).
```

### 2) Dynamic extension on a viewer's entity

```csharp
[ByRefEvent]
private void OnExpand(Entity<TransformComponent> viewer, ref ExpandPvsEvent args)
{
    if (viewer.Owner != _player.LocalPlayer?.ControlledEntity)
        return;

    args.Entities ??= new List<EntityUid>();
    args.Entities.Add(_markerUid);

    // args.VisMask = 0x2; // CAUTION: this mask also applies to all of this
    // session's overrides this tick, not just _markerUid.
}
// Verify: PvsSystem.cs -> public struct ExpandPvsEvent(ICommonSession session, int mask).
```

### 3) Critical entity outside normal range

```csharp
// Ignores budget and visibility masks, but does NOT send children.
_pvsOverrides.AddForceSend(uid);
// Verify: PvsOverrideSystem.AddForceSend(EntityUid) and per-session overload.
```

## Dimension checklist

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating | N/A | PVS is server-side; no prediction flags involved |
| Server / Client / Shared split | ◑ | `SharedPvsOverrideSystem` is shared; `PvsSystem` is server-only; detach handling is client |
| Event and `UpdatesBefore` / `UpdatesAfter` ordering | ◑ | `ExpandPvsEvent` is raised inside `AddAllOverrides`, before chunk processing |
| Component lifecycle | ◑ | overrides auto-cleared on entity deletion; viewers tracked per session |
| Hot path and allocations | ◑ | per-tick override traversal, chunk rebuilds on parent changes, full-enumeration fallback on ack lag |
| PVS / network visibility of client-side logic | ☑ | this is the topic |

## LoD and view bounds

- Chunk `Contents` are pre-sorted: PvsPriority → anchored → direct children → grandchildren → all descendants. `LodCounts[0..4]` store cumulative counts.
- Runtime uses **two** tiers (`PvsSystem.ToSendSet.cs`): if the Chebyshev distance to the chunk centre `<= (viewSize + ChunkSize) / 2` send the whole `Contents`; otherwise send only `LodCounts[0]` (PvsPriority entities). `LodCounts[1..3]` are computed but not consumed.
- Caveat: re-anchoring an entity or toggling its `PvsPriority` flag does not repopulate the chunk until it is next dirtied.
- View bounds per viewer (`CalcViewBounds`): `worldPos + EyeComponent.Offset`, size `= max(net.pvs_range, net.pvs_priority_range) × PvsScale`, radius `= size / 2`. Chunks intersecting the square are collected and sorted by distance to the nearest viewer (near-first, so budget pressure cuts far entities first).
- `net.pvs_priority_range` is meant for `MetaDataFlags.PvsPriority` entities (e.g. lights, occluders) that are **directly parented to a grid or map**, to reduce pop-in.

## Budget mechanics

Per client per tick, in order: forced entities (no budget) → real limits → overrides (budget) → visible chunks (budget). If the budget is exhausted, the entity is simply not sent this tick and is retried next tick if still visible. An entity counts as "entering" when it was never seen (`EntityLastAcked == 0`), was not sent last tick (`LastSeen != CurTick - 1`), is not in the last acked state (`EntityLastAcked < FromTick`), or re-entered after leaving (`LastLeftView >= FromTick`).

## Leave mechanics

`ProcessLeavePvs` collects entities not sent this tick, records `LastLeftView`, and sends a **reliable** `MsgStateLeavePvs`. The client sets `MetaDataFlags.Detached`, moves the entity into null-space and keeps it in memory (capped by `net.pvs_exit_budget`). Re-entry clears the flag and resumes normal updates — the entity is not re-created.

## CVars and debugging

| CVar | Default | Flags | Description |
|---|---|---|---|
| `net.pvs` | true | ARCHIVE, REPLICATED, SERVER | master switch; when off, no culling |
| `net.pvs_range` | 25 | ARCHIVE, REPLICATED, SERVER | side of the view square (radius 12.5) |
| `net.pvs_priority_range` | 32.5 | ARCHIVE, REPLICATED, SERVER | extended range for PvsPriority entities |
| `net.pvs_budget` | 50 | ARCHIVE, REPLICATED, CLIENT | max never-seen (new) entities per state |
| `net.pvs_enter_budget` | 200 | ARCHIVE, REPLICATED, CLIENT | max re-entering entities per state |
| `net.pvs_exit_budget` | 75 | ARCHIVE, CLIENTONLY | detach entities processed per client tick |
| `net.pvs_async` | true | ARCHIVE, SERVERONLY | parallel PVS jobs |
| `net.pvs_compress_level` | 3 | ARCHIVE | ZSTD level for game states (network + replays) |

```
net.pvs false          // disable culling (first state is a full dump, then deltas)
net.pvs_range 50
net.pvs_budget 200
net.pvs_enter_budget 200
pvs_override_info <NetEntity>   // show override info for an entity
```

## Extension rule

1. Prefer `SessionOverride` / `GlobalOverride` / `ExpandPvsEvent` over `ForceSend` unless budget-and-mask immunity is genuinely required.
2. When adding an always-visible entity, record the trade-offs: child delivery, mask behavior, per-tick traversal cost.
3. Keep CVar references under their current names; re-verify against `Robust.Shared/CVars.cs`.

## Related skills

`ss14-prediction` (client-side consequences of PVS detach), `ss14-netcode` (game-state transport), `ss14-ui-bui` (why BUI entities need overrides), `ss14-ecs-entities` (NetEntity vs EntityUid).

Verified against code state: 2026-08-02.
