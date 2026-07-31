---
name: ss14-events
description: A guide to using events in Space Station 14 - strict taxonomy, subscriptions, by-ref event prioritization, and networking patterns.
---

# 📨 SS14 Events Guide

Events are the primary way of communication between systems and entities in Space Station 14. 🚀 This guide covers how to properly define, raise, and handle events while following engine standards.

## 📝 Event Definition

### Local Events 🏠
For local events (within a single client or server), use a simple `struct` or `class` structure.
* **Structs**: Preferred for high frequency events (e.g. `MoveEvent`, damage-related attempt events) to avoid GC load. 🏎️
* **Classes**: Use for complex data or events that require inheritance (e.g. `ExaminedEvent`). 📚
* **Naming**: The `Event` suffix is ​​required (for example, `DoorOpenedEvent`).

```csharp
// Simple event structure (illustrative example — verify the event name exists before referencing it in guides)
public readonly record struct DoorOpenedEvent(EntityUid User);

// Event class with output data (actual implementation)
public sealed class ExaminedEvent : EntityEventArgs {
    public FormattedMessage Message { get; }
    public EntityUid Examined { get; }
    public EntityUid Examiner { get; }

    public ExaminedEvent(FormattedMessage message, EntityUid examined, EntityUid examiner, bool isInDetailsRange, bool hasDescription) { ... }
}
```

### Network Events 🌐
Events transmitted over the network **MUST** inherit `EntityEventArgs` and be marked with `[Serializable, NetSerializable]` attributes.

**Note:** Net-identity is transmitted via `NetEntity` (not raw `EntityUid`), which survives serialization across the wire.

```csharp
// Content.Shared/CrewManifest/SharedCrewManifestSystem.cs
[Serializable, NetSerializable]
public sealed class RequestCrewManifestMessage : EntityEventArgs
{
    public NetEntity Id { get; }

    public RequestCrewManifestMessage(NetEntity id)
    {
        Id = id;
    }
}
```

## 🔗 Subscribe to Events

Subscriptions are always processed in `EntitySystem.Initialize()`.

### 1. Directed Subscription (`SubscribeLocalEvent`) 🎯
Use when you want to listen to an event *on a specific entity* that has a specific component.

**Modern format:** Use the `Entity<T>` wrapper to access the component and UID at the same time.

```csharp
public override void Initialize() {
    base.Initialize();
    SubscribeLocalEvent<DoorComponent, DoorOpenedEvent>(OnDoorOpened);
}

private void OnDoorOpened(Entity<DoorComponent> ent, ref DoorOpenedEvent args) {
    // ent.Owner is the EntityUid
    // ent.Comp is a DoorComponent
    if (ent.Comp.IsOpen) ...
}
```

### 2. Broadcast Subscription (`SubscribeLocalEvent<T>`) 📢
Use for global events that are not tied to a specific entity.

```csharp
SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
```

### 3. Network Subscription (`SubscribeNetworkEvent`) 📡
Use to process events sent from the other side (Client -> Server or Server -> Client).

```csharp
SubscribeNetworkEvent<RequestCrewManifestMessage>(OnRequestCrewManifest);
```

## 🧩 Specific Patterns

### 1. Cancellable Events 🚫
Used to check whether an action can be performed ("Attempt" events). Any subscriber can cancel the action.

* **Classes**: Inherit from `CancellableEntityEventArgs` (e.g. `BeforeDoorOpenedEvent` in `Content.Shared/Doors/DoorEvents.cs`).
* **Structures**: Add the `public bool Cancelled;` field (e.g. `DisarmAttemptEvent` in `Content.Shared/Actions/Events/DisarmAttemptEvent.cs`).
* **Important**: Always pass such events through `ref` so that changes to `Cancelled` are visible to the calling code.

**Usage (struct variant — actual implementation):**
```csharp
// Definition — Content.Shared/Actions/Events/DisarmAttemptEvent.cs
[ByRefEvent]
public record struct DisarmAttemptEvent
{
    public readonly EntityUid TargetUid;
    public readonly EntityUid DisarmerUid;
    public readonly EntityUid? TargetItemInHandUid;

    public bool Cancelled;

    public DisarmAttemptEvent(EntityUid targetUid, EntityUid disarmerUid, EntityUid? targetItemInHandUid = null) { ... }
}
```

```csharp
// Class variant — Content.Shared/Doors/DoorEvents.cs
public sealed class BeforeDoorOpenedEvent : CancellableEntityEventArgs
{
    public EntityUid? User = null;
}
```

```csharp
// Subscription (Lock action) — struct variant uses `args.Cancelled = true`
private void OnDisarmAttempt(Entity<DisarmRestrictionComponent> ent, ref DisarmAttemptEvent args) {
    if (!ent.Comp.CanBeDisarmed)
        args.Cancelled = true; // For CancellableEntityEventArgs classes: args.Cancel();
}
```

```csharp
// Call (Permission check) — struct must be passed by ref
var attempt = new DisarmAttemptEvent(target, user, inTargetHand);
RaiseLocalEvent(target, ref attempt);

if (attempt.Cancelled)
    return; // Action interrupted
```

### 2. Handled Events ✅
Used when an event must be processed by only one system (for example, interaction with an object). If one system has "handled" an event, the others do not need to execute their logic.

