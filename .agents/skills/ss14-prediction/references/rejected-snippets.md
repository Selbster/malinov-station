# Rejected Snippets (Prediction)

| Zone | What's found | Why not take it as a standard | Signal |
|---|---|---|---|
| `[NetSync]` attribute | "use `[NetSync]` on `[AutoNetworkedField]` to control sync direction" | Does not exist anywhere in the codebase; `AutoNetworkedFieldAttribute` is parameterless. Direction is controlled by `NetSyncEnabled` / `SendOnlyToOwner` / `SessionSpecific` | Missing symbol |
| `new System.Random((int)(uid.Id + _timing.CurTick.Value))` | old "deterministic predicted random" recipe | Seeds with the local `EntityUid`, which differs between client and server → results diverge → misprediction. Canonical helper seeds from `NetEntity` (`PredictedRandom`) | Desync |
| `IRobustCloneable` requirement | "reference types in components must implement `IRobustCloneable`" | Vestigial from the hand-written `GetComponentState` era; `IRobustCloneable<T>` has no consumers. `[AutoNetworkedField]` reference fields are serialized automatically | Legacy API |
| `QueueDel(serverEntity)` during prediction | "deleting a server entity mid-prediction is supported" | `ClientDirtySystem.OnTerminate` only logs `Log.Error`; server-entity deletion is not supported and leaves state inconsistent. Model it as a component/state change | Log.Error |
| Unguarded `PlayPvs` / `PopupEntity` | "just call the sound/popup during prediction" | Replays on every re-prediction → duplicated effects. Gate with `IsFirstTimePredicted` or use `*Predicted` overloads | Duplicated side effects |
| `IRobustRandom` for gameplay rolls | "use the shared RNG during prediction" | Client RNG is independent and not re-seeded per tick; rolls differ between replays and from the server | Nondeterministic replay |
| Old CVar names (`net.maxupdaterange` etc.) | debugging instructions that use renamed CVars | Renamed in `Robust.Shared/CVars.cs`; stale names simply do nothing | Missing CVar (see the pvs skill catalog for correct names) |
