---
name: ss14-graphics-sprite-system
description: An in-depth practical guide to SpriteSystem in Space Station 14: lifecycle, goal-grouped API map, working with layers and layer-map, practical patterns and anti-patterns. Use it when developing dynamic sprites, visualizer systems and refactoring outdated SpriteComponent calls.
---

# SpriteSystem in SS14

This skill only covers `SpriteSystem` and the practice of its application in the current SS14 architecture :)
Related topics live in sibling skills: the appearance pipeline in `ss14-graphics-generic-visualizer-appearance`, timed animations in `ss14-graphics-animation-player`.

## When to use

Use `SpriteSystem` when you need:

- change the appearance of an entity at runtime on the client;
- manage layers: visibility, color, offset, RSI/texture;
- dynamically add/remove layers;
- use the layer map for stable addressing by key;
- make precise visual updates from visualizer systems.

Keep static visuals as plain prototype data (no dynamics needed), and keep networked business logic out of client visual code.

## Source of truth

- Engine code wins over docs: `SpriteSystem` partial classes in Robust.Client define every method; `SpriteComponent` keeps only `[Obsolete]` proxy wrappers that redirect into the system.
- Treat direct `SpriteComponent` call examples anywhere as historical.
- Facts that cannot be verified against the fork's current code must be marked `[unverified]`.

## SpriteSystem mental model

1. The server/shared layer decides the state of gameplay.
2. The client receives state data (often via an appearance/visualizer).
3. The client system calls `SpriteSystem` and changes only the visual appearance.
4. A sprite renders from a set of layers with their own parameters.
5. The layer map gives stable keys (`Enum`/`string`) instead of "magic" indexes.

Idea: visuals change locally, deterministically and cheaply over the network :)

## API map (grouped by goal)

All methods take `Entity<SpriteComponent?>` first; `(uid, sprite)` tuples convert implicitly.

- **Whole-sprite setters:** `SetScale`, `SetRotation`, `SetOffset`, `SetVisible`, `SetDrawDepth(int)`, `SetColor`, `SetBaseRsi(RSI?)`, `SetContainerOccluded`, `SetSnapCardinals`, `SetGranularLayersRendering`.
- **Layer CRUD:** `AddBlankLayer`, `AddLayer(Layer/SpriteSpecifier/PrototypeLayerData)`, `AddRsiLayer(RSI.StateId, RSI?, int?)`, `AddTextureLayer`, `RemoveLayer(..., logMissing = true)`, `TryGetLayer`, `LayerExists`.
- **Layer map:** `LayerMapReserve(sprite, key) -> int` (preferred entry point), `LayerMapSet/Add/Remove/TryGet/Get`; supports `Enum` and `string` keys — prefer `Enum` in project code (`string` is natural for layers defined in YAML).
- **Layer mutators (main working set):** `LayerSetData`, `LayerSetSprite`, `LayerSetTexture`, `LayerSetRsiState`, `LayerSetRsi`, `LayerSetScale`, `LayerSetRotation`, `LayerSetOffset`, `LayerSetVisible`, `LayerSetColor`, `LayerSetDirOffset`, `LayerSetAnimationTime`, `LayerSetAutoAnimated`, `LayerSetRenderingStrategy`.
- **Layer getters:** `LayerGetRsiState`, `LayerGetEffectiveRsi`, `LayerGetDirections`, `LayerGetDirectionCount`.
- **Bounds/render/helpers:** `GetLocalBounds(sprite|layer)`, `CalculateBounds`, `RenderSprite`, `GetFrame(spec, time)`, `Frame0`, `RsiStateLike`, `GetIcon`, `GetPrototypeIcon`, `GetPrototypeTextures`, `GetFallbackState`, `GetFallbackTexture`, `GetState`, `GetTexture`, `GetSpriteWorldPosition`, `GetSpriteScreenCoordinates`.
- **Utilities:** `ForceUpdate(uid)` — force an immediate update pass; `SetAutoAnimateSync`; `CopySprite(source, target)`; `QueueUpdateIsInert` / `QueueUpdateInert`.

### Overload pattern

Most layer methods come in four flavors: by index `int`, by key `Enum`, by key `string`, or by `Layer` object.
Rule: address gameplay layers through the layer map with `Enum` keys; pick the overload matching what you already hold (index from `LayerMapReserve`, or a `Layer` you already resolved).

