---
name: ss14-transform-system-api
description: A complete reference for the SharedTransformSystem API in Space Station 14: method families, overload selection, obsolete warnings and practical patterns. Use when you need to select the correct TransformSystem method, avoid coordinate space errors, or batch transform operations.
---

# TransformSystem: API

This skill is the public API directory for `SharedTransformSystem`.
For the general architecture and operation scheme, first read `SS14 Transform System Core`.

## Mental model

Two questions to pick the right method:
1. **What coordinate space** is the input in? (`EntityCoordinates` = relative to parent, `MapCoordinates` = world-in-map)
2. **Read or write?**

Read: `GetWorldPosition` / `GetMapCoordinates` / `GetMoverCoordinates`
Write: `SetLocal*` / `SetWorld*` / `SetMapCoordinates` / `SetCoordinates`

Prefer uid-overloads. Resolve `TransformComponent` once per entity in hot loops.

## SS14 dimensions

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating | ☐ N/A | Transform methods are shared; prediction gating is handled at call-site. When subscribing to `MoveEvent`, account for prediction re-fires. |
| Server / Client split | ☑ | Server: authoritative anchoring, reparenting, teleport. Client: interpolation (`ActivateLerp`), matrix rendering, mover coordinates for UI. |
| Event ordering | ☑ | `MoveEvent` [ByRefEvent] fires after position update, before grid traversal. `EntParentChangedMessage` [ByRefEvent] fires on parent change. `OnGlobalMoveEvent` fires after directed events. |
| Hot path / allocations | ☑ | Prefer uid-overloads; resolve `TransformComponent` once per entity in loops. Use `EntityQuery<TransformComponent>` for batch traversal. |
| Component lifecycle | ☐ N/A | `TransformStartupEvent` is engine-internal (0 content subscribers). Content code uses `MapInitEvent` / `ComponentStartup`. |
| PVS / network visibility | ☐ N/A | Transform data feeds PVS but methods do not perform visibility filtering. |

## 1) Lifecycle and events

- `TransformStartupEvent` — directed event when TransformComponent first initializes. **Engine-internal only** (0 content subscribers; used by PVS, GridTraversal). Content init: use `MapInitEvent` / `ComponentStartup`.
- `MoveEvent` — **[ByRefEvent]** directed at the moving entity. Fields: `OldPosition`, `NewPosition` (both `EntityCoordinates`, NOT world positions), `OldRotation`, `NewRotation`. Computed: `ParentChanged`. Subscribe with `ref MoveEvent ev`.
- `OnGlobalMoveEvent` — **[Discouraged]** global broadcast. Engine docs say "which you probably shouldn't" — use only when you truly need every move event across all entities.
- `ActivateLerp` — virtual hook for client interpolation. Content code should not override.

## 2) Coordinate transformations and distances

- `IsValid(EntityCoordinates coordinates)` — check validity.
- `WithEntityId(EntityCoordinates coordinates, EntityUid entity)` — translate into another entity's space.
- `ToMapCoordinates(EntityCoordinates coordinates, bool logError = true)` — EntityCoords → MapCoords.
  `ToMapCoordinates(NetCoordinates coordinates)` — from network coordinates.
- `ToWorldPosition(EntityCoordinates coordinates, bool logError = true)` — to world Vector2.
  `ToWorldPosition(NetCoordinates coordinates)`.
- `ToCoordinates(Entity<TransformComponent?> entity, MapCoordinates coordinates)` — MapCoords → EntityCoords anchored to entity.
  `ToCoordinates(MapCoordinates coordinates)` — unanchored variant.
- `GetGrid(EntityCoordinates coordinates)` / `GetGrid(Entity<TransformComponent?> entity)` — resolve grid uid.
- `GetMapId(EntityCoordinates coordinates)` / `GetMapId(Entity<TransformComponent?> entity)` — resolve map id.
- `GetMap(EntityCoordinates coordinates)` / `GetMap(Entity<TransformComponent?> entity)` — resolve map entity uid.
- `InRange(EntityCoordinates coordA, EntityCoordinates coordB, float range)` — cross-space distance check.
  `InRange(Entity<TransformComponent?> entA, Entity<TransformComponent?> entB, float range)`.

## 3) Mover and tile helper API

- `GetMoverCoordinates(EntityUid uid)` — effective world-space position accounting for container nesting.
  Overloads: uid / uid+xform / EntityCoordinates+xformQuery / EntityCoordinates.
