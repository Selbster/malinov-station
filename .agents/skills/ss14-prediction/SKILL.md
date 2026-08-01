---
name: ss14-prediction
description: Client-side prediction in Space Station 14 — prediction loop, timing flags (InPrediction / IsFirstTimePredicted / ApplyingState), rollback via ResetPredictedEntities, predicted spawn/audio/popups, and deterministic predicted random. Use it to debug mispredictions, add predicted gameplay, or design client-side side effects.
---

# Client-Side Prediction in SS14

## What to read first

1. `references/fresh-pattern-catalog.md` — confirmed, verified API recipes.
2. `references/rejected-snippets.md` — legacy and unsafe zones that must not be copied.

## Source of truth

1. The fork codebase is the ground truth: `Robust.Client/GameStates/ClientGameStateManager.cs`, `Robust.Client/GameStates/ClientDirtySystem.cs`, `Robust.Client/Timing/ClientGameTiming.cs`.
2. Everything not re-verified against current code is marked `[unverified]` — check it before reuse.
3. CVar names, attributes and method signatures drift between upstream syncs; re-verify them by name.

## Prediction mental model

The client runs two sides of the same simulation: it applies the confirmed server state, then replays local input ahead of it. The whole replay is rolled back and redone whenever a new server state arrives.

```
ClientGameStateManager.ApplyGameState()
  1. ResetPredictedEntities()    // roll back all predicted changes to the last server state
  2. ApplyGameState(cur, next)   // apply new server state, create/delete/detach entities
  3. MergeImplicitData()         // fake initial states for new entities (from prototypes)
  4. PredictTicks(target)        // replay ticks, apply pending input and events
  5. TickUpdate()                // final tick = predictionTarget, runs exactly once
```

Timing flags (all from `IGameTiming` / `IClientGameTiming`):

- `InPrediction` = `!ApplyingState && CurTick > LastRealTick` — inside the replay window.
- `ApplyingState` — a server state is being applied; never produce side effects here.
- `IsFirstTimePredicted` — true **only on the final present tick** of a run, false inside the past-prediction replay. This is the correct gate for one-shot side effects.
- `LastRealTick` — last tick confirmed by the server; `LastProcessedTick` — last tick a state was applied for.

The single most important pattern: **every side effect (sound, popup, visual, spawn) must fire exactly once — gate it with `IsFirstTimePredicted` or use a `*Predicted` method.**

## Patterns

1. Gate every side effect with `IsFirstTimePredicted` or a `*Predicted` method (`PlayPredicted`, `PopupPredicted`).
2. Call `Dirty(uid, comp)` after every change to a networked component field, otherwise the change never leaves the client.
3. Keep predicted logic and components in **Content.Shared** — client and server must derive the same result from the same input.
4. Use the `EntityManager.PredictedSpawn*` family for client-spawned entities; the engine reconciles them on rollback via `PredictedSpawnComponent`.
5. Use deterministic random — `SharedRandomExtensions.PredictedRandom(_timing, GetNetEntity(uid))` — seeded from `NetEntity`, identical on client and server.
6. Model "must be removed" states as a component change (state-as-component) instead of deleting a server entity.
7. Keep `[AutoNetworkedField]` values small and plain; big reference types are serialized into every delta.

## Anti-patterns

1. Calling `PlayPvs` / `PopupEntity` during prediction without a gate — the effect repeats on every re-prediction.
2. `QueueDel(serverEntity)` while `InPrediction` — the engine only logs `Log.Error` ("Predicting the deletion of a networked entity"); the state is left inconsistent.
3. Seeding `new System.Random((int)uid.Id + ...)` — the local `EntityUid` differs between client and server, so seeds diverge → misprediction. Seed from `NetEntity` instead.
4. Using `IRobustRandom` for gameplay during prediction — the client's RNG is independent and not re-seeded per tick, so results differ between replays.
5. Mutating non-networked fields during prediction — `ResetPredictedEntities` never rolls them back.
6. `[NetworkedComponent]` in `Content.Client` — silently ineffective; it must live in `Content.Shared`.
7. Forgetting `Dirty()` — the change is dropped and the prediction diverges from the server.

## Code examples

### 1) Sound that plays exactly once, only for the local player

```csharp
// Client: plays locally only when IsFirstTimePredicted.
// Server: plays to PVS but excludes `user` via ExcludedEntity (no double sound).
_audio.PlayPredicted(sound, sourceUid, user);
// Verify signature: SharedAudioSystem.PlayPredicted(SoundSpecifier?, EntityUid source, EntityUid? user, AudioParams?).
```

### 2) Deterministic predicted random

```csharp
// Same seed on client and server: CurTick + NetEntity ids.
var rand = SharedRandomExtensions.PredictedRandom(_timing, GetNetEntity(ent));
if (rand.Prob(0.5f))
    DoAction();
// Verify: Content.Shared/Random/Helpers/SharedRandomExtensions.cs (PredictedRandom, PredictedProb).
```

### 3) Predicted spawn with automatic reconciliation

```csharp
var ent = EntityManager.PredictedSpawn(protoId, mapCoords);
// ResetPredictedEntities deletes PredictedSpawnComponent entities, then re-creates them from the server state.
// Verify family: PredictedSpawn, PredictedSpawnAtPosition, PredictedTrySpawnNextTo, PredictedTrySpawnInContainer.
```

## Dimension checklist

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating (`InPrediction` / `IsPredictionEnabled`) | ☑ | `InPrediction` for rollback-sensitive code; `IsFirstTimePredicted` for one-shot effects |
| Server / Client / Shared split + `[NetworkedComponent]` | ☑ | predicted logic in Shared; `[NetworkedComponent]` in Shared only |
| Event and `UpdatesBefore` / `UpdatesAfter` ordering | ◑ | keep the replay identical to the server: order systems relative to InputSystem; verify each new system |
| Component lifecycle (Add/Remove/Initialize/Shutdown) | ◑ | rollback uses `CreationTick` vs `LastRealTick`; deleted components are re-added with the server state |
| Hot path and allocations | ◑ | re-prediction multiplies per-tick cost; avoid heavy queries/allocations in predicted systems |
| PVS / network visibility of client-side logic | ◑ | predicted entities detach when leaving PVS (`net.pvs_exit_budget`) — prediction stops until re-entry |

## Prediction testing

```
net.predict false        // disable prediction -> see real latency and snap-backs
net.predict true         // enable again
net.fakelagmin 0.2       // +200ms minimum latency (CVar.CHEAT — dev only)
net.fakelagrand 0.05     // +0–50ms random delay (CVar.CHEAT)
net.fakeloss 0.05        // 5% packet loss (CVar.CHEAT)
```

Tuning: `net.predict_tick_bias` (default 1) and `net.predict_lag_bias` (0.016 s on Windows, 0 on Linux) shift how far the client runs ahead. Test with prediction on, off, under fake lag, and with rapid repeated actions.

## Extension rule

1. Extend via Shared systems + `[AutoNetworkedField]` state that stays rollback-friendly: no side effects, no non-networked mutations, no `IRobustRandom`.
2. Before shipping a predicted mechanic, check: deterministic random? one-shot side effect gating? `Dirty()` on every change? clear entity lifetime (`PredictedSpawn*` or state component)?
3. If a section outgrows this file, move it to `references/` and update the reading order at the top.

Verified against code state: 2026-08-02.
