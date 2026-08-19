---
name: ss14-tests-authoring
description: Practical workflow for writing your own unit/integration tests in the Space Station 14 architecture: from choosing a strategy to stable assertions and test maintenance.
---

# Writing your own tests in SS14

## When to use

Use this skill when needed:
- design a new test script from scratch;
- select the test type (unit, server-only integration, server+client integration);
- design the test so that it is stable and does not break the pool;
- quickly review the test for anti-patterns before launching CI.

The internals of `PoolManager/TestPair` are discussed in detail in `ss14-tests-poolmanager`. Here the emphasis is on the author's workflow and the quality of tests as an artifact.

## Mental model

Tests borrow pooled server/client pairs via `PoolManager.GetServerClient()`.
Mutations go in `WaitPost(...)`, assertions go in `WaitAssertion(...)`.
Every pair must be returned via `CleanReturnAsync()` — `DisposeAsync()` is destructive.
Tick synchronization (`RunTicksSync` + `SyncTicks`) is mandatory before cross-side assertions.
The pool reuses pairs across tests for speed; minimize side effects so reuse stays safe.

## Short decision tree

1. Are you testing pure logic without network/tick/IoC?
- Yes: unit test.
- No: integration test.

2. Do you need real synchronization client<->server, UI/BUI, prediction or input?
- Yes: server+client integration.
- No: server-only integration may be simpler and faster.

3. Does the test change the global state, which does not survive reuse well?
- Yes: `Dirty = true` or `Pool = false` for isolation.
- No: leave reuse, it greatly speeds up the run.

## Basic workflow of the author

1. Fix the invariants:
- what needs to change;
- what should not change;
- on which side (server/client) each condition is checked.

2. Select minimal environment settings:
- `Connected`, if you need a live client;
- `DummyTicker = false`, if you need real round-flow;
- `InLobby = true`, if the lobby logic is checked;
- `Dirty = true`, only if the test really spoils the reuse state.

3. Arrange:
- create a map/entities/components in `WaitPost(...)`;
- if necessary, connect test prototypes (`[TestPrototypes]` or `ExtraPrototypes`).

4. Act:
- perform an action through the system/input/UI;
- after the action, give the necessary ticks for processing.

5. Assert:
- check in `WaitAssertion(...)` or outside the callback after synchronization;
- for server+client always take into account the tick delta.

6. Cleanup:
- always `CleanReturnAsync()` for pairs from the pool;
- if you raised separate server/client instances, disconnect/shutdown correctly.

## Pattern 1: standard content integration test

```csharp
// Verify: PoolManager.GetServerClient — grep in RobustToolbox IntegrationTestPair
[Test]
public async Task MyScenario_Works()
{
    await using var pair = await PoolManager.GetServerClient(new PoolSettings
    {
        Connected = true,
        Dirty = false,
    });

    var server = pair.Server;
    EntityUid subject = default;

    await server.WaitPost(() =>
    {
        // Arrange: create a test entity.
        subject = server.EntMan.SpawnEntity("MobHuman", MapCoordinates.Nullspace);
    });

    await pair.RunTicksSync(5);
    await pair.SyncTicks(targetDelta: 1);

    await server.WaitAssertion(() =>
    {
        // Assert: check after stabilization.
        Assert.That(server.EntMan.EntityExists(subject), Is.True);
    });

    await pair.CleanReturnAsync();
}
```

## Pattern 2: inline prototypes inside a test

```csharp
// Verify: TestPrototypesAttribute — grep in RobustToolbox
[TestPrototypes]
private const string Prototypes = @"
- type: entity
  id: MyTestTarget
  components:
  - type: Physics
";

[Test]
public async Task UsesInlinePrototype()
{
    await using var pair = await PoolManager.GetServerClient();

    await pair.Server.WaitAssertion(() =>
    {
        Assert.That(pair.Server.ProtoMan.HasIndex<EntityPrototype>("MyTestTarget"), Is.True);
    });

    await pair.CleanReturnAsync();
}
```

## Pattern 3: isolated special case without a pool