* **Implementation**: Add field `public bool Handled;` to a struct (e.g. `CanDropDraggedEvent` in `Content.Shared/DragDrop/DraggableEvents.cs`) or inherit `HandledEntityEventArgs` for classes (e.g. `InteractEvent` in `Content.Shared/Interaction/AfterInteract.cs`).

**Usage (struct variant — actual implementation):**
```csharp
// Definition — Content.Shared/DragDrop/DraggableEvents.cs
[ByRefEvent]
public record struct CanDropDraggedEvent(EntityUid User, EntityUid Target)
{
    public bool Handled = false;
    public bool CanDrop = false;
}
```

```csharp
// Subscription
private void OnDropDragged(Entity<MyComponent> ent, ref CanDropDraggedEvent args) {
    if (args.Handled) return; // Already processed by someone

    // Executing the logic
    args.Handled = true; // Mark as processed
}
```
**Important**: The pattern of `Handled` is different from `Cancelled`. `Cancelled` asks for permission (“Is it possible?”), and `Handled` speaks of the fact of accomplishment (“I did it!”).

## ⚡ Performance: By-Ref Events

For high-load code, especially frequently triggered events (physics, motion), use **By-Ref** (reference) events. This avoids copying large structures.

### Definition of By-Ref Events
Mark the structure with the `[ByRefEvent]` attribute. Real-world example — engine `MoveEvent` in `RobustToolbox/Robust.Shared/GameObjects/Components/Transform/TransformComponent.cs`:

```csharp
[ByRefEvent]
public readonly struct MoveEvent(
    Entity<TransformComponent, MetaDataComponent> entity,
    EntityCoordinates oldPos,
    EntityCoordinates newPos,
    Angle oldRotation,
    Angle newRotation)
{
    public readonly EntityCoordinates OldPosition = oldPos;
    public readonly EntityCoordinates NewPosition = newPos;
    // ...
}
```

### Subscription By-Ref
You **MUST** use the `ref` keyword in the handler signature. ⚠️

```csharp
SubscribeLocalEvent<PhysicsComponent, MoveEvent>(OnMove);
```

```csharp
private void OnMove(Entity<PhysicsComponent> ent, ref MoveEvent args) {
    // args is passed by reference, changes are visible everywhere
}
```

## 📤 Calling Events

### Calling Local Events
Use `RaiseLocalEvent` from `EntitySystem`.

```csharp
// By Value
RaiseLocalEvent(uid, new DoorOpenedEvent(user));
```

```csharp
// By reference (By Ref). NOTE: `[ByRefEvent]` only enables the `ref` passing —
// you still must write `ref` at the call site explicitly.
var moveEv = new MoveEvent(entity, oldPos, newPos, oldRot, newRot);
RaiseLocalEvent(uid, ref moveEv);
```

## ❌ Antipatterns and Frequent Errors

### 1. ⚠️ Legacy handler signature
**Legacy**: The expanded signature `(EntityUid uid, Component comp, args)`.
**Status**: Still valid and compiles — widely used in the codebase, especially for value-passed (non-`[ByRefEvent]`) events. Do not mix styles inside one system; default to `Entity<T>` for new code.
**Preferred (new code)**:
```csharp
// ✅ PREFERRED
private void OnEvent(Entity<MyComponent> ent, ref MyEvent args) { ... }
```

```csharp
// ⚠️ LEGACY (valid, avoid in new code)
private void OnEvent(EntityUid uid, MyComponent component, MyEvent args) { ... }
```

### 2. 🚫 Subscribe to `OnMapInit` or `Startup`
**Error**: Subscribe to events inside component lifecycle methods.
**Why**: This causes memory leaks and duplicate subscriptions.
**Correct**: Always subscribe only to `Initialize()` of your `EntitySystem`.

### 3. 🚫 Using `CancellableEntityEventArgs` for Structs
**Error**: Trying to inherit structures from classes or using `CancellableEntityEventArgs` unnecessarily.
**Why**: This creates unnecessary allocations (boxing).
**Correct**: Add the `bool Handled` or `bool Cancelled` field directly to the structure and pass it through `ref`.

### 4. 🚫 Heavy logic in event constructors
**Bug**: Perform complex calculations in event constructor.
**Why**: Events are created frequently.
**Correct**: Transfer only ready data.

### 5. 🚫 Forgotten `sealed` for event classes
**Error**: Creating an event class without `sealed`.
**Why**: Prevents the JIT compiler from devirtualizing calls, reducing performance.
**Correct**: Always write `public sealed class MyEvent`.

### 6. 🚫 Changing `ref` arguments unnecessarily
**Bug**: Change fields in `ref` event if you are not the "responsible" system.
**Why**: This may break the logic of other systems that receive the modified event.
**Correct**: Change the data only if your system needs to intercept or modify the result (for example, armor reduces damage).

## Performance addition: `ByRef record struct`

For frequent local events, prefer this format:

```csharp
[ByRefEvent] public record struct ChargedMachineActivatedEvent;

private void RaiseActivated(EntityUid uid)
{
    var ev = new ChargedMachineActivatedEvent();
    RaiseLocalEvent(uid, ref ev); // Important: ref is required.
}
```

### Why is this useful?

1. Less copying of events in mass flows.
2. More stable behavior in hot-path compared to heavy event classes.

### Anti-pattern

```csharp
// ❌ Frequent event as a class + call without by-ref:
public sealed class FrequentEvent : EntityEventArgs { }
RaiseLocalEvent(uid, new FrequentEvent());
```

Use classes where it is really needed due to semantics, and not out of habit.
