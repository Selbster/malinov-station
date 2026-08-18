---
name: ss14-transform-system-core
description: In-depth practical guide to TransformSystem in SS14: coordinate model (EntityCoordinates/MapCoordinates), parent-grid-map hierarchy, safe movement, anchor/unbind and client/server patterns. Use for teleports, entity transfers, containers, anchoring and spatial optimizations.
---

# TransformSystem: Core

This skill covers the architecture and working techniques of TransformSystem.
For the full API catalog, see the separate skill `ss14-transform-system-api`.

## Mental model

Transform in SS14 = parent → grid → map hierarchy. Each entity has `ParentUid`, `GridUid`, `MapUid`/`MapID`. For coordinate spaces (`EntityCoordinates` vs `MapCoordinates`) and API selection, see `ss14-transform-system-api`.

Core focuses on **invariants, lifecycle, and safe movement patterns**.

## SS14 dimensions

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating | ☑ | `SharedTransformSystem` uses `UpdatesOutsidePrediction = true` — transforms update on both client and server. Content code subscribing to `MoveEvent` must account for prediction. |
| Server / Client split | ☑ | Server: authoritative movement, anchoring, containers. Client: visual interpolation, matrix rendering, mover coordinates for UI. |
| Event ordering | ☑ | `MoveEvent` fires after parent/position update but before grid traversal. `OnGlobalMoveEvent` fires after directed events. `EntParentChangedMessage` fires on parent change. |
| Hot path / allocations | ☑ | Prefer uid-overloads; resolve `TransformComponent` once per entity in loops. Use `EntityQuery<TransformComponent>` for batch traversal. |
| Component lifecycle | ☐ N/A | `TransformStartupEvent` is engine-internal. Content code uses `MapInitEvent` / `ComponentStartup`. |
| PVS / network visibility | ☐ N/A | Transform data feeds PVS but methods do not perform visibility filtering. |

## Moving pattern

Basic flow when moving through `SetCoordinates(...)`:

1. If necessary, remove the anchor (`Unanchor`), if the movement allows it.
2. Update the local position/rotation and, when changing the parent, recalculate the map/grid membership.
3. Raise motion events (`MoveEvent`, `EntParentChangedMessage`) and update spatial subsystems.
4. Perform post-step traversal so that the entity ends up on the correct grid/map parent.

Practical consequence:
- do not mix direct mutation of `TransformComponent` fields and system methods;
- system methods hold tree, broadphase and network state invariants.

## Quick API selection

1. Need to shift in local space without changing parent?
- `SetLocalPosition`, `SetLocalRotation`, `SetLocalPositionRotation`.
2. Need to be placed at a world point/angle?
- `SetWorldPosition`, `SetWorldRotation`, `SetWorldPositionRotation`, or `SetMapCoordinates`.
3. Do you need to transfer between parent-space (container/entity/grid/map)?
- `SetCoordinates`, then, if necessary, `AttachToGridOrMap`.
4. Should I "put it next to it" taking into account the containers?
- `DropNextTo` or `PlaceNextTo`.
5. Do you need to correctly compare the distance between different parent-spaces?
- `InRange` rather than manual subtraction of local vectors.

## Patterns

- After teleport or cross-grid/cross-map moves, normalize with `AttachToGridOrMap`. Intra-grid local moves and container transfers do NOT need it.
- To change the coordinate system, use `ToMapCoordinates`/`ToCoordinates`.
- For overlay/visualization, take pairs of matrices through `GetWorldPositionRotationMatrixWithInv`.
- For tile logic, use helper methods (`GetGridOrMapTilePosition`, `TryGetGridTilePosition`).
- For container-resistant drops, use `DropNextTo`.
- When handling `EntParentChangedMessage`, note that `OldMapId` is of type `EntityUid?` (the map **entity** UID), not `MapId` (enum). Compare directly against `Transform.MapUid` (also `EntityUid?`): `if (args.OldMapId != args.Transform.MapUid)`.

## Anti-patterns

- **Don't** apply purely cosmetic position/rotation updates (popups, visual indicators) from both server and client. **Do** gate these behind `_timing.IsFirstTimePredicted` or `_timing.IsClient` to avoid double-application. Authoritative state changes (actual movement, anchoring, container transfers) do NOT need this gate.
- **Don't** compare `EntityCoordinates.Position` from entities on different parent trees — they are in different coordinate spaces. **Do** convert to `MapCoordinates` via `ToMapCoordinates` first.
- **Don't** teleport via `SetCoordinates` without calling `AttachToGridOrMap` — entity may end up parented to the wrong grid/map. **Do** normalize after teleport.
- **Don't** use `SetGridId` directly in content code — it is a low-level method without `[Obsolete]` but bypasses traversal invariants and grid rebinding. **Do** use `AttachToGridOrMap` or `SetCoordinates` which handle grid rebinding automatically.
- **Don't** ignore `Try`-returns from `TryGetMapOrGridCoordinates`, `TryGetGridTilePosition` — they return false when the entity is in an invalid state. **Do** check the result.

## Known issues

