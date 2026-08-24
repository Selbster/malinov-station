---
name: ss14-graphics-generic-visualizer-appearance
description: A practical and architectural guide to the combination of AppearanceComponent, AppearanceSystem, VisualizerSystem and GenericVisualizer in SS14. Use it when designing network visual states, YAML visualizations and client visualizer systems.
---

# GenericVisualizer and Appearance in SS14

Scope: the pipeline `Appearance` + `VisualizerSystem` + `GenericVisualizer`.
Low-level rendering and the detailed `SpriteSystem` API live in `ss14-graphics-sprite-system`;
timelines/flicks belong to `ss14-graphics-animation-player`.

Triggering condition: discrete `data -> layer/state/color/shader` mapping.
Non-triggering condition: time-based effects — hand those to `AnimationPlayerSystem`;
continuous high-frequency data — see decision tree item 5.

## Reading order

1. This file top-to-bottom: architecture -> enum contract -> API -> decision tree -> examples.
2. Extended catalog of recipes: `references/examples.md`.

Source of truth: current code wins over docs; verify every API against implementation before reuse.

## Mental model

The server reports "what is visually true"; the client decides "how to draw".

> **Single most important pattern**: appearance keys are shared enums; values are small,
> coarse, clone-safe data. Every real change dirties the whole component and resyncs the
> entire dictionary — keep writes rare and semantic.

## End-to-end architecture

1. Server logic computes the visual state and writes it via `SharedAppearanceSystem.SetData(...)`.
2. Data enters `AppearanceComponent.AppearanceData` and is synchronized over the network.
3. Client `AppearanceSystem` applies state, replaces its dictionary with a clone, and queues an update.
4. During client `FrameUpdate`, `AppearanceChangeEvent` is raised (batched per frame).
5. Client visualizer systems (`VisualizerSystem<T>`) apply data to sprite layers; pure value->layer
   mapping is delegated to `GenericVisualizerSystem`.

## Hard invariants

### Network cost

- Any effective write calls `Dirty(uid, component)` -> a full `AppearanceComponentState`
  (the whole `Dictionary<Enum, object>`) is serialized; the receiving side replaces and clones
  its entire dictionary on apply.
- Equal-value `SetData` writes are deduplicated (`existing.Equals(value)` -> no-op). Dedupe uses .NET
  equality, so class payloads compare by reference — re-pushing an equal-content instance still resyncs.
- `RemoveData` has no dedupe at all: it dirties even when the key was absent. Call it only when
  the key is actually set.
- Changing values back and forth still costs two full syncs; quantize continuous values into coarse
  semantic enums (e.g. `ChargeState.Empty/Medium/Full`) before they reach appearance.

### State-application gating (prediction)

- While network state is being applied (`IGameTiming.ApplyingState`) and the component has
  `NetSyncEnabled`, `SetData`/`RemoveData` **silently return without changing anything**.
- Never write appearance from predicted or client-driven gameplay paths; compute it in server
  logic. A write that lands during state application is simply lost.

### PVS behavior

- Client `FrameUpdate` skips detached entities; events are not raised for out-of-PVS entities.
- On re-entering PVS, `ComponentHandleState` requeues an update, so visuals self-heal.

### VisualizerSystem<T> conveniences

Inheriting `VisualizerSystem<T>` gives you `AppearanceSystem`, `SpriteSystem` and
`AnimationPlayerSystem` (field `AnimationSystem`) dependencies for free.

```text
// Verify: Shared/GameObjects/Systems/SharedAppearanceSystem.cs — grep CheckIfApplyingState / Dirty(
// Verify: Client/GameObjects/EntitySystems/AppearanceSystem.cs — grep FrameUpdate / CloneAppearanceData
// Verify: Client/GameObjects/EntitySystems/VisualizerSystem.cs — grep AnimationSystem
```

## Enum contract: which enums are needed and in what order

Two different enum contracts are involved, sometimes three:

1. `AppearanceKey enum` (shared) — keys of the `AppearanceData` dictionary,
   passed to `SetData` / `TryGetData` / `RemoveData`.
2. `LayerKey enum` (client + prototype visualization) — keys of sprite layers (layer map),
   used by `SpriteSystem` and in `GenericVisualizer` YAML.
3. `AppearanceValue enum` (optional, shared) — a **value** of appearance data,
   e.g. `ChargeState.Empty/Medium/Full`.

Critically important: `AppearanceKey` and `LayerKey` play different roles and must not be mixed ⚠

### Order of application

1. Describe the `AppearanceKey` enum in the shared contract.
2. Describe the `LayerKey` enum for layer addressing.
3. Server writes values: `SetData(uid, AppearanceKey.SomeKey, value)`.
4. Client: a custom visualizer reads `AppearanceKey` via `TryGetData` and applies it to `LayerKey`
   via `SpriteSystem`; GenericVisualizer maps `AppearanceKey -> LayerKey -> value-string ->
   PrototypeLayerData` in YAML.