## Must know about obsolete wrappers

`SpriteComponent` has many deprecated proxy methods (`[Obsolete]`) that redirect to `SpriteSystem`.

Why direct calls to `SpriteComponent` are an anti-pattern:

- they blur the unified update API;
- complicate refactoring and auditing of visual changes;
- break consistency with the modern ECS style in the project;
- increase the risk of silent regressions when the engine changes.

Briefly: new code goes through `SpriteSystem`, not through old component methods.

## Practical examples

### Example 1: Basic entity sprite setup

```csharp
public void ApplyMachineLook(EntityUid uid, SpriteComponent sprite, bool highlighted)
{
    // Change only visual properties of the entity.
    _sprite.SetVisible((uid, sprite), true);
    _sprite.SetDrawDepth((uid, sprite), (int)DrawDepth.WallMountedItems);
    _sprite.SetColor((uid, sprite), highlighted ? Color.Cyan : Color.White);
}
// Verify: SpriteSystem.Setters.cs — grep SetDrawDepth(int); Content.Shared DrawDepth enum has no "Machines" member — grep WallMountedItems.
```

### Example 2: layer reserve by enum key and data filling

```csharp
private enum MachineLayerKey : byte
{
    Base,
    Status
}

public void SetStatusLayer(EntityUid uid, SpriteComponent sprite, PrototypeLayerData data)
{
    // Guarantee a layer exists under this key.
    var index = _sprite.LayerMapReserve((uid, sprite), MachineLayerKey.Status);

    // Update the whole layer via PrototypeLayerData.
    _sprite.LayerSetData((uid, sprite), index, data);
    _sprite.LayerSetVisible((uid, sprite), index, true);
}
// Verify: SpriteSystem.LayerMap.cs — LayerMapReserve(Entity<SpriteComponent?>, Enum); LayerSetters.cs — LayerSetData(Entity, int, PrototypeLayerData).
```

### Example 3: Safely deleting a layer by key

```csharp
public void HideStatusLayer(EntityUid uid, SpriteComponent sprite)
{
    // Absence of the layer is not an error here: this is an idempotent flow.
    _sprite.RemoveLayer((uid, sprite), MachineLayerKey.Status, logMissing: false);
}
// Verify: SpriteSystem.LayerMap.cs — RemoveLayer(Entity<SpriteComponent?>, Enum key, bool logMissing = true).
```

### Example 4: Dynamically generating and clearing text layers

```csharp
public void RebuildTextLayers(EntityUid uid, SpriteComponent sprite, IReadOnlyList<int> oldLayers, string text)
{
    // First clear the old temporary layers.
    foreach (var old in oldLayers)
        _sprite.RemoveLayer((uid, sprite), old, logMissing: false);

    var x = 0f;
    foreach (var ch in text)
    {
        // One layer per character.
        var layer = _sprite.AddRsiLayer((uid, sprite), new RSI.StateId(ch.ToString()), _fontRsi);
        _sprite.LayerSetOffset((uid, sprite), layer, new Vector2(x, 0f));
        _sprite.LayerSetVisible((uid, sprite), layer, true);

        x += 0.5f; // Step between characters.
    }
}
// Verify: SpriteSystem.Layer.cs — AddRsiLayer(Entity<SpriteComponent?>, RSI.StateId, RSI? = null, int? = null).
```

### Example 5: Directional layer offsets

```csharp
public void ApplyDirectionalOffset(EntityUid uid, SpriteComponent sprite, Enum pipeLayerKey, DirectionOffset dirOffset)
{
    // The same layer looks different per direction.
    _sprite.LayerSetDirOffset((uid, sprite), pipeLayerKey, dirOffset);
}
// Verify: SpriteSystem.LayerSetters.cs — grep LayerSetDirOffset(Enum key variant exists alongside int/string/Layer).
```

### Example 6: Bit mask for a group of indicator layers

