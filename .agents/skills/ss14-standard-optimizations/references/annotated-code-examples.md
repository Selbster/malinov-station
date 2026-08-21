# Annotated code examples

Below are examples verified against real code.  
Format: subsystem, signature, layer, relevance, then a fragment with explanations.

## Example 1: precomputation before nested loops (real fast-path)

- Subsystem: storage area insert
- Signature: position/angle selection section in the placement verification method
- Layer: `shared`
- Relevance: `2025-05`

```csharp
var itemShape = ItemSystem.GetItemShape(itemEnt); // One shape calculation before cycles.
var fastAngles = itemShape.Count == 1;
var fastPath = itemShape.Count == 1 && itemShape[0].Contains(Vector2i.Zero);

var angles = new ValueList<Angle>();
if (!fastAngles)
{
    for (var angle = startAngle; angle <= Angle.FromDegrees(360 - startAngle); angle += Math.PI / 2f)
        angles.Add(angle); // We prepared a set of corners once.
}
else
{
    angles.Add(startAngle);
    if (itemShape[0].Width != itemShape[0].Height)
        angles.Add(startAngle + Angle.FromDegrees(90));
}

while (chunkEnumerator.MoveNext(out var storageChunk))
{
    for (var y = bottom; y <= top; y++)
    {
        for (var x = left; x <= right; x++)
        {
            foreach (var angle in angles)
            {
                // Within the most expensive section we use only pre-calculated data.
                // This eliminates the need to re-calculate shape/angles for each tile.
            }
        }
    }
}

// Verify: SharedStorageSystem.cs — grep GetItemShape(itemEnt)
```

## Example 2: `fieldDeltas + DirtyField` for point changes

- Subsystem: proximity detector
- Signature: `public override void Update(float frameTime)`
- Layer: `shared`
- Relevance: current

```csharp
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(fieldDeltas: true), AutoGenerateComponentPause]
public sealed partial class ProximityDetectorComponent : Component
{
    [ViewVariables, AutoNetworkedField]
    public EntityUid? Target;

    [ViewVariables, AutoNetworkedField]
    public float Distance = float.PositiveInfinity;

    [DataField, AutoNetworkedField]
    public TimeSpan UpdateCooldown = TimeSpan.FromSeconds(1);

    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoNetworkedField, AutoPausedField]
    public TimeSpan NextUpdate = TimeSpan.Zero;
}

public override void Update(float frameTime)
{
    var query = EntityQueryEnumerator<ProximityDetectorComponent>();

    while (query.MoveNext(out var uid, out var component))
    {
        if (component.NextUpdate > _timing.CurTime)
            continue;

        component.NextUpdate += component.UpdateCooldown;
        DirtyField(uid, component, nameof(ProximityDetectorComponent.NextUpdate)); // Only the changed field goes out.

        if (!_toggle.IsActivated(uid))
            continue;

        UpdateTarget((uid, component));
    }
}

// Verify: ProximityDetectionSystem.cs + ProximityDetectorComponent.cs — grep DirtyField
```

## Example 3: cache `EntityQuery<T>` in `Initialize()`

- Subsystem: shared disposal holder
- Signature: `public override void Initialize()`
- Layer: `shared`
- Relevance: current

```csharp
private EntityQuery<TransformComponent> _xformQuery;

public override void Initialize()
{
    _xformQuery = GetEntityQuery<TransformComponent>(); // Cache once per system lifetime.
}

// Later, in a hot path:
if (!_xformQuery.TryGetComponent(entity, out var transform))
    return; // Quick early exit without general-purpose lookups.

// Verify: SharedDisposalHolderSystem.cs — grep _xformQuery
```

## Example 4: `ByRef record struct` for a frequent local event

- Subsystem: power charge machine
- Signature: `[ByRefEvent] public record struct ...`
- Layer: `server`
- Relevance: `2024-08`

```csharp
[ByRefEvent] public record struct ChargedMachineActivatedEvent;
[ByRefEvent] public record struct ChargedMachineDeactivatedEvent;

private void Notify(EntityUid uid, bool active)
{
    if (active)
    {
        var ev = new ChargedMachineActivatedEvent();
        RaiseLocalEvent(uid, ref ev); // By-ref call.
        return;
    }

    var off = new ChargedMachineDeactivatedEvent();
    RaiseLocalEvent(uid, ref off);
}

// Verify: PowerChargeSystem.cs — grep ChargedMachineActivatedEvent
```

## Example 5: reusing `ValueList` without per-frame allocations

- Subsystem: visual effects
- Signature: `public override void Update(float frameTime)`
- Layer: `client`
- Relevance: `2024-06`