- `SwapPositions` has documented client misprediction. Test swap mechanics on both server and connected client.

## Code examples

### Example 1: safe dash with parent-space normalization
// Verify: SharedMagicSystem.cs, GhostSystem.cs — SetCoordinates + AttachToGridOrMap.

```csharp
// 1) Transfer the entity to the target EntityCoordinates (from the ability).
_transform.SetCoordinates(user, xform, args.Target);

// 2) Normalize parent to the actual grid/map at the target point.
_transform.AttachToGridOrMap(user, xform);
```

### Example 2: "implanting" a projectile using SetParent
// Verify: SharedProjectileSystem.cs — SetParent + SetLocalPosition after reparent.

```csharp
// We stop physics and make the projectile static.
_physics.SetLinearVelocity(projectile, Vector2.Zero, body: body);
_physics.SetBodyType(projectile, BodyType.Static, body: body);

// Re-attach the projectile to the target.
_transform.SetParent(projectile, projectileXform, target);

// We apply local offset after the parent-change.
_transform.SetLocalPosition(
    projectile,
    projectileXform.LocalPosition + rotation.RotateVec(embedOffset),
    projectileXform);
```

### Example 3: overlay rendering using world+inv matrix
// Verify: ExplosionOverlay.cs, ExplosionSystem.GridMap.cs — GetWorldPositionRotationMatrixWithInv.

```csharp
var (_, _, worldMatrix, invWorldMatrix) =
    _transform.GetWorldPositionRotationMatrixWithInv(gridXform, xforms);

// We translate the camera bounds into local grid coordinates.
var localBounds = invWorldMatrix.TransformBox(worldBounds).Enlarged(grid.TileSize * 2);

// We draw in the local space of the grid.
drawHandle.SetTransform(worldMatrix);
```

### Example 4: anchoring/unanchoring using system methods only
// Verify: AnchorableSystem.cs, BlockingSystem.cs — AnchorEntity/Unanchor.

```csharp
if (!xform.Anchored)
    _transform.AnchorEntity(uid, xform);

// ...game logic...

if (xform.Anchored)
    _transform.Unanchor(uid, xform);
```

### Example 5: pop-up direction via mover-coordinates
// Verify: MagnetPickupSystem.cs, HandsSystem.cs, FlashEntityEffectSystem.cs — GetMoverCoordinates.

```csharp
// MoverCoordinates gives operational coordinates in grid/map terms.
var moverCoords = _transform.GetMoverCoordinates(observer);

// Based on them, we select the side of the signature.
var horizontalDir = moverCoords.X <= popupOrigin.X ? 1f : -1f;
```

### Example 6: map-change detection via EntParentChangedMessage
// Verify: EyeLerpingSystem.cs — grep EntParentChangedMessage.

```csharp
private void OnParentChanged(EntityUid uid, TransformComponent xform, ref EntParentChangedMessage args)
{
    // OldMapId is EntityUid? (map entity UID), NOT MapId enum.
    // Compare directly against current MapUid — both are EntityUid?.
    if (args.OldMapId != xform.MapUid)
    {
        // Entity changed map — update visual state, reset tracking, etc.
        _eyeLerping.OnMapChanged(uid);
    }
}
```

## Server and client usage guidelines

- **Server**: all authoritative mutations — `SetCoordinates`/`SetWorldPosition` for movement, `AnchorEntity`/`Unanchor` for anchoring, `DropNextTo`/`PlaceNextTo` for container-aware placement, `AttachToGridOrMap` after teleport.
- **Client**: matrix calculations for overlays via `GetWorldPositionRotationMatrixWithInv`, UI positioning via `GetMoverCoordinates`, tile queries via `TryGetGridTilePosition`. Cosmetic interpolation via `SetLocalPosition` (triggers `ActivateLerp`).
- **Shared**: coordinate conversions (`ToMapCoordinates`/`ToCoordinates`), distance checks (`InRange`), hierarchy queries (`GetParent`, `ContainsEntity`).

## Mini-checklist

- [ ] After teleport: `AttachToGridOrMap` called? (Only for cross-grid/cross-map moves)
- [ ] Anchor logic via `AnchorEntity`/`Unanchor` (not legacy setter)?
- [ ] Subscribing to `MoveEvent`? Remember it's `[ByRefEvent]` — use `ref`
- [ ] EntParentChangedMessage: comparing `OldMapId` against `Transform.MapUid` (both `EntityUid?`)?
- [ ] Server-side cosmetic change? Gated behind `_timing.IsFirstTimePredicted`?

## Source of truth

Architecture verified against RobustToolbox engine code and Content.* systems. The coordinate model is defined by `TransformComponent` properties: `ParentUid`, `GridUid`, `MapUid`, `MapID`, `LocalPosition`, `LocalRotation`. See sibling `ss14-transform-system-api` for method signatures.
Verified against fork HEAD: 2026-08-19.

## Extension rule

When adding new patterns to this skill:
1. Verify the pattern against at least 2 real Content.* systems.
2. Mark examples with `// Verify: <System>.cs — grep <Method>`.
3. Keep this skill under 300 lines; deep API details go to the api sibling.