Use when the case involves global registrations/deep isolation.

```csharp
// Verify: StartServer/StartClient — grep in RobustToolbox IntegrationTest
var server = StartServer(new ServerIntegrationOptions
{
    Pool = false,
    ExtraPrototypes = Prototypes,
});
var client = StartClient(new ClientIntegrationOptions
{
    Pool = false,
    ExtraPrototypes = Prototypes,
});

await Task.WhenAll(server.WaitIdleAsync(), client.WaitIdleAsync());
Assert.DoesNotThrow(() => client.SetConnectTarget(server));
await client.WaitPost(() => client.ResolveDependency<IClientNetManager>().ClientConnect(null!, 0, null!));
```

## Good test patterns

- Each test checks 1 behavioral contract (not "all at once").
- Arrange/Act/Assert are visually separated.
- All mutations are wrapped in `WaitPost`, all checks are wrapped in `WaitAssertion`.
- There is an explicit synchronization of ticks before cross-side assertions.
- Complex scripts rely on helper methods (input, do-after, UI) instead of copy-paste.
- The test name describes the observed behavior (`X_WhenY_ShouldZ` or equivalent).

## Anti-patterns

- "Giant" test with dozens of independent goals.
- Magical `RunTicks(123)` without explanation why exactly so much.
- `Dirty = true` and `Pool = false` default for no reason.
- Assertions in `Post(...)` and mutations in `WaitAssertion(...)`.
- Reliance on documentation contrary to current code.
- Ignoring cleanup and hoping for automatic dispose.
- Spawning entities in `WaitPost(...)` without deleting them in cleanup. Stale entities leak into the next test on a reused pair. Mark `Dirty = true` or explicitly `DelEntity(...)` in cleanup.

## Examples from actual code

### Example A: scenario where lobby/in-round transitions are needed

```csharp
// Verify: GameTicker.RunLevel — grep GameRunLevel
await using var pair = await PoolManager.GetServerClient(new PoolSettings
{
    Dirty = true,       // the test changes the phase of the game
    DummyTicker = false,
    Connected = true,
    InLobby = true,
});

var ticker = pair.Server.System<GameTicker>();
await pair.Server.WaitAssertion(() =>
{
    Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
});
```

### Example B: Testing Interaction via System Call

```csharp
// Verify: InteractionSystem.UserInteraction — grep in Content.Server
await server.WaitPost(() =>
{
    // Act: server-side user interaction with the target.
    interactionSystem.UserInteraction(user, server.Transform(target).Coordinates, target);
});

await server.WaitAssertion(() =>
{
    // Assert: the expected handler actually worked.
    Assert.That(interactHand, Is.True);
});
```

### Example C: UI/BUI steps considering network RTT

```csharp
// Verify: BoundUserInterface.SendMessage — grep in RobustToolbox
await client.WaitPost(() => bui.SendMessage(message));

// We give time for client->server->client processing.
await pair.RunTicksSync(15);

await client.WaitAssertion(() =>
{
    Assert.That(expectedUiState, Is.True);
});
```

## Extension rule

When adding a new test pattern:
1. Verify the pattern against the fork's current test infrastructure (grep PoolManager, IntegrationTest).
2. Add `// Verify:` marker to every new code snippet.
3. State the triggering condition (when the pattern applies) and the non-triggering condition (when it does NOT).
4. Update this checklist if a new anti-pattern is discovered.

## Mini checklist before PR

- The test runs locally for at least 3 runs in a row.
- There is no direct dependence on the order in which other tests are performed.
- No hidden mutation of global state without rollback.
- Cleanup is explicit and correct.
- The environment parameters are minimal (nothing extra).
- Inline prototypes (`[TestPrototypes]`, `ExtraPrototypes`) pass YAML linter.

## Practical notes from docs

- `COMPlus_gcServer=1` [unverified] usually speeds up integration runs. Check against current CI configuration.
- User data is in-memory integration mode and is not saved between runs [unverified].

Use this as an operational note, but always check the final decision against the current implementation of the code.

Verified against code state: 2026-08-20
