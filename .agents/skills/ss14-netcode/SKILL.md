---
name: ss14-netcode
description: Architecture guide for SS14 networking - Lidgren transport, NetManager abstraction, typed messages, game state deltas, network events, EntityUid/NetEntity conversion, and component replication basics. Use it when writing or debugging any code that crosses the network boundary (RaiseNetworkEvent, AutoNetworkedField, Dirty/DirtyField, NetEntity conversions), before reaching for specialized ss14-pvs, ss14-prediction, or ss14-events skills.
---

# SS14 network architecture

## Mental model

The server is authoritative. It simulates the world, filters visibility through
PVS, composes one delta `GameState` per player per tick, and streams it down.
Clients apply states, predict local inputs ahead of server acks, and send
commands/events up. Only three things cross the process boundary: component
states (`AutoNetworkedField` + `Dirty()`), network events (`RaiseNetworkEvent`),
and session/console messages.

Stack, top-down: content systems -> game state managers + PVS -> typed
`NetMessage`s in delivery groups -> `NetManager` (`INetManager` wrapper) ->
Lidgren UDP with its reliability layer.

**The single most important pattern:** after mutating a networked component on
the server you MUST call `Dirty()` (or `DirtyField()`), otherwise the change is
server-only and clients silently desync:

```csharp
comp.Value = newValue;
Dirty(uid, comp); // no Dirty() = change never reaches clients
// Verify: Robust.Shared/GameObjects - grep "public void Dirty("
```

## What to read first

1. This file end-to-end once; afterwards jump by task.
2. `[NetworkedComponent]` / `[AutoNetworkedField]` deep dive: **ss14-ecs-components**.
3. Prediction loop and timing flags: **ss14-prediction**.
4. PVS overrides, budgets, leave mechanics: **ss14-pvs**.
5. Event taxonomy and subscriptions: **ss14-events**; hot-path discipline: **ss14-standard-optimizations**.
6. Network debugging recipes: `references/debugging.md`.

## Source of truth

1. Ground truth is the fork codebase: transport in RobustToolbox `Robust.Shared/Network/`, state streaming in `Robust.Server/GameStates/` + `Robust.Client/GameStates/`.
2. CVar names/defaults drift between upstream syncs - re-check `Robust.Shared/CVars.cs`.
3. Anything not re-verified against current code is marked `[unverified]`.

## Patterns

1. Validate every client->server event on the server (component presence + interaction range) from shared helpers so client pre-checks reuse the code.
2. Call `Dirty()` after mutating any `AutoNetworkedField`; switch to `DirtyField()` for point updates on heavy components (`fieldDeltas: true`).
3. Convert IDs only at the boundary: store `EntityUid`, wrap via `GetNetEntity()` when serializing, unwrap via `GetEntity()` when consuming.
4. Respect delivery-group defaults instead of hand-picking transports: unreliable for per-tick data, reliable ordered for chat/ECS events.
5. Mark owner-private components with `SendOnlyToOwner => true` and per-session divergent data with `SessionSpecific => true`; never filter sends manually.
6. Use PVS overrides only where visibility must exceed range limits - see ss14-pvs.
7. Test against hostile networks early with `net.fake*` CVars.

## Anti-patterns

1. Trusting client-sent event payloads without validation - any client can spoof any message and target entity.
2. Mutating an `AutoNetworkedField` without `Dirty()` - server changes, clients never learn about it.
3. Full `Dirty()` every tick when one field changed on a field-heavy component - use `DirtyField()` instead.
4. Storing `NetEntity` in component fields or sending raw `EntityUid` in event payloads without conversion.
5. Putting `[NetworkedComponent]` on Client/Server project components - it silently does nothing there.
6. Assuming ordering or arrival for unreliable delivery - handlers must tolerate drops and reorderings.
7. Sizing packets against configured `net.mtu` alone - the MsgState reliability threshold is hardcoded lower (488 bytes, see below).

## Lidgren transport layer

Game code never touches Lidgren's `NetPeer` directly; everything goes through the `INetManager` abstraction. Configuration flows from CVars:

- `net.mtu` (default 700) feeds Lidgren's `MaximumTransmissionUnit`; Lidgren's own constant `kDefaultMTU` is 508. Oversized packets are fragmented.
- Fake-network testing: `net.fakeloss`, `net.fakelagmin`, `net.fakelagrand`, `net.fakeduplicates` (CHEAT cvars).
- Buffers: `net.sendbuffersize`, `net.receivebuffersize`; protocol id: `CVars.NetLidgrenAppIdentifier`.

At startup the engine pins Lidgren's clock to engine time:

```csharp
NetTime.SetNow(_timing.RealTime.TotalSeconds);
// Verify: Robust.Shared/Network/NetManager.cs - grep NetTime.SetNow
```

## NetMessage: typed messages

Every wire message derives from `NetMessage`; the group sets the default delivery method (verify: `Robust.Shared/Network/NetMessage.cs` - grep `DeliveryMethod`):

| Group | Delivery | Used for |
|-------|----------|----------|
| `Core` | ReliableUnordered | connections, disconnections, ticks |
| `Entity` | Unreliable | entity/state synchronization |
| `String` | ReliableOrdered | chat, text messages |
| `Command` | ReliableUnordered | commands client -> server |
| `EntityEvent` | ReliableOrdered | ECS events between peers |

Ordering applies only within a sequence channel (up to 32; channels 16+ are reserved for engine use). Message type names travel as numeric IDs synced via `StringTable` during connection setup. Registration happens once per side:

```csharp
_networkManager.RegisterNetMessage<MsgStateAck>(HandleStateAck); // with handler
_networkManager.RegisterNetMessage<MsgState>();                  // send-only
// Verify: Robust.Shared/Network/NetManager.cs - grep RegisterNetMessage
```

## NetManager

One class implements both `IClientNetManager` and `IServerNetManager`. `ProcessPackets()` runs every frame: drains the Lidgren queue, dispatches `Data` messages to handlers, reacts to `StatusChanged`, logs warnings, updates Prometheus metrics, recycles buffers. Each connection surfaces as an `INetChannel`: Lidgren connection + `NetUserId` + ping + auth status.

## Game state synchronization

A `GameState` is a delta between two ticks: changed component states, player sessions, entity deletions, the `FromSequence`/`ToSequence` range, and `LastProcessedInput`. `FromSequence == 0` means a full state - sent after connecting or on desync recovery requested via `MsgStateRequestFull`.

`MsgState` packing rules:

- ZStd compression kicks in above 256 bytes of serialized payload.
- Reliability switches on size: payloads over the hardcoded `ReliableThreshold` (= Lidgren `kDefaultMTU - 20` = 488 bytes) go Reliable, smaller go Unreliable. NOT derived from the configured `net.mtu`.
- `ForceSendReliably` marks states the client must not lose.

```csharp
public const int CompressionThreshold = 256;
public const int ReliableThreshold = NetPeerConfiguration.kDefaultMTU - 20;
// Verify: Robust.Shared/Network/Messages/MsgState.cs - grep Threshold
```

Flow, server -> client: `ServerGameStateManager.SendGameStateUpdate()` -> PVS builds one per-player `GameState` -> serialize/compress -> send as `MsgState` -> client buffers it in `GameStateProcessor` (buffer size trades latency against smoothness; local tick rate nudges to match the server) -> client answers with `MsgStateAck`.

## Network events

Local and networked events are different mechanisms; taxonomy lives in the ss14-events skill. Event payloads must be plain serializable data - convert entities to `NetEntity` before sending.

```csharp
RaiseNetworkEvent(new MyNetEvent());      // crosses the network
SubscribeNetworkEvent<MyNetEvent>(OnNetEvent);
// Verify: Robust.Shared/GameObjects/EntitySystem.Subscriptions.cs - grep SubscribeNetworkEvent
```

**Validate everything a client sends.** Any client can spoof any message:

```csharp
// BAD - a spoofed TargetEntity triggers an arbitrary server-side action
private void OnClientEvent(MyEvent ev, EntitySessionEventArgs args)
{
    DoAction(ev.TargetEntity);
}

// GOOD - validate on the server; mirror checks client-side from shared code
private void OnClientEvent(MyEvent ev, EntitySessionEventArgs args)
{
    if (!HasComp<MyComponent>(ev.TargetEntity))
        return;
    if (!_interaction.InRangeUnobstructed(args.SenderSession, ev.TargetEntity))
        return;
    DoAction(ev.TargetEntity);
}
// Verify: Content.Shared/Interaction/SharedInteractionSystem.cs - grep InRangeUnobstructed
```

Sending patterns (real overloads):

```csharp
RaiseNetworkEvent(new MyEvent());                    // server -> all clients
RaiseNetworkEvent(new MyEvent(), session);           // server -> one player
var filter = Filter.Broadcast().RemovePlayerByAttachedEntity(uid);
RaiseNetworkEvent(new MyEvent(), filter);            // everyone except one
// Verify: Robust.Shared/GameObjects/EntitySystem.cs - grep RaiseNetworkEvent
```

## EntityUid vs NetEntity

