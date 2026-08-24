---
name: ss14-tests-poolmanager
description: An in-depth guide to the SS14 integration test framework: PoolManager, TestPair lifecycle, server/client synchronization, action emulation and pitfalls.
---

# SS14 Integration Test Framework

## Mental model

`PoolManager` maintains a pool of pre-initialized server/client pairs (`TestPair`).
Tests borrow a pair, run, and return it via `CleanReturnAsync()` for reuse.
`DisposeAsync()` is destructive — it destroys the pair and degrades pool performance.
`PoolSettings` control pair lifecycle: `Destructive`, `Fresh`, `Connected`, `Dirty`, `NoLoadTestPrototypes`.
Critical calculated properties:
- `MustNotBeReused => Destructive || NoLoadTestPrototypes` — pair is destroyed after the test.
- `MustBeNew => Fresh || NoLoadTestPrototypes` — pool creates a new pair instead of reusing.

## When to use

Use this skill when needed:
- understand how `PoolManager` and reusing server/client pairs actually work;
- it is safe to configure `PoolSettings` and `PairSettings`;
- correctly synchronize server and client ticks;
- emulate player actions (input, UI, BUI, interactions);
- analyze typical pool problems and flaky behavior.

If the task is specifically about designing and writing a new test as a product, use a separate skill `ss14-tests-authoring`. Here the focus is only on the infrastructure and execution mechanics.

Quality filter before transferring the pattern to work:
- do not use examples older than 2 years;
- do not use fragments that contain TODO/problematic comments on the test topic;
- if in doubt, choose a more recent and lower-level example from the engine.

## How `PoolManager` selects and prepares a pair

The algorithm is essentially this:
1. If `MustBeNew`, a new pair is created.
2. Otherwise, a suitable one is taken from the pool.
3. If `CanFastRecycle(...) == true`, only `ApplySettings(...)` applies.
4. Otherwise, the full `RecycleInternal(...)` is executed.
5. After the pair is issued, stabilization ticks and tick delta synchronization are run.

Practical conclusion:
- even after a "quick" return, immediate stability cannot be assumed;
- after critical operations, `RunTicksSync(...)` and `SyncTicks(...)` are always appropriate.

**Order matters:** `RunTicksSync(N)` advances both sides by N ticks; `SyncTicks(targetDelta)` then aligns the remaining offset. Always call `SyncTicks` **after** `RunTicksSync`, not before.

## Lifecycle couples and return to the pool

Correct way:
1. Take a pair.
2. Execute the script.
3. Call `CleanReturnAsync()`.

What `CleanReturnAsync()` does:
- `Cleanup()` pairs;
- clearing/returning changed CVars;
- checking that client/server are alive (if the test is not destructive);
- check runtime exceptions;
- transfer the pair to `Ready` or complete destruction if reuse is prohibited.

If you don't call `CleanReturnAsync()`, dirty-dispose will work via `DisposeAsync()`:
- the pair is destroyed;
- a warning appears;
- the performance and stability of the test run degrades.

## Server/client: correct execution contract

`IIntegrationInstance` has strict discipline:
- `WaitPost(...)` / `Post(...)`: mutations only.
- `WaitAssertion(...)` / `Assert(...)`: checks and `Assert.That(...)`.
- `WaitIdleAsync()`: ensures that the queue is processed.

Responsibilities cannot be mixed:
- assertions inside `Post(...)` give noisy and hard to read crashes;
- mutations in `WaitAssertion(...)` break the cause-and-effect model of the test.

## Specificity of `PoolSettings` in content

Setting options in content integration tests:
- `InLobby = true` automatically requires connected state. It also affects `CanFastRecycle` eligibility.
- `DummyTicker` is used for an easier mode without full round-flow. With `DummyTicker = true` (default), `GameTicker` does not tick — round-level assertions (`RunLevel`, `PlayerStatus`) are meaningless. Set `DummyTicker = false` explicitly when testing round-flow.
- `Dirty = true` is needed if the test changes the state of the round, readiness, lobby phases, etc.
- `AdminLogsEnabled` enable only when the test actually checks the admin logs.

`CanFastRecycle` in the content additionally takes into account:
- `DummyTicker`,
- `Map`,
- `InLobby`.

## Emulate player actions

### 1) Enter (keybind)

In production code this is done through `ClientFullInputCmdMessage` and `InputSystem.HandleInputCommand(...)`.

```csharp
// Verify: InputSystem.HandleInputCommand — grep in Content.Shared
// Emulation of pressing keybind in a client instance.
var funcId = inputManager.NetworkBindMap.KeyFunctionID(ContentKeyFunctions.TryPullObject);
var msg = new ClientFullInputCmdMessage(client.Timing.CurTick, client.Timing.TickFraction, funcId)
{
    State = BoundKeyState.Down,
    Coordinates = client.EntMan.GetCoordinates(targetCoords),
    Uid = client.EntMan.GetEntity(targetNetEntity),
};

await client.WaitPost(() =>
{
    inputSystem.HandleInputCommand(clientSession, ContentKeyFunctions.TryPullObject, msg);
});
```

