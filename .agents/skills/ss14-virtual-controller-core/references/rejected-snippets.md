# Rejected Snippets (VirtualController Core)

> This catalog owns engine/architecture caveats. API-misuse zones (relay component surgery, `BodyStatus.InAir` overuse, empty client `ConveyorController`, eject-through-climb flow, NPC climb flow, `PullController`/`RandomWalkController` caveats) live in `ss14-virtual-controller-api/references/rejected-snippets.md`.

| Zone | What's found | Why not take it as a standard | Signal |
|---|---|---|---|
| `SharedPhysicsSystem.Solver.GetInvMass` | Special branch for `KinematicController` with a note about fragility | The internals are useful for understanding, but not as a template for new gameplay code | Comment `shitcodey` + TODO about improvement |
| `SharedPhysicsSystem.Contacts.UpdateContact` | Temporary caveats about refactor and unstable contact deletion scenarios | Do not copy as a ready-made architectural pattern | Explicit TODO/temporary-markers |
| `ClimbSystem` (pin tail of stop logic) | Sites with TODO about engine cleanup and obsolete necessity | Do not use as baseline for new contact completion controller | TODO `Remove this on engine` / `Is this needed` |
| Docs: `physics` page | There is a TODO marker for fixture/bodytype events | The documentation does not cover all the details of the current implementation | HTML TODO marker in document |
