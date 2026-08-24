# Fresh Pattern Catalog (PVS)

Verified against the fork's current code on 2026-08-02. Re-check symbols before copying.

## Override API

`Robust.Server/GameStates/PvsOverrideSystem.cs` (extends `SharedPvsOverrideSystem`):

- `AddGlobalOverride(EntityUid)` / `RemoveGlobalOverride(EntityUid)` — everyone, respects budget and masks, sends entity + parents + children.
- `AddSessionOverride(EntityUid, ICommonSession)` / `RemoveSessionOverride(EntityUid, ICommonSession)` — one client, same semantics as global.
- `AddSessionOverrides(EntityUid, Filter)` — applies a session override to every client in the filter.
- `AddForceSend(EntityUid)` / `RemoveForceSend(EntityUid)` — everyone, **ignores budget and masks**, sends parents but NOT children.
- `AddForceSend(EntityUid, ICommonSession)` / `RemoveForceSend(EntityUid, ICommonSession)` — per-session variant.

Automatic ForceSend: maps (`OnMapCreated`), grids (`OnGridCreated`), and the session's viewers (attached entity first, then `ViewSubscriptions`).

Console: `pvs_override_info <NetEntity>` prints override info for an entity.

| Need | Choose | Children? | Budget? | Masks? |
|---|---|---|---|---|
| one client always sees it | `AddSessionOverride` | yes | yes | yes |
| every client always sees it | `AddGlobalOverride` | yes | yes | yes |
| truly critical, no budget/mask limits | `AddForceSend` | **no** | no | no |
| dynamic per-tick addition | `ExpandPvsEvent` | both fields | yes | yes |

## ExpandPvsEvent

`Robust.Server/GameStates/PvsSystem.cs`:

```csharp
[ByRefEvent]
public struct ExpandPvsEvent(ICommonSession session, int mask)
{
    public readonly ICommonSession Session;
    public List<EntityUid>? Entities;          // no children
    public List<EntityUid>? RecursiveEntities; // adds all children too
    public int VisMask;                        // defaults to the session mask
}
```

- Raised inside `AddAllOverrides` on the player's `AttachedEntity` (or globally if none).
- Both lists still respect visibility masks and the PVS budget.
- **Side effect:** if you set `VisMask`, it becomes the mask used for that client's *global and session overrides too* — not just the entities you add.

## Chunk internals

- `PvsSystem.ChunkSize = 8`; chunk location = `PvsChunkLocation(EntityUid Uid, Vector2i Indices)`, indices = `(localPosition / 8).Floored()`.
- `PvsChunk.Contents` order: PvsPriority entities → anchored → low-priority direct children → grandchildren (one level) → all remaining descendants. `LodCounts[0..4]` = cumulative counts.
- Runtime tiering (`PvsSystem.ToSendSet.cs`): `distance <= (_viewSize + ChunkSize) / 2 ? Contents.Count : LodCounts[0]` (Chebyshev distance to chunk centre).
- A chunk is dirtied when an entity is added/removed, moves between chunks, or changes parent. Dirty chunks are rebuilt in `ProcessVisibleChunks` (`PopulateContents`).

## Visibility masks

- Entity side: `MetaDataComponent.VisibilityMask` (default `1`).
- Viewer side: `EyeComponent.VisibilityMask` (default `1`); session mask is the **union** over all viewers (`session.VisMask |= viewer.VisibilityMask`).
- Send check: `(sessionMask & meta.VisMask) == meta.VisMask`.
- `EyeComponent.PvsScale` scales the view bounds (`CalcViewBounds`).

## Budgets and "entering"

- Order: `AddForcedEntities` → set limits (`net.pvs_budget`, `net.pvs_enter_budget`) → `AddAllOverrides` → `AddPvsChunks`.
- Entity is "entering" when `EntityLastAcked == 0`, `LastSeen != CurTick - 1`, `EntityLastAcked < FromTick`, or `LastLeftView >= FromTick`.
- Exhausted budget → entity skipped this tick, retried while still visible.
- Re-entry sends a delta from `EntityLastAcked` (`GetEntityState(..., fromTick: EntityLastAcked)`).

## PvsData / PvsSession fields

- `PvsData`: `LastSeen`, `LastLeftView`, `EntityLastAcked`.
- `PvsSession`: `VisMask`, `Viewers`, `Chunks`, `ToSend`, `States`, `Budget {NewLimit, EnterLimit, NewCount, EnterCount, DirtyCount}`, `FromTick`, `LastReceivedAck`, `RequestedFull`, `PreviouslySent` (bounded by `DirtyBufferSize`).

## Leave / detach lifecycle

- Server: `ProcessLeavePvs` → `MsgStateLeavePvs` (reliable) → `PvsData.LastLeftView = tick`.
- Client: sets `MetaDataFlags.Detached`, moves the entity to null-space, keeps it in memory; processing is capped by `net.pvs_exit_budget`. Re-entry clears the flag and resumes deltas.

## CVars (current names)

| CVar | Default | Flags |
|---|---|---|
| `net.pvs` | true | ARCHIVE, REPLICATED, SERVER |
| `net.pvs_range` | 25 | ARCHIVE, REPLICATED, SERVER |
| `net.pvs_priority_range` | 32.5 | ARCHIVE, REPLICATED, SERVER |
| `net.pvs_budget` | 50 | ARCHIVE, REPLICATED, CLIENT |
| `net.pvs_enter_budget` | 200 | ARCHIVE, REPLICATED, CLIENT |
| `net.pvs_exit_budget` | 75 | ARCHIVE, CLIENTONLY |
| `net.pvs_async` | true | ARCHIVE, SERVERONLY |
| `net.pvs_compress_level` | 3 | ARCHIVE |
| `net.pvs_entity_growth` / `net.pvs_entity_initial` / `net.pvs_entity_max` | 1<<16 / 1<<16 / 1<<24 | ARCHIVE, SERVERONLY (internal storage) |
