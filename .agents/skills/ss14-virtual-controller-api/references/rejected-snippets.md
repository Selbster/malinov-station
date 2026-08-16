# Rejected Snippets (VirtualController API)

> This catalog owns API-misuse zones. Engine/architecture caveats (`GetInvMass`, `UpdateContact`, `ClimbSystem` stop logic, docs `physics` page) live in `ss14-virtual-controller-core/references/rejected-snippets.md`.

| Zone | What's found | Why not take it as a standard | Signal |
|---|---|---|---|
| `PullController` | Internal comments about "slop" and the need for a different approach | Implementation works, but contains recognized technical debt | TODO + warning comments |
| `RandomWalkController` | Ambiguity on semantics `prediction` in the method documentation | Cannot be used as a reference under the prediction contract | TODO `Document this` |
| Manual field surgery on `RelayInputMoverComponent` or `MovementRelayTargetComponent` | Bypass `SetRelay`/`ComponentShutdown` cleanup | Risk of leaving inconsistent relay-target states | Manual component surgery |
| Empty client `ConveyorController` | Only network/prediction presence without business logic | Cannot be used as an API reference for conveyor behavior | Base date 2023-02-13 (not re-verified against the current fork) |
| `ExitContainerOnMoveSystem` | Old container-exit flow via climb | Useful historically, but not as a fresh reference | Date 2024-01-15 (not re-verified against the current fork) |
| NPC obstacle climbing block | Set of TODO and workaround conditions around obstacles | Low tolerance, high risk of regressions | TODO-heavy comments |
| Excessive use of `BodyStatus.InAir` | Often looks like a quick-fix outside of special mechanics | May mask a real problem in physics/contacts | Semantic misuse |