```csharp
[Serializable, NetSerializable]
public enum LockerVisuals : byte // 1) AppearanceKey enum
{
    Open,
    ChargeState,
}

[Serializable, NetSerializable]
public enum LockerVisualLayers : byte // 2) LayerKey enum
{
    DoorOpen,
    DoorClosed,
}

[Serializable, NetSerializable]
public enum LockerChargeState : byte // 3) AppearanceValue enum (optional)
{
    Empty,
    Medium,
    Full,
}

// Server: keys + values only
_appearance.SetData(uid, LockerVisuals.ChargeState, LockerChargeState.Full, appearance);
```

## API parsing

### `SharedAppearanceSystem` (shared)

- `SetData(EntityUid, Enum key, object value, AppearanceComponent?)`
- `RemoveData(EntityUid, Enum, AppearanceComponent?)`
- `TryGetData<T>(EntityUid, Enum, out T, AppearanceComponent?)`
- `TryGetData(EntityUid, Enum, out object?, AppearanceComponent?)`
- `CopyData(Entity<AppearanceComponent?> src, Entity<AppearanceComponent?> dest)` — clears dest first
- `AppendData(Entity<AppearanceComponent?> src, Entity<AppearanceComponent?> dest)` — merge/replace per key;
  an overload taking a resolved source component also exists
- `QueueUpdate(...)` — virtual; base is a server-side no-op, the client overrides it

Keys are `Enum`; values must survive cloning: value type, `ICloneable`, or serializer-copyable —
otherwise state application throws `NotSupportedException`. Initial values may also be seeded from
prototype YAML via the read-only `AppearanceDataInit` data field.

### Client `AppearanceSystem`

- Keeps an update queue; `FrameUpdate` drains it and raises `AppearanceChangeEvent`
  only for running, non-detached entities.
- `Sprite` may be null in the event (spriteless appearances are legal); always null-check.
- Cloning requirements as above; non-clonable payloads break state application.

### Server `AppearanceSystem`

- Issues `AppearanceComponentState` from `ComponentGetState`; knows nothing about rendering.

### `VisualizerSystem<T>` / `GenericVisualizerSystem`

- Custom visualizers override `OnAppearanceChange(EntityUid uid, T component, ref AppearanceChangeEvent args)`.
- `GenericVisualizerComponent.Visuals` is
  `Dictionary<Enum, Dictionary<string, Dictionary<string, PrototypeLayerData>>>`.
- Algorithm: read key -> `ToString()` (null/empty skipped) -> look up variant ->
  resolve layer key (enum reference via reflection or raw string) ->
  `LayerMapReserveBlank` (auto-creates the layer) -> apply `PrototypeLayerData`.

## Decision tree: GenericVisualizer vs custom vs direct state?

1. Pure mapping "value X -> state/visible/color/shader/offset"? Use `GenericVisualizer` ✅
2. Timers, animations, external systems, complex calculations? Custom `VisualizerSystem<T>` ✅
3. Layers created imperatively at runtime by logic? Custom ✅
   (declarative auto-creation of static layers is fine with `LayerMapReserveBlank`)
4. Non-trivial composition of several appearance keys? Usually custom ✅
5. Continuous/high-frequency data whose appearance writes would spam full-dict resyncs?
   Skip appearance: network the data via `[AutoNetworkedField]` on a `[NetworkedComponent]` and drive
   sprite changes client-side from `ComponentHandleState` — common in modern content.
   Non-triggering: one-off discrete visuals — GenericVisualizer stays cheaper ✅

## Practical examples

### Example 1: server writes and clears a flag symmetrically

```csharp
private void UpdateLockerAppearance(EntityUid uid, bool open, AppearanceComponent? appearance = null)
{
    // Symmetry matters: leaving stale keys keeps layers in their last applied state.
    if (open)
        _appearance.SetData(uid, LockerVisuals.Open, true, appearance);
    else
        _appearance.RemoveData(uid, LockerVisuals.Open, appearance);
}
// Verify: SharedAppearanceSystem.cs — grep SetData / RemoveData / CheckIfApplyingState
```

### Example 2: client visualizer reads typed data

```csharp
protected override void OnAppearanceChange(EntityUid uid, LockerVisualsComponent component,
    ref AppearanceChangeEvent args)
{
    if (args.Sprite == null)
        return;

    // Reads AppearanceKey (LockerVisuals), applies to LayerKey (LockerVisualLayers).
    if (_appearance.TryGetData(uid, LockerVisuals.Open, out bool open, args.Component))
    {
        _sprite.LayerSetVisible((uid, args.Sprite), LockerVisualLayers.DoorOpen, open);
        _sprite.LayerSetVisible((uid, args.Sprite), LockerVisualLayers.DoorClosed, !open);
    }
}
// Verify: VisualizerSystem.cs — grep OnAppearanceChange
```