- `GetMoverCoordinateRotation(EntityUid uid, TransformComponent xform)` — mover coords + world rotation.
- `GetGridOrMapTilePosition(EntityUid uid, TransformComponent? xform = null)` — tile position on grid or map.
- `GetGridTilePositionOrDefault(Entity<TransformComponent?> entity, MapGridComponent? grid = null)` — tile position, or `Vector2i.Zero`.
- `TryGetGridTilePosition(Entity<TransformComponent?> entity, out Vector2i indices, MapGridComponent? grid = null)` — safe `Try` variant.

## 4) Hierarchy, parent, anchoring

- `AnchorEntity(Entity<T> entity, Entity<MapGridComponent>? grid = null)` — preferred.
  Convenience: `AnchorEntity(EntityUid uid)` or `(EntityUid uid, TransformComponent xform)`.
  Obsolete: `(EntityUid, TransformComponent, EntityUid, MapGridComponent, Vector2i)` and `(EntityUid, TransformComponent, MapGridComponent)`.
- `Unanchor(EntityUid uid)` or `Unanchor(EntityUid uid, TransformComponent xform, bool setPhysics = true)`.
- `ContainsEntity(EntityUid parent, Entity<TransformComponent?> child)` — check nesting in transform tree.
- `IsParentOf(TransformComponent parent, EntityUid child)` — quick parent check.
- `SetGridId` — low-level, no `[Obsolete]` attribute but bypasses traversal invariants. Prefer `AttachToGridOrMap` or `SetCoordinates` for content code.
- `ReparentChildren(EntityUid oldUid, EntityUid uid)` — bulk reparent.
- `GetParent(EntityUid uid)` / `GetParentUid(EntityUid uid)`.
- `SetParent(EntityUid uid, EntityUid parent)` or `SetParent(EntityUid uid, TransformComponent xform, EntityUid parent, TransformComponent? parentXform = null)`.

## 5) Local mutations — preferred overloads

- `SetLocalPosition(EntityUid uid, Vector2 value, TransformComponent? xform = null)`
- `SetLocalPositionNoLerp(EntityUid uid, Vector2 value, TransformComponent? xform = null)`
- `SetLocalRotation(EntityUid uid, Angle value, TransformComponent? xform = null)`
- `SetLocalRotationNoLerp(EntityUid uid, Angle value, TransformComponent? xform = null)`
- `SetLocalPositionRotation(EntityUid uid, Vector2 pos, Angle rot, TransformComponent? xform = null)` — combined pos+rot.
- `SetCoordinates(EntityUid uid, EntityCoordinates value)` — simple.
  `SetCoordinates(EntityUid uid, TransformComponent xform, EntityCoordinates value, Angle? rotation = null, bool unanchor = true, ...)` — full; use named parameter `rotation:` explicitly.

All overloads with `TransformComponent` as **first** parameter are `[Obsolete]`. Never use in new code.

## Preferred overload cheat-sheet

| Task | Preferred | Convenience | Obsolete (avoid) |
|------|-----------|-------------|------------------|
| Set local pos | `SetLocalPosition(EntityUid, Vector2, TransformComponent?)` | — | `(TransformComponent, Vector2)` |
| Set local pos (no lerp) | `SetLocalPositionNoLerp(EntityUid, Vector2, TransformComponent?)` | — | `(TransformComponent, Vector2)` |
| Set local rot | `SetLocalRotation(EntityUid, Angle, TransformComponent?)` | — | `(TransformComponent, Angle)` |
| Set local pos+rot | `SetLocalPositionRotation(EntityUid, Vector2, Angle, TransformComponent?)` | — | `(TransformComponent, Vector2, Angle)` |
| Set world pos | `SetWorldPosition(Entity<T>, Vector2)` | `(EntityUid, Vector2)` | `(TransformComponent, Vector2)` |
| Set world rot | `SetWorldRotation(EntityUid, Angle)` or `(TransformComponent, Angle)` | — | — |
| Set world pos+rot | `SetWorldPositionRotation(EntityUid, Vector2, Angle, TransformComponent?)` | — | — |
| Set map coords | `SetMapCoordinates(EntityUid, MapCoords)` or `(Entity<T>, MapCoords)` | — | — |
| Anchor | `AnchorEntity(Entity<T>, Entity<MapGridComponent>?)` | `(EntityUid)` or `(EntityUid, xform)` | 5-param and 3-param `(MapGridComponent)` variants |
| Unanchor | `Unanchor(EntityUid)` or `(EntityUid, xform, bool)` | — | — |
| Detach | `DetachEntity(EntityUid, TransformComponent?)` or `(Entity<T?>)` | — | `DetachParentToNull` |
| Drop | `DropNextTo(Entity<T?>, Entity<T?>)` | — | — (no uid overload) |