```csharp
private ValueList<EntityUid> _toRemove = new();

public override void Update(float frameTime)
{
    var query = AllEntityQuery<ColorFlashEffectComponent>();
    _toRemove.Clear(); // Clearing instead of new ValueList<...>().

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

## Example 6: `ArrayPool<T>` + `ReadOnlySpan<T>` when sending

- Subsystem: device network
- Signature: `private void SendPacket(...)` / `private void SendToConnections(...)`
- Layer: `server`
- Relevance: `2022-05` (soft-exception: canonical pattern, still applicable)

```csharp
var deviceCopy = ArrayPool<DeviceNetworkComponent>.Shared.Rent(totalDevices);
try
{
    devices.CopyTo(deviceCopy);
    SendToConnections(deviceCopy.AsSpan(0, totalDevices), packet);
}
finally
{
    ArrayPool<DeviceNetworkComponent>.Shared.Return(deviceCopy);
}

private void SendToConnections(ReadOnlySpan<DeviceNetworkComponent> connections, DeviceNetworkPacketEvent packet)
{
    foreach (var connection in connections)
    {
        // We work with span without unnecessary copies of the list.
    }
}

// Verify: DeviceNetworkSystem.cs — grep ArrayPool<DeviceNetworkComponent>
```

## Example 7: `EntityQuery<T>` in the system API instead of repeating general checks

- Subsystem: tags
- Signature: `public override void Initialize()` / `public bool HasTag(...)`
- Layer: `shared`
- Relevance: `2024-05`

```csharp
private EntityQuery<TagComponent> _tagQuery;

public override void Initialize()
{
    _tagQuery = GetEntityQuery<TagComponent>();
}

public bool HasTag(EntityUid uid, ProtoId<TagPrototype> tag)
{
    return _tagQuery.TryComp(uid, out var component) &&
           component.Tags.Contains(tag);
}

// Verify: TagSystem.cs — grep _tagQuery.TryComp
```

## Example 8: ordering components in a query by cardinality

- Subsystem: ECS runtime
- Signature: `EntityQuery<TComp1, TComp2>(...)`
- Layer: `engine`
- Relevance: `2023-11` (soft-exception: canonical engine comment)

```csharp
// There is a direct indication in runtime:
// "you really want trait1 to be the smaller set of components"

// Practical conclusion:
var query = EntityQueryEnumerator<ActiveTimerTriggerComponent, TimerTriggerComponent>();
// First the rarer component to reduce the overlap.

// Verify: RobustToolbox EntityManager.Components.cs — grep "smaller set of components"
```

## Example 9: incremental counter instead of state recalculation

- Subsystem: ranged gun burst mode
- Signature: shot processing area in `SharedGunSystem`
- Layer: `shared`
- Relevance: `2024-10` (used in the current code 2025+)

```csharp
if (gun.Comp.SelectedMode == SelectiveFire.Burst)
{
    gun.Comp.BurstActivated = true;
}
if (gun.Comp.BurstActivated)
{
    gun.Comp.BurstShotsCount += shots; // Incremental counter at the mutation site.

    if (gun.Comp.BurstShotsCount >= gun.Comp.ShotsPerBurstModified)
    {
        gun.Comp.NextFire += TimeSpan.FromSeconds(gun.Comp.BurstCooldown);
        gun.Comp.BurstActivated = false;
        gun.Comp.BurstShotsCount = 0; // Reset when the burst window ends.
    }
}

// Verify: SharedGunSystem.cs — grep BurstShotsCount
```

Comment: the system does not “recalculate the shot history” to check the burst limit, but updates the counter at the point where the state changes.

## Example 10: staggered per-entity timer instead of uniform sweeps

- Subsystem: thirst
- Signature: `public override void Update(float frameTime)`
- Layer: `shared`
- Relevance: current

```csharp
var query = EntityQueryEnumerator<ThirstComponent>();
while (query.MoveNext(out var uid, out var thirst))
{
    if (_timing.CurTime < thirst.NextUpdateTime)
        continue;

    thirst.NextUpdateTime += thirst.UpdateRate; // Advance, don't reset: cadence stays stable under lag.

    ModifyThirst(uid, thirst, -thirst.ActualDecayRate);
    var calculatedThirstThreshold = GetThirstThreshold(thirst, thirst.CurrentThirst);

    if (calculatedThirstThreshold == thirst.CurrentThirstThreshold)
        continue;

    thirst.CurrentThirstThreshold = calculatedThirstThreshold;
    UpdateEffects(uid, thirst);
}

// Verify: ThirstSystem.cs — grep NextUpdateTime +=
```

Comment: the expensive threshold/alert body only runs once per interval per entity; entities created at different moments tick out of phase, so the load is spread across ticks without extra bookkeeping.