| | EntityUid | NetEntity |
|---|---|---|
| Where used | locally, at runtime | only on the wire |
| Stability | differs between client and server | identical everywhere |
| Storage | in components and logic | transmission only |

```csharp
var netEntity = GetNetEntity(uid); // serialize out
var uid = GetEntity(netEntity);    // deserialize in
// Verify: Robust.Shared/GameObjects/IEntityManager.Network.cs - grep GetNetEntity
```

Edge cases: deleted/untracked entities convert to `NetEntity.Invalid` - guard with `!= NetEntity.Invalid` before converting back. An entity that left PVS still exists client-side with `MetaDataFlags.Detached`, parked in null-space; on re-entry it revives - revive what exists instead of spawning duplicates. With `[AutoNetworkedField]`, `EntityUid` values and collections convert automatically; manual conversion is for custom serialization paths only.

## Component networking

Automatic replication is the default choice:

```csharp
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class MyComponent : Component
{
    [DataField, AutoNetworkedField]
    public float Value = 1f;
}
// Verify: Robust.Shared/Analyzers/ComponentNetworkGeneratorAuxiliary.cs - grep AutoNetworkedFieldAttribute
```

To react to applied incoming states without re-implementing replication, set `[AutoGenerateComponentState(raiseAfterAutoHandleState: true)]` and handle the ByRef `AfterAutoHandleStateEvent` component event. Owner-private and per-session data are COMPONENT-level property overrides, not field attributes:

```csharp
// ALL AutoNetworkedFields here reach only the owning player's client
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class AlertsComponent : Component
{
    public override bool SendOnlyToOwner => true;

    [AutoNetworkedField] public int AlertCount;
}
// Verify: Content.Shared/Alert/AlertsComponent.cs - grep SendOnlyToOwner
```

Non-triggering condition: do NOT use it when other clients need the data for prediction or rendering - owner-only delivery starves their predictors.

```csharp
// each client receives values scoped to itself (disguise/hidden-role visibility)
public override bool SessionSpecific => true;
// Verify: Content.Shared/Revolutionary/Components/RevolutionaryComponent.cs - grep SessionSpecific
```

`[NetworkedComponent]` belongs ONLY on Shared-project components; on Client or Server projects it silently does nothing - no compile error, no sync.

Reference-type fields: the generator special-cases `List<T>` and `Dictionary<K,V>` out of the box (including automatic `EntityUid` conversion inside collections). Custom reference types MUST implement the generic `IRobustCloneable<T>`, otherwise prediction restores them by reference and mutations leak across predicted states:

```csharp
public sealed partial class MySettings : IRobustCloneable<MySettings>
{
    public string Mode = string.Empty;

    public MySettings Clone() => new() { Mode = Mode };
}
// Verify: Robust.Shared.Serialization - grep IRobustCloneable + GlobalIRobustCloneableName in CompNetworkGenerator
```

## Point updates with DirtyField

For components built with `fieldDeltas: true`, mark individual field changes instead of dirtying the whole component:

```csharp
[RegisterComponent, NetworkedComponent]
[AutoGenerateComponentState(fieldDeltas: true)]
public sealed partial class ProximityDetectorComponent : Component
{
    [AutoNetworkedField] public TimeSpan NextUpdate = TimeSpan.Zero;
    [AutoNetworkedField] public float Distance = float.PositiveInfinity;
    [AutoNetworkedField] public EntityUid? Target;
    [DataField] public TimeSpan UpdateCooldown = TimeSpan.FromSeconds(1);
}

private void Tick(EntityUid uid, ProximityDetectorComponent comp)
{
    comp.NextUpdate += comp.UpdateCooldown;
    DirtyField(uid, comp, nameof(ProximityDetectorComponent.NextUpdate));
    // delta carries only NextUpdate; Distance/Target stay untouched
}
// Verify: Robust.Shared/GameObjects/EntityManager.ComponentDeltas.cs - grep DirtyField
```

Use `DirtyField` when specific fields change, the component carries many networked fields, and changes are frequent. Reserve full `Dirty()` for when most of the state actually changes together (see Anti-pattern 3).

## Debugging network problems

Quick levers: `net.predict false` shows raw latency by disabling prediction; the `net.fake*` CVars inject loss/lag/duplicates. Full recipes - Prometheus metrics, buffer tuning, desync triage - live in `references/debugging.md`.

## Extension rule

Add new transport/message-level facts next to the matching section and give every new code example a `// Verify:` marker. If a topic grows past ~40 extra lines, split it into its own skill or `references/` page and link it here. Never re-explain PVS internals or event taxonomy here - extend the dedicated skills instead.

Verified against code state: 2026-08-22