### 2) Click on the UI control

The standard way is to send `Down` + `Up` via `GUIBoundKeyEventArgs` and `DoGuiEvent(...)`:

```csharp
// Verify: IIntegrationInstance.DoGuiEvent — grep in RobustToolbox
// Pressed the button.
await client.DoGuiEvent(control, downArgs);
await pair.RunTicksSync(1);

// Released the button.
await client.DoGuiEvent(control, upArgs);
await pair.RunTicksSync(1);
```

### 3) BUI message

After `SendMessage(...)` they usually give additional ticks for round-trip client<->server:

```csharp
// Verify: BoundUserInterface.SendMessage — grep in RobustToolbox
await client.WaitPost(() => bui.SendMessage(msg));
await pair.RunTicksSync(15); // handling reserve on both sides
```

### 4) Server interaction directly

For low-level interaction scenarios, use `UserInteraction(...)` inside `WaitPost(...)`, and then wait for the consequences (do-after, target change, synchronization).

## Patterns

- `await using var pair = await PoolManager.GetServerClient(settings);` + explicit `await pair.CleanReturnAsync();`
- After critical steps: `RunTicksSync(...)` and when comparing server/client also `SyncTicks(targetDelta: ...)`. Call `SyncTicks` **after** `RunTicksSync`.
- Separation: `WaitPost` for actions, `WaitAssertion` for checks.
- For special cases of global state (for example, registration of tile defs): `Pool = false`.
- For non-standard prototypes: `ExtraPrototypes` or `[TestPrototypes]` instead of external mutable dependencies.

## Anti-patterns

- Rely on `DisposeAsync()` and don't do `CleanReturnAsync()`.
- Mix mutation and checks in one callback without discipline.
- Check server/client without tick alignment and then catch the "random" flack.
- Set `Dirty = true` "just in case" in every test.
- Use `NoLoadTestPrototypes` without real need (expensive way, no reuse).
- Mutate CVars or IoC singletons without `Dirty = true` or `Pool = false`. The CVar state persists on the reused pair and causes ghost failures in subsequent tests.
- Spawn entities without deleting them. Stale entities accumulate on the reused pair and cause side effects in later tests.

## Examples from actual code

### Example A: basic borrow/return via pool

```csharp
// Verify: PoolManager.GetServerClient — grep CleanReturnAsync
[Test]
public async Task RoundScenario_Works()
{
    await using var pair = await PoolManager.GetServerClient(new PoolSettings
    {
        Connected = true,
        Dirty = true,
        DummyTicker = false,
        InLobby = true,
    });

    // Let us stabilize the initial state of the pair.
    await pair.RunTicksSync(5);
    await pair.SyncTicks(targetDelta: 1);

    await pair.Server.WaitAssertion(() =>
    {
        var ticker = pair.Server.System<GameTicker>();
        Assert.That(ticker.RunLevel, Is.EqualTo(GameRunLevel.PreRoundLobby));
    });

    // An explicit clean-return is mandatory for the health of the pool.
    await pair.CleanReturnAsync();
}
```

### Example B: Correct separation of Post and Assertion

```csharp
// Verify: IIntegrationInstance.WaitPost / WaitAssertion
EntityUid entity = default;

await server.WaitPost(() =>
{
    // Action only: create an entity.
    entity = server.EntMan.SpawnEntity("MobHuman", spawnCoords);
});

await server.WaitAssertion(() =>
{
    // Just checking: making sure that the entity exists.
    Assert.That(server.EntMan.EntityExists(entity), Is.True);
});
```

### Example C: manual server/client start for a special case

```csharp
// Verify: StartServer/StartClient — grep Pool=false
var serverOpts = new ServerIntegrationOptions
{
    Pool = false,              // insulation, no reuse
    ExtraPrototypes = Prototypes,
};
var clientOpts = new ClientIntegrationOptions
{
    Pool = false,
    ExtraPrototypes = Prototypes,
};

var server = StartServer(serverOpts);
var client = StartClient(clientOpts);
await Task.WhenAll(server.WaitIdleAsync(), client.WaitIdleAsync());

Assert.DoesNotThrow(() => client.SetConnectTarget(server));
await client.WaitPost(() => client.ResolveDependency<IClientNetManager>().ClientConnect(null!, 0, null!));
```

## Extension rule

When adding a new emulation pattern or anti-pattern:
1. Verify the API call exists in the fork's current RobustToolbox (grep the class/method).
2. Add `// Verify:` marker to every new code snippet.
3. State triggering and non-triggering conditions.
4. Keep `SKILL.md` under 300 lines — move deep details to `references/` if needed.

## Practical notes from the documentation

- To speed up the test run, `COMPlus_gcServer=1` [unverified] is useful (especially noticeable in integration tests). Check against current CI configuration.
- In integration tests, user data is stored in in-memory FS and does not persist between runs [unverified].

Use this as operational guidance, but check the CVar names and behavior against the current code.

Verified against code state: 2026-08-20