```csharp
[Flags]
public enum IndicatorBits : byte
{
    None = 0,
    Powered = 1 << 0,
    Charging = 1 << 1,
    Broken = 1 << 2
}

public void UpdateIndicators(EntityUid uid, SpriteComponent sprite, IndicatorBits bits)
{
    // Each flag controls its own layer, without long if-chains for states.
    // String keys are correct HERE because these layers come from a YAML-defined layerMap;
    // for runtime-created layers prefer Enum keys via LayerMapReserve.
    _sprite.LayerSetVisible((uid, sprite), "powered",  (bits & IndicatorBits.Powered)  != 0);
    _sprite.LayerSetVisible((uid, sprite), "charging", (bits & IndicatorBits.Charging) != 0);
    _sprite.LayerSetVisible((uid, sprite), "broken",   (bits & IndicatorBits.Broken)   != 0);
}
// Verify: SpriteSystem.LayerSetters.cs — RemoveLayer/LayerSetVisible have string-key overloads.
```

### Example 7: replacing base RSI + draw depth in one update

```csharp
public void ApplyStorageVisualMode(EntityUid uid, SpriteComponent sprite, bool opened)
{
    // Change base RSI and render depth as one logical update.
    _sprite.SetBaseRsi((uid, sprite), opened ? _openedRsi : _closedRsi);
    _sprite.SetDrawDepth((uid, sprite), opened ? (int)DrawDepth.SmallObjects : (int)DrawDepth.Objects);
    _sprite.ForceUpdate(uid); // Push an immediate visual update.
}
// Verify: SpriteSystem.Setters.cs — SetBaseRsi(Entity, RSI?); Component.cs — ForceUpdate(EntityUid).
```

## Patterns 🙂

- Use `LayerMapReserve` + `Enum` keys for stable layer addressing (`string` keys only for YAML-defined layers).
- Treat a layer as the minimal unit of visual state, not "the whole sprite at once".
- Group changes belonging to one visual event into one method.
- For temporary layers always run an explicit cleanup cycle (also on component removal/shutdown).
- For complex indicators use bit masks instead of cascades of boolean fields.
- For identical visual entities copy the configuration via `CopySprite`.

## Anti-patterns ❌

- Direct deprecated calls to `SpriteComponent` instead of `SpriteSystem`.
- Working with "magic indexes" of layers without a layer map.
- Mixing gameplay logic and visual updates in one method.
- Full rebuilds of all layers when a single flag changed.
- Using `string` keys where an `Enum` layer-map key already exists.
- Suppressing `logMissing` when a missing layer actually indicates a bug — silence warnings only where absence is an expected, idempotent outcome.

## Checklist before change ✅

- Is the visual changed through `SpriteSystem`, not through the obsolete API?
- Are key layers addressed via `Enum`/layer map?
- Is there symmetric clearing for dynamic layers?
- Do local updates avoid rebuilding the entire sprite without reason?
- Is the right method chosen: `LayerSetData` (bulk) or a point mutator?
- Does the change keep network business logic out of client visual code?

## Common errors

- The layer exists in the prototype but is not reserved/linked to the expected key.
- State updated, but the target layer stayed invisible.
- Wrong overload used (index instead of key, or vice versa).
- Temporary layers never cleared and accumulate between updates.
- Wrong `DrawDepth` makes the object "disappear" behind neighboring entities.
- Trying to fix an architectural problem with `ForceUpdate` instead of the correct update flow.

## SS14 dimension checklist

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating (`InPrediction` / `IsPredictionEnabled`) | N/A | pure client-side visuals |
| Server / Client / Shared split + `[NetworkedComponent]` | N/A | `SpriteSystem` is client-only; server conveys state via appearance data |
| Event and `UpdatesBefore`/`UpdatesAfter` ordering | N/A | driven by appearance events/frame updates |
| Component lifecycle (Add/Remove/Initialize/Shutdown) | ☑ | clear temporary layers on removal/shutdown to avoid accumulation |
| Hot path and allocations | ☑ | mutate single layers instead of full rebuilds; batch changes per event |
| PVS / network visibility of client-side logic | N/A | sprites render locally |

## Extension/change rule

New method groups go under "API map" grouped by goal; deep per-method recipes go to `references/`. Appearance networking stays in `ss14-graphics-generic-visualizer-appearance`; timed animations stay in `ss14-graphics-animation-player`. Keep this file under 300 lines.

Verified against code state: 2026-08-23
