# Fresh Pattern Catalog (VirtualController Core)

> Status `Limited` means architectural contract: useful for understanding, but the code has not been re-verified against the current fork (older than the last upstream sync point) and is not copied as a template.
>
> `Date by blame` is the last commit that touched the anchor line (`git blame -L`). For usage-case rows the anchor is the caller site; for method rows it is the method definition. Re-verify the date before relying on the freshness status.
>
> This catalog owns architecture anchors (engine contract, substep pipeline, layer split). The full method/usage catalog for gameplay code lives in `ss14-virtual-controller-api/references/fresh-pattern-catalog.md`.

| Class/method | Pattern | Why is it useful | Layer | Date by blame | Status |
|---|---|---|---|---|---|
| `VirtualController.Initialize` | Subscription to `PhysicsUpdateBeforeSolveEvent`/`PhysicsUpdateAfterSolveEvent` through a single boilerplate | Basic runtime contract for all virtual controllers | Engine | 2022-01-16 | Limited |
| `VirtualController.UpdateBeforeSolve/UpdateAfterSolve` | Two symmetrical pre/post-solver hooks | Explicit controller extension point | Engine | 2021-03-08 | Limited |
| `SharedPhysicsSystem.SimulateWorld` | Raising before/after events within each substep | Fixes the actual call frequency of virtual controllers | Engine | 2022-12-25 | Limited |
| `SharedPhysicsSystem.SimulateWorld` | `FindNewContacts` + `Step` in substep loop | Shows the place of controllers in the contact/solver pipeline | Engine | 2025-05-28 | Use |
| `SharedMoverController.Initialize` | `UpdatesBefore.Add(typeof(TileFrictionController))` to `base.Initialize()` | Stable order mover vs friction | Shared | 2025-05-28 | Use |
| `TileFrictionController.UpdateBeforeSolve` | Manual damping branch for `BodyType.KinematicController` | Correct attenuation for controller bodies | Shared | 2025-05-02 | Use |
| `SharedConveyorController.UpdateBeforeSolve` | Parallel compute (`_parallel.ProcessNow`) + combine with `wishDir` | Productive conveyor without desync | Shared | 2025-03-28 | Use |
| `PullController.UpdateBeforeSolve` | Pulse in pullable + reverse pulse puller in weightless/blocked | Physically stable pull in difficult conditions | Server | 2024-05-27 | Use |
| `MoverController (Client).OnUpdate*Predicted` | `UpdateIsPredictedEvent` for mover/relay target/pullable | Reduces mispredict in local management | Client | 2024-09-12 | Use |
| `ChasingWalkSystem` | Setting speed + `SetBodyStatus(..., BodyStatus.InAir)` for special entities | Supports desired pursuit mechanics | Server | 2024-03-25 | Use |
| `ChaoticJumpSystem.Jump` | Raycast-target selection and teleport safe-offset before `SetWorldPosition` | Reduces the chance of teleporting into a collision | Server | 2023-12-28 | Use |
