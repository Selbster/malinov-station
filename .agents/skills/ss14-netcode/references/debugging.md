# Debugging SS14 network problems

Companion reference for the ss14-netcode skill. All CVar names verified
against `Robust.Shared/CVars.cs`; re-check after upstream syncs.

## Network condition simulation

Set in the client or server console (CHEAT cvars):

```
net.fakeloss 0.1        // 10% packet loss
net.fakelagmin 0.1      // minimum 100ms latency
net.fakelagrand 0.05    // + random 0-50ms jitter
net.fakeduplicates 0.05 // 5% duplicated packets
```

Use these before shipping any netcode feature: code that only works on a
clean loopback is not finished.

## Prediction toggle

```
net.predict false  // disable client prediction - exposes raw server RTT
net.predict true   // re-enable
```

Related tuning: `net.predict_tick_bias`, `net.predict_lag_bias`
(client-side, ARCHIVE cvars).

## Desync triage

1. If clients see stale values: check for a missing `Dirty()`/`DirtyField()`
   after the server-side mutation first - it is the most common cause.
2. If an entity is invisible but interactable client-side: it likely left PVS
   (`MetaDataFlags.Detached`, parked in null-space) or was never sent due to
   visibility masks/budgets - see the ss14-pvs skill.
3. On metadata errors the client requests recovery via `MsgStateRequestFull`;
   the server answers with a full state (`FromSequence = 0`). Frequent full
   requests indicate systematic state corruption, not random lag.
4. `net.pvs false` dumps everything once, then switches to delta-only - do not
   treat it as "send all every tick".

## Prometheus metrics

Exported by NetManager:

- `robust_net_sent_packets` / `robust_net_recv_packets`
- `robust_net_sent_bytes` / `robust_net_recv_bytes`
- `robust_net_resent_delay` / `robust_net_resent_hole`
- `robust_net_dropped`

Spiking resends usually mean payloads hover around the reliability threshold;
spiking drops point to client bandwidth limits or PVS burst budgets.

Verified against code state: 2026-08-22