## 6) World position, rotation, map coordinates

**Getters** (all have uid / component / uid+query / component+query overloads):
- `GetWorldPosition`, `GetWorldRotation`, `GetWorldMatrix`, `GetWorldPositionRotation`.
- `GetMapCoordinates(EntityUid entity, TransformComponent? xform = null)`.

**Setters — preferred:**
- `SetWorldPosition(Entity<TransformComponent> entity, Vector2 worldPos)` — preferred.
  Convenience: `SetWorldPosition(EntityUid uid, Vector2 worldPos)`.
- `SetWorldRotation(EntityUid uid, Angle angle)` or `(TransformComponent, Angle)`.
- `SetWorldPositionRotation(EntityUid uid, Vector2 worldPos, Angle worldRot, TransformComponent? component = null)`.
- `SetMapCoordinates(EntityUid entity, MapCoordinates coordinates)` or `(Entity<TransformComponent>, MapCoordinates)`.
- `SetWorldRotationNoLerp(Entity<T?> entity, Angle angle)`.

**Obsolete setter:** `SetWorldPosition(TransformComponent, Vector2)` — use Entity<T> variant.

**Relative:**
- `GetRelativePositionRotation(TransformComponent component, EntityUid relative)`.
- `GetRelativePosition(TransformComponent component, EntityUid relative)`.
  Obsolete: overloads with extra `EntityQuery<TransformComponent>` parameter.

## 7) Batch matrix bundle API

All have uid / component / uid+query / component+query overloads:
- `GetInvWorldMatrix`, `GetWorldPositionRotationMatrix`, `GetWorldPositionRotationInvMatrix`, `GetWorldPositionRotationMatrixWithInv`.

Use when you need several derivatives at once (pos+rot+matrix) without repeated hierarchy passes.

**Note:** System methods produce flat translate+rotate matrices. For overlay/UI this is correct. For nested-grid accumulated transforms, consult the engine source.

## 8) Attach/Detach and "placement nearby"

- `AttachToGridOrMap(EntityUid uid, TransformComponent? xform = null)` — normalize parent to actual grid or map.
- `TryGetMapOrGridCoordinates(EntityUid uid, out EntityCoordinates? coordinates, TransformComponent? xform = null)` — safe grid/map coordinates.
- `DetachEntity(EntityUid uid, TransformComponent? xform = null)` or `DetachEntity(Entity<T?> ent)`.
  Obsolete: `DetachParentToNull`.
- `DropNextTo(Entity<T?> entity, Entity<T?> target)` — drop respecting containers, otherwise nearby in world.
- `PlaceNextTo(Entity<T?> entity, Entity<T?> target)` — place with same parent or target container.
- `SwapPositions(Entity<T?> entity1, Entity<T?> entity2)` — swap with parent-loop protection.

## 9) Legacy and restrictions

- Overloads marked `obsolete` are left for compatibility: in new code, prefer uid/entity options.
- `SetGridId` and `ActivateLerp` are low-level APIs.
- Direct legacy property-setters on `TransformComponent` (`LocalPosition`, `Coordinates`, `Anchored`) should not be used for new content code.

## Patterns

- In hot loops, resolve `TransformComponent` once and pass it to overloads.
- To change position and angle at the same time, use `SetLocalPositionRotation`.
- For rendering and spatial-culling, use bundle matrix methods.
- After teleport or cross-grid/cross-map moves, call `AttachToGridOrMap` to normalize parent. Intra-grid local moves and container transfers do NOT need it.
- For drop/spawn nearby, use `DropNextTo` instead of manual `SetParent` + `SetCoordinates`.

## Anti-patterns

❌ Ignore `Try`-result:
```csharp
// BAD: crash on invalid state
var tile = _transform.TryGetGridTilePosition(uid, out var indices);
```
✅ Always check:
```csharp
if (!_transform.TryGetGridTilePosition(uid, out var tile))
    return;
```

❌ Mix coordinates from different parent trees:
```csharp
// BAD: EntityCoordinates.Position from different grids are incomparable
var dist = entityA.Coordinates.Position - entityB.Coordinates.Position;
```
✅ Convert first:
```csharp
var posA = _transform.GetMapCoordinates(entityA);
var posB = _transform.GetMapCoordinates(entityB);
var dist = Vector2.Distance(posA.Position, posB.Position);
```

