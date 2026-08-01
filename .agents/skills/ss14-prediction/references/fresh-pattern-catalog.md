# Fresh Pattern Catalog (Prediction)

Verified against the fork's current code on 2026-08-02. Re-check symbols before copying.

## Predicted random

```csharp
// Content.Shared/Random/Helpers/SharedRandomExtensions.cs
public static System.Random PredictedRandom(IGameTiming timing, NetEntity netEnt, NetEntity? netEnt2 = null);
public static bool PredictedProb(IGameTiming timing, float probability, NetEntity netEnt1, NetEntity? netEnt2 = null);
```

- Seed = `HashCodeCombine((int)timing.CurTick.Value, netEnt.Id, netEnt2?.Id ?? 0)` — identical on client and server because it uses `NetEntity`, not the local `EntityUid`.
- Note: the helper file carries `// TODO: REPLACE ALL OF THIS WITH PREDICTED RANDOM WHEN ENGINE PR IS MERGED` — check whether an engine-level predicted RNG landed upstream before building new code on it.
- `Prob(this System.Random random, double chance)` lives in `Robust.Shared/Random/IRobustRandom.cs` (extension).

## Predicted spawn family

`Robust.Client/GameObjects/ClientEntityManager.Spawn.cs` — all flag the spawned entity with `PredictedSpawnComponent` so `ResetPredictedEntities` can delete/re-create it:

- `PredictedSpawn(string? proto, ComponentRegistry? = null, bool doMapInit = true)`
- `PredictedSpawn(string? proto, MapCoordinates, ComponentRegistry? = null, Angle = default)`
- `PredictedSpawnAtPosition(string? proto, EntityCoordinates, ComponentRegistry? = null)`
- `PredictedTrySpawnNextTo(string? proto, EntityUid target, out EntityUid uid, ...)`
- `PredictedTrySpawnInContainer(string? proto, EntityUid container, string containerId, out EntityUid uid, ...)`
- `PredictedSpawnNextToOrDrop(...)` (in `ClientEntityManager`, shared contract on `IEntityManager`)

## Predicted audio / popups

- `SharedAudioSystem.PlayPredicted(SoundSpecifier?, EntityUid source, EntityUid? user, AudioParams?)`
- Client plays locally only when `IsFirstTimePredicted`; server plays to PVS but sets `ExcludedEntity = user`.
- Popups: `PopupPredicted(message, uid, recipient, type)` — same `IsFirstTimePredicted` semantics (`Content.Shared/Popups/SharedPopupSystem.cs`).

## Rollback mechanics (`ResetPredictedEntities`)

`ClientGameStateManager.ResetPredictedEntities()` runs in this order:

1. Delete all entities with `PredictedSpawnComponent`.
2. Restore dirty entities: components reset to the last server state via `ComponentHandleState` (auto-generated handler restores `[AutoNetworkedField]` fields).
3. Remove components added during prediction (`CreationTick > LastRealTick`).
4. Re-add components removed during prediction (with the last server state).
5. `PhysicsSystem.ResetContacts()`, then `ClientDirtySystem.Reset()`.

## Timing flags

`Robust.Client/Timing/ClientGameTiming.cs`:

- `InPrediction => !ApplyingState && CurTick > LastRealTick`
- `IsFirstTimePredicted` — true initially; set false by `StartPastPrediction`/`StartStateApplication`; restored by the `PredictionGuard` / `EndPastPrediction`. Net effect: true only on the final present tick of a run.
- `LastRealTick` / `LastProcessedTick` / `CurTick` / `ServerTime` — see `IGameTiming`.

## Prediction target formula

`ClientGameStateManager.cs` (roughly):

```csharp
predictionTarget = _timing.LastProcessedTick
    + TargetBufferSize
    + ceil(_timing.TickRate * (ping + PredictLagBias) / _timing.TimeScale)
    + PredictTickBias;
```

Where `ping` is the smoothed server ping in seconds and `TargetBufferSize` / `PredictTickBias` / `PredictLagBias` come from the CVars below.

## Networked state recipe

- `[AutoGenerateComponentState(raiseAfterAutoHandleState: ...)]` + parameterless `[AutoNetworkedField]` on each replicated field.
- `[AutoNetworkedField]` has **no configuration** — there is no `[NetSync]` attribute.
- Delivery-direction controls that do exist: `Component.NetSyncEnabled` (component-level), `SendOnlyToOwner`, `SessionSpecific` (on networked state).

## CVars

| CVar | Default | Notes |
|---|---|---|
| `net.predict` | true | master switch for client prediction (CLIENTONLY, ARCHIVE) |
| `net.predict_tick_bias` | 1 | extra ticks to run ahead |
| `net.predict_lag_bias` | 0.016 (Win) / 0 (Linux) | compensates Windows time-period lag |
| `net.fakelagmin` | 0 | extra latency in seconds (CHEAT) |
| `net.fakelagrand` | 0 | random extra latency up to this (CHEAT) |
| `net.fakeloss` | 0 | packet loss fraction (CHEAT) |