The event struct also exposes `args.AppearanceData` (`IReadOnlyDictionary<Enum, object>`)
for direct reads without resolving the component.

### Example 3: GenericVisualizer YAML (bool -> visibility)

```yaml
- type: GenericVisualizer
  visuals:
    enum.LockerVisuals.Open:
      enum.LockerVisualLayers.DoorOpen:
        "True":
          visible: true
      enum.LockerVisualLayers.DoorClosed:
        "True":
          visible: false
        "False":
          visible: true
  # Verify: GenericVisualizerSystem.cs — grep LayerMapReserveBlank
```

Variant strings must match `ToString()` output exactly — booleans serialize as `"True"`/`"False"`.

Extended catalog lives in `references/examples.md`: typed payload class, `CopyData`/`AppendData`
migration, enum value -> state/shader YAML, custom visualizer that starts animations, and the
direct-state alternative without `AppearanceComponent`.

## Patterns 🙂

1. Keep appearance keys in shared enums so both sides speak one contract — prevents silent read misses.
2. Prefer coarse semantic values over high-frequency raw data — prevents per-change full-dict resync spam.
3. Write appearance only from server logic / non-predicted paths — prevents writes lost to state application.
4. Guard `RemoveData` behind "key actually exists" knowledge — prevents no-op dirties that resync everything.
5. Use `GenericVisualizer` for declarative mapping only; promote to custom `VisualizerSystem<T>` once timelines or cross-key logic appear.
6. Clean up symmetrically: `RemoveData` for invalidated keys; `CopyData`/`AppendData` for entity migrations.
7. Keep visualizers idempotent: derive everything from current data, never from event count or order.
8. Reuse the dependencies provided by `VisualizerSystem<T>` instead of re-resolving systems.

## Anti-patterns ❌

- High-frequency `SetData` of continuously changing values (per-tick floats) — full-dict resync each change.
- Re-pushing class payloads (`Equals` is reference equality) — every push resyncs despite identical content.
- `RemoveData` for keys never written — still dirties and resyncs the whole dictionary.
- Writing appearance from predicted/client gameplay paths — silently ignored during state application.
- Passing non-clonable/non-serializable reference objects as values.
- Using strings instead of shared enums for keys.
- Passing the `LayerKey` enum into `SetData`/`TryGetData` instead of the `AppearanceKey` enum.
- Stuffing branches/timers/animations into `GenericVisualizer` YAML.
- Duplicating one visual logic across unrelated visualizer systems.
- Leaving stale keys behind — "stuck" visuals after state reversal.
- Mixing server state computation and client layer application in one method.

## Checklist before change ✅

- Are key enums and payload types declared in shared code?
- Are all appearance values clone/copy safe?
- Right tool chosen: `GenericVisualizer` vs custom visualizer vs direct component-state visuals?
- Are writes symmetric (`SetData`/`RemoveData`) and free of dead stores?
- Is every `RemoveData` backed by a key that is actually set?
- Is the visualizer idempotent and order-independent?
- Does visual logic stay on the correct side of the wire?

## Common errors

- Variant string in YAML does not match `ToString()` of the actual value (`"True"` vs `"true"`).
- Swapped roles of `AppearanceKey` and `LayerKey` (most common first-implementation mistake).
- Payload not serializable -> exceptions during state application.
- Reading an appearance key the server never sets.
- Wrong layer addressed due to inconsistent layer keys.
- After `CopyData` migration, dependent client components not refreshed.

## SS14 dimension checklist

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating | Covered | Writes no-op while `_timing.ApplyingState`; server-only writes |
| Server / Client / Shared split | Covered | Shared system, server GetState, client queue/events, `[NetworkedComponent]` appearance |
| Event / update ordering | Covered | `AppearanceChangeEvent` raised from client `FrameUpdate`, batched per frame |
| Component lifecycle | Covered | Startup queues initial update; `CopyData`/`AppendData` for migrations |
| Hot path & allocations | Covered | Full-dict resync per change; `SetData` dedupes via `Equals`, `RemoveData` never does |
| PVS / network visibility | Covered | Detached entities skipped; PVS re-entry requeues update |

## Extension and change rule

Add new declarative mappings as YAML variants; move long catalogs to `references/examples.md`.
Promote to a custom `VisualizerSystem<T>` when timelines or cross-key logic appear, keeping the
enum contract intact. For high-frequency continuous data prefer the direct component-state path
(decision tree item 5) over appearance. Re-verify API signatures against RobustToolbox HEAD on any upstream sync.

Verified against code state: 2026-08-23
