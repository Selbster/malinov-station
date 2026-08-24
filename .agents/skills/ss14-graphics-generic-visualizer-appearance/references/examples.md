# GenericVisualizer / Appearance — extended example catalog

Companion of `SKILL.md`. Confirm every snippet against RobustToolbox/Content HEAD before reuse.

## Example A: typed payload instead of scattered flags

```csharp
[Serializable, NetSerializable]
public sealed class ShowLayerData
{
    public string Key = string.Empty;   // Target layer key.
    public bool Visible;                // Whether the layer should be visible.
    public string? State;               // Optional RSI state.
}

private void PushLayerPayload(EntityUid uid, AppearanceComponent appearance, ShowLayerData data)
{
    // One semantic payload beats a pile of loose boolean keys.
    _appearance.SetData(uid, MapperVisuals.LayerData, data, appearance);
}
// Verify: SharedAppearanceSystem.cs — grep SetData(
```

The payload must stay clone-safe: `[Serializable, NetSerializable]` reference types are copied by
the serializer during state application; mutable shared instances would leak between entities.

Warning: `SetData` dedupe compares `Equals`, and classes get reference equality — pushing a fresh
instance with identical content still dirties and resyncs the whole dictionary. Push such payloads
only when content actually changed (compare fields yourself first), or prefer enums/value types
for frequently updated keys.

## Example B: transferring appearance data between entities

```csharp
private void CopyAppearance(Entity<AppearanceComponent?> source, Entity<AppearanceComponent?> target)
{
    // Complete replacement: destination data is cleared first.
    _appearance.CopyData(source, target);

    // Merge mode for partial enrichment; existing keys in dest are replaced per key.
    // _appearance.AppendData(source, target);
}
// Verify: SharedAppearanceSystem.cs — grep CopyData / AppendData
```

Both methods `EnsureComp<AppearanceComponent>` on the destination and queue an update.

## Example C: GenericVisualizer YAML (enum value -> state/shader/color)

```yaml
- type: GenericVisualizer
  visuals:
    enum.PowerVisuals.ChargeState:
      enum.PowerVisualLayers.Indicator:
        "Empty":
          state: empty
          shader: unshaded
        "Medium":
          state: medium
        "Full":
          state: full
          color: "#99ff99"
```

Variant strings match `LockerChargeState`-style enum `ToString()` output exactly.

## Example D: custom visualizer when timelines start

```csharp
protected override void OnAppearanceChange(EntityUid uid, TriggerVisualsComponent component,
    ref AppearanceChangeEvent args)
{
    if (args.Sprite == null)
        return;

    if (!_appearance.TryGetData(uid, TriggerVisuals.Active, out bool active, args.Component))
        return;

    // Not pure mapping anymore: a pulsing timeline is required -> delegate to AnimationPlayerSystem.
    _sprite.LayerSetRsiState((uid, args.Sprite), TriggerLayers.Core, active ? "active" : "idle");
    if (active && !_animation.HasRunningAnimation(uid, "pulse"))
        _animation.Play(uid, BuildPulseAnimation(), "pulse");
    else if (!active)
        _animation.Stop(uid, null, "pulse");
}
// Verify: sibling skill ss14-graphics-animation-player — Play/Stop guards and Finished handling
```

This is the promotion point from the decision tree: appearance reports discrete truth,
the animation system owns the timeline.

## Example E: direct component-state visuals without AppearanceComponent

For continuous or high-frequency data, skip appearance entirely: network the field on a shared
`[NetworkedComponent]` and apply it to sprite layers from `ComponentHandleState`.

```csharp
// Shared component (server writes, client reads):
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(true)]
public sealed partial class GaugeVisualsComponent : Component
{
    [AutoNetworkedField] public float Level;
}

// Client system:
public override void Initialize()
{
    base.Initialize();
    SubscribeLocalEvent<GaugeVisualsComponent, ComponentHandleState>(OnHandleState);
}

private void OnHandleState(EntityUid uid, GaugeVisualsComponent component, ref ComponentHandleState args)
{
    if (args.Current is not GaugeVisualsComponentState state)
        return;

    // Quantize here so per-tick floats do not become per-tick sprite writes.
    var bucket = (int) MathF.Ceiling(state.Level * 4f);
    ...
}
// Verify: Content.Client/Sprite/RandomSpriteSystem.cs — grep SubscribeLocalEvent<RandomSpriteComponent, ComponentHandleState>
// Verify: Content.Shared — grep AutoGenerateComponentState(true) / AutoNetworkedField for the attribute pair
```

Triggering condition: values change continuously (fills, temperatures, counters) where every
appearance write would cost a full-dictionary resync. Non-triggering condition: discrete states —
`GenericVisualizer` stays cheaper and declarative. Note the trade-off: you own the diffing,
layer mapping and PVS re-entry handling that the appearance pipeline gave for free.