❌ Use `SetGridId` as normal movement API — bypasses traversal invariants.
✅ Use `AttachToGridOrMap` or `SetCoordinates` which handle grid rebinding.

❌ Manually `SetParent + SetCoordinates` for drops — misses containers.
✅ Use `DropNextTo` / `PlaceNextTo`.

## Known issues

- `SwapPositions` has documented client misprediction (`SwapLocationOnTriggerSystem.cs` comment: "SwapPositions mispredicts at the moment"). Only 1 of 5 callers applies `_net.IsServer` guard. Test swap on server+connected client.
- `GetMoverCoordinates(EntityCoordinates, EntityQuery)` is a plumbing overload that delegates to `GetMoverCoordinates(EntityCoordinates)` — the `xformQuery` parameter is ignored.

## Code examples

### Example 1: teleport with grid/map normalization
// Verify: SharedMagicSystem.cs — grep SetCoordinates + AttachToGridOrMap

```csharp
_transform.AttachToGridOrMap(entity, transform);

if (_map.TryFindGridAt(mapId, position, out var gridUid, out _))
{
    var gridPos = Vector2.Transform(position, _transform.GetInvWorldMatrix(gridUid));
    _transform.SetCoordinates(entity, transform, new EntityCoordinates(gridUid, gridPos));
}
else
{
    _transform.SetWorldPosition((entity, transform), position);
    _transform.SetParent(entity, transform, mapEntity);
}
```

### Example 2: spawn fallback via DropNextTo
// Verify: SharedEntityStorageSystem.cs — grep DropNextTo

```csharp
var uid = Spawn(protoName, overrides, doMapInit);
var xform = Transform(uid);

_xforms.DropNextTo((uid, xform), (target, targetXform));
```

### Example 3: tile-safe check via TryGetGridTilePosition
// Verify: MagnetPickupSystem.cs — grep TryGetGridTilePosition

```csharp
if (args.Grid is {} grid
    && _transform.TryGetGridTilePosition(uid, out var tile)
    && _atmosphere.IsTileAirBlockedCached(grid, tile))
{
    return;
}
```

### Example 4: GetGridTilePositionOrDefault for atmospheric calculations
// Verify: SharedAtmosphereSystem.cs — grep GetGridTilePositionOrDefault

```csharp
var indices = _transform.GetGridTilePositionOrDefault((uid, transform));
var tileMix = _atmosphere.GetTileMixture(transform.GridUid, null, indices, true);
```

### Example 5: changing space via WithEntityId
// Verify: SharedTransformSystem.Coordinates.cs — grep WithEntityId

```csharp
var localPos = _transform.WithEntityId(coords, gridUid).Position;
var snappedGrid = new EntityCoordinates(gridUid, snappedLocalPos);
var backToOriginal = _transform.WithEntityId(snappedGrid, coords.EntityId);
```

### Example 6: exchange entity positions
// Verify: SwapTeleporterSystem.cs — grep SwapPositions

```csharp
if (!_transform.SwapPositions(first, second))
    return;
```

## Mini-checklist

- [ ] Input coordinates: `EntityCoordinates` or `MapCoordinates`?
- [ ] Using preferred uid-overload (not obsolete component-first)?
- [ ] After teleport: called `AttachToGridOrMap`?
- [ ] Subscribing to `MoveEvent`? It's `[ByRefEvent]` — use `ref MoveEvent ev`
- [ ] In hot loop? Resolved `TransformComponent` once, passed to overload?
- [ ] Need combined pos+rot? Use `SetLocalPositionRotation` or `SetWorldPositionRotation`
- [ ] Drop/place/swap? Use `DropNextTo`/`PlaceNextTo`/`SwapPositions` — they accept `Entity<T?>`, NOT raw `EntityUid`
- [ ] Comparing positions across different parent trees? Convert via `ToMapCoordinates` first

## Source of truth

API signatures verified against `SharedTransformSystem.Component.cs`, `SharedTransformSystem.Coordinates.cs`, and `SharedTransformSystem.cs` in the RobustToolbox submodule. When in doubt, grep the actual method in the fork's RobustToolbox.
Verified against fork HEAD: 2026-08-19.

## Extension rule

When adding new transform-related methods to this skill:
1. Verify the signature exists in the fork's current RobustToolbox HEAD.
2. Mark obsolete methods with `(obsolete)`.
3. Show only the preferred overload + one line about alternatives.
4. Sync the bridge in `.claude/skills` and update the description if the trigger changed.
