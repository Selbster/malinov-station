---
name: ss14-databases
description: SS14 database architecture — dual engine (PostgreSQL/SQLite), EF Core DbContext hierarchy, post-BanRefactor ban model, TypedHwid, Database Notifications, and data access patterns
---

# Databases in SS14

This skill covers the EF Core architecture of SS14: dual engine support, data model, database access patterns, and pitfalls. Use it when creating new tables, writing database queries, refactoring models, or troubleshooting data type issues.

## Architecture

### Component diagram

```
IServerDbManager             — facade (all public methods)
  ServerDbBase               — abstract base class (shared logic)
    ServerDbSqlite           — SQLite implementation
    ServerDbPostgres         — PostgreSQL implementation (partial class with notifications)

ServerDbContext              — abstract EF Core context (DbSets, OnModelCreating)
  SqliteServerDbContext      — SQLite context (ValueConverters for IP, JSON)
  PostgresServerDbContext    — PostgreSQL context (GIN indexes, check constraints)
```

### Key supporting classes

- **`DbGuard`** — nested `abstract class` inside `ServerDbBase`, implements `IAsyncDisposable`. Contains `abstract ServerDbContext DbContext`. Engine-specific subclasses (`DbGuardImpl`) add strongly-typed `SqliteDbContext` or `PgDbContext` properties. Acts as a RAII wrapper for controlled dispose + semaphore release.
- **`EFCoreExtensions.ApplyIncludes`** — extension method for chaining `.Include()`. Accepts `IEnumerable<Expression<Func<TEntity, object>>>`. Used across all ban queries.
- **`SnakeCaseExtension`** — `IDbContextOptionsExtension` that enables snake_case naming convention for table and column names in both DbContexts.
- **`BanMatcher`** — static class for in-memory ban matching against a player. Used by SQLite (Postgres does the same server-side in SQL).

### Responsibility boundaries

- `Content.Server.Database/` — **models only** (EF Core entity classes, contexts, ValueConverters, migrations).
- `Content.Server/Database/` — **logic only** (ServerDbManager, ServerDbBase, BanDef, DatabaseRecords, etc.).
- Never use EF entity classes outside `Content.Server.Database/`. For the external layer, use record types from `DatabaseRecords.cs` and domain objects like `BanDef`.

## Source of truth

- **Code is the single source of truth**. Documentation and comments are a secondary layer.
- Migration `20260120200455_BanRefactor` (Jan 2026) radically changed the ban model. Any code written before this date is invalid for ban logic.
- Migration `20241111170107_ModernHwid` introduced `TypedHwid` and `HwidType` (Legacy/Modern) — the old flat `byte[]` is no longer used alone.

## Data model

### Main tables

| DbSet | Class | Purpose |
|-------|-------|---------|
| `Preference` | `Preference` | Player preferences: selected slot, OOC color, construction favorites |
| `Profile` | `Profile` | Character profile: name, appearance, slot, markings |
| `AssignedUserId` | `AssignedUserId` | Guest account GUID persistence |
| `Player` | `Player` | Player records: IP, HWID, names, last seen |
| `Admin` | `Admin` | Admin accounts with flags and rank |
| `AdminRank` | `AdminRank` | Admin rank definitions with permission flags |
| `Round` | `Round` | Round tracking with start date and players |
| `Server` | `Server` | Server identity for multi-server setups |
| `Ban` | `Ban` | Bans: server and role |
| `BanRound` | `BanRound` | Round association for a ban |
| `BanPlayer` | `BanPlayer` | UserId selector for a ban |
| `BanAddress` | `BanAddress` | IP/CIDR selector for a ban (NpgsqlInet) |
| `BanHwid` | `BanHwid` | HWID selector for a ban (TypedHwid) |
| `BanRole` | `BanRole` | Role selector for role bans |
| `Unban` | `Unban` | Unbans |
| `BanExemption` | `ServerBanExemption` | Ban exemption flags per user |
| `ConnectionLog` | `ConnectionLog` | Connection logs |
| `ServerBanHit` | `ServerBanHit` | Ban hit records per connection |
| `PlayTime` | `PlayTime` | Role timers |
| `AdminLog` | `AdminLog` | Admin action logs |
| `AdminLogPlayer` | `AdminLogPlayer` | Player association for admin log entries |
| `Whitelist` | `Whitelist` | Whitelisted users |
| `Blacklist` | `Blacklist` | Blacklisted users |
| `UploadedResourceLog` | `UploadedResourceLog` | Log of uploaded client resources |
| `AdminNotes` | `AdminNote` | Admin notes |
| `AdminWatchlists` | `AdminWatchlist` | Watchlists |
| `AdminMessages` | `AdminMessage` | Admin messages |
| `RoleWhitelists` | `RoleWhitelist` | Job whitelist |
| `BanTemplate` | `BanTemplate` | Ban templates (used by SS14.Admin) |
| `IPIntelCache` | `IPIntelCache` | IP reputation cache |
| `CustomVoteLog` | `CustomVoteLog` | Custom vote logs |
| `CustomVoteLogOption` | `CustomVoteLogOption` | Options for each custom vote |

### Ban structure (post-BanRefactor)

`Ban` no longer has flat `userId`, `address`, `hwid` columns. Instead, it uses lists:

```
Ban
├── Players: List<BanPlayer>     — UserId matching
├── Addresses: List<BanAddress>   — IP/CIDR matching (NpgsqlInet)
├── Hwids: List<BanHwid>          — HWID matching (TypedHwid)
├── Rounds: List<BanRound>        — round association
├── Roles: List<BanRole>?         — roles (BanType.Role only)
├── Unban: Unban?                 — unban (if present)
├── BanHits: List<ServerBanHit>   — connection hit records
```

Each selector (`BanPlayer`, `BanAddress`, `BanHwid`) implements `IBanSelector { int BanId; Ban? Ban; }`. This enables building Union queries in Postgres through a common interface.

`BanType` discriminates ban type:
- `BanType.Server` — Roles = null, ExemptFlags can be non-zero.
- `BanType.Role` — Roles must be non-empty, ExemptFlags = 0 (enforced by `NoExemptOnRoleBan` check constraint).

### TypedHwid

`TypedHwid` is an owned entity (`[Owned]`) with:
- `byte[] Hwid` — raw HWID bytes
- `HwidType Type` — `Legacy` or `Modern`

It has implicit operators for conversion between `TypedHwid` and `ImmutableTypedHwid`.

Used in: `Player.LastSeenHWId`, `ConnectionLog.HWId`, `BanHwid.HWId`.

### AdminLog

- Composite key: `(RoundId, Id)` — `RoundId` is both FK and part of PK.
- `AdminLogPlayer` — triple key: `(RoundId, LogId, PlayerUserId)`.
- Postgres has a GIN index on `Message` with `IsTsVectorExpressionIndex("english")` for full-text search.

## DbGuard: how GetDb and GetDbImpl work

`ServerDbBase.GetDb()` is `protected abstract`, returns `DbGuard`. Engine-specific classes override it, returning `DbGuardImpl`.

**Shared logic** (ServerDbBase):
```csharp
await using var db = await GetDb(cancel);
// access only via db.DbContext — shared for both engines
var result = await db.DbContext.Preference
    .Include(p => p.Profiles)
    .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);
```

**Engine-specific logic** (ServerDbSqlite / ServerDbPostgres):
```csharp
private async Task<DbGuardImpl> GetDbImpl(CancellationToken cancel = default)
{
    await _dbReadyTask;
    await _prefsSemaphore.WaitAsync(cancel);
    return new DbGuardImpl(this, new SqliteServerDbContext(_options()));
}
// inside DbGuardImpl:
//   DbContext (shared)  → _ctx (SqliteServerDbContext)
//   SqliteDbContext      → _ctx (strongly typed)
```

DbGuardImpl calls `DisposeAsync()` on the context and releases the semaphore in `DisposeAsync`.

## NormalizeDatabaseTime

Abstract method in `ServerDbBase`, overridden per engine:
- **SQLite**: converts `DateTimeKind.Unspecified` → `Utc` (SQLite always reads as Unspecified)
- **Postgres**: asserts `DateTimeKind.Utc` and returns as-is

Used when converting EF entities → records/DTOs to guarantee UTC.

## Database Notifications (Postgres only)

`DatabaseNotification` is a `struct` with `Channel` (string) and `Payload` (string?).

`ServerDbPostgres` runs a background `NotificationListener` that:
1. Opens a separate NpgsqlConnection.
2. Executes `LISTEN channel` for each channel.
3. Loops calling `WaitAsync`, blocking until a new notification arrives.
4. On `NpgsqlNotificationEventArgs`, dispatches via `NotificationReceived`.

**SQLite**: `SendNotification` is a no-op, `SubscribeToNotifications` has no effect.

**Extension**: `ServerDbManagerExt.SubscribeToJsonNotification<TData>` deserializes `Payload` as JSON, filters, and runs the action on the main thread via `ITaskManager.RunOnMainThread`.

## Patterns

1. **Always use `await` and EF async methods** (`ToListAsync`, `SingleOrDefaultAsync`, `SaveChangesAsync`). Synchronous calls block the main thread and cause lag.

2. **Isolate engine-specific types via ValueConverter** in `SqliteServerDbContext.OnModelCreating`. For IP → `ValueConverter<IPAddress, string>`, for NpgsqlInet → `ValueConverter<NpgsqlInet, string>`, for JsonDocument → `ValueConverter<JsonDocument, byte[]>` or `ValueConverter<JsonDocument, string>`.

3. **Use `ApplyIncludes` for reusable Include chains**. All ban queries use `GetBanDefIncludes(type)`, which returns `List<Expression<Func<Ban, object>>>` with Players, Rounds, Hwids, Unban, Addresses (plus Roles if BanType.Role).

4. **Filter server-side before materialization**. Never do `db.DbContext.Player.ToList().Where(...)` — load only needed rows via `Where(...).ToListAsync()`.

5. **Use `AsSplitQuery()` for performance-critical queries** with multiple Includes to avoid Cartesian explosion.

6. **Check `DateTimeKind` after reading from SQLite** — SqliteReader always returns `Unspecified`. Use `DateTime.SpecifyKind(time, DateTimeKind.Utc)`.

7. **Use DatabaseNotification + `LISTEN`/`NOTIFY` for cross-server sync** on Postgres. This mechanism is unavailable on SQLite.

8. **Always add indexes on frequently filtered columns** in `OnModelCreating`: `Player.UserId` (unique), `Player.LastSeenUserName`, `ConnectionLog.UserId`, `ConnectionLog.Time`, `AdminLog.Date`.

9. **When adding a new model, create a DbSet in abstract `ServerDbContext`**, then add engine-specific configuration in concrete contexts (ValueConverters in SQLite, check constraints in Postgres).

## Anti-patterns

1. **Using Postgres-specific types in `ServerDbBase`** (`NpgsqlInet`, `jsonb` directly). SQLite does not know these types. Use abstract methods or ValueConverters at the context level.

2. **Calling `GetDbImpl()` from `ServerDbBase`**. This method is private in each subclass. For shared logic, use `GetDb()`. For engine-specific code, override virtual methods or add new abstract ones in `ServerDbBase`.

3. **Writing to `Ban.Address`, `Ban.UserId` and other flat fields that were removed**. After BanRefactor, bans use `BanPlayer`, `BanAddress`, `BanHwid` lists:
   ```csharp
   // WRONG — these fields don't exist
   db.DbContext.Ban.Add(new Ban { Address = ..., UserId = ... });
   // CORRECT
   db.PgDbContext.Ban.Add(new Ban
   {
       Players = [new BanPlayer { UserId = userId.UserId }],
       Addresses = [new BanAddress { Address = address.ToNpgsqlInet() }],
   });
   ```

4. **Using `SearchLogs` for admin log search**. This `virtual` method in `ServerDbContext` is not called by production code. Real search uses `StartAdminLogsQuery` (abstract in `ServerDbBase`, implemented per engine).

5. **Forgetting the `prefsSemaphore` in SQLite**. SQLite does not support concurrent writes. `ServerDbSqlite` uses `ConcurrencySemaphore` to control the number of simultaneous connections. In synchronous mode (tests), `maxCount` must be 1.

6. **Trying to subscribe to DatabaseNotification on SQLite**. `SendNotification` is a no-op, `SubscribeToNotifications` has no effect. This is a Postgres-only mechanism.

7. **Doing `select *` and filtering in memory**. SQLite's `GetBanQueryAsync` is forced to pull all bans into memory (due to lack of `ContainsOrEqual` for IP). In all other cases, filter in SQL.

8. **Ignoring `[CallerMemberName]` in `GetDb` parameters**. The caller method name is logged via `LogDbOp` — this helps debugging database performance issues.

## Examples

### 1. Adding a server ban (Postgres)

```csharp
// ServerDbPostgres.AddBanAsync (override from ServerDbBase)
public override async Task<BanDef> AddBanAsync(BanDef ban)
{
    await using var db = await GetDbImpl();
    var banEntity = new Ban
    {
        Type = ban.Type,
        Addresses = [..ban.Addresses.Select(ba => new BanAddress { Address = ba.ToNpgsqlInet() })],
        Hwids = [..ban.HWIds.Select(bh => new BanHwid { HWId = bh })],
        Reason = ban.Reason,
        Severity = ban.Severity,
        BanningAdmin = ban.BanningAdmin?.UserId,
        BanTime = ban.BanTime.UtcDateTime,
        ExpirationTime = ban.ExpirationTime?.UtcDateTime,
        PlaytimeAtNote = ban.PlaytimeAtNote,
        ExemptFlags = ban.ExemptFlags,
        Players = [..ban.UserIds.Select(bp => new BanPlayer { UserId = bp.UserId })],
        Rounds = [..ban.RoundIds.Select(bri => new BanRound { RoundId = bri })],
        Roles = ban.Roles == null
            ? []
            : ban.Roles.Value.Select(brd => new BanRole
                {
                    RoleType = brd.RoleType,
                    RoleId = brd.RoleId
                })
                .ToList(),
    };
    db.PgDbContext.Ban.Add(banEntity);
    await db.PgDbContext.SaveChangesAsync();
    return ConvertBan(banEntity);
}
```

**Key point**: each ban selector is added as a separate entity type. SQLite code is nearly identical, except `DateTime` is additionally converted via `DateTime.SpecifyKind` (see `ServerDbSqlite.ConvertBan`).

### 2. Ban lookup in Postgres (optimized SQL)

```csharp
// ServerDbPostgres.MakeBanLookupQuery — uses Union of selectors
// All 9 parameters are required; exemptFlags is nullable ServerBanExemptFlags?
private static IQueryable<Ban> MakeBanLookupQuery(
    IPAddress? address,
    NetUserId? userId,
    ImmutableArray<byte>? hwId,
    ImmutableArray<ImmutableArray<byte>>? modernHWIds,
    DbGuardImpl db,
    bool includeUnbanned,
    ServerBanExemptFlags? exemptFlags,
    bool newPlayer,
    BanType type)
{
    DebugTools.Assert(!(address == null && userId == null && hwId == null));

    var selectorQueries = new List<IQueryable<IBanSelector>>();

    if (userId is { } uid)
        selectorQueries.Add(db.DbContext.BanPlayer.Where(b => b.UserId == uid.UserId));

    if (hwId != null && hwId.Value.Length > 0)
    {
        selectorQueries.Add(db.DbContext.BanHwid.Where(bh =>
            bh.HWId!.Type == HwidType.Legacy && bh.HWId!.Hwid.SequenceEqual(hwId.Value.ToArray())));
    }

    if (modernHWIds != null)
    {
        foreach (var modernHwid in modernHWIds)
        {
            selectorQueries.Add(db.DbContext.BanHwid
                .Where(b => b.HWId!.Type == HwidType.Modern
                            && b.HWId!.Hwid.SequenceEqual(modernHwid.ToArray())));
        }
    }

    // exemptFlags is nullable — use GetValueOrDefault() not HasFlag() directly
    if (address != null
        && !exemptFlags.GetValueOrDefault(ServerBanExemptFlags.None).HasFlag(ServerBanExemptFlags.IP))
    {
        selectorQueries.Add(db.PgDbContext.BanAddress
            .Where(ba => EF.Functions.ContainsOrEqual(ba.Address, address)
                         // BlacklistedRange bans apply only to new players
                         && !(ba.Ban!.ExemptFlags.HasFlag(ServerBanExemptFlags.BlacklistedRange)
                              && !newPlayer)));
    }

    // Union all selectors
    var selectorQuery = selectorQueries
        .Select(q => q.Select(sel => sel.BanId))
        .Aggregate((a, b) => a.Union(b));

    var banQuery = db.DbContext.Ban.Where(b => selectorQuery.Contains(b.Id));

    if (!includeUnbanned)
    {
        banQuery = banQuery.Where(p =>
            p.Unban == null && (p.ExpirationTime == null || p.ExpirationTime.Value > DateTime.UtcNow));
    }

    if (exemptFlags is { } exempt)
    {
        if (exempt != ServerBanExemptFlags.None)
            exempt |= ServerBanExemptFlags.BlacklistedRange;
        banQuery = banQuery.Where(b => (b.ExemptFlags & exempt) == 0);
    }

    return banQuery
        .Where(b => b.Type == type)
        .ApplyIncludes(GetBanDefIncludes(type))
        .AsSplitQuery();
}
```

**SQLite counter-example**: SQLite does not support `EF.Functions.ContainsOrEqual` for IP. Instead, `GetBanQueryAsync` loads all bans into memory and filters via `BanMatcher.BanMatches()`. Both implementations must be kept in sync.

### 3. Reading a player profile (shared logic in ServerDbBase)

```csharp
public async Task<Preference?> GetPlayerPreferencesAsync(NetUserId userId, CancellationToken cancel)
{
    await using var db = await GetDb(cancel);   // DbGuard from shared method
    return await db.DbContext
        .Preference
        .Include(p => p.Profiles).ThenInclude(h => h.Jobs)
        .Include(p => p.Profiles).ThenInclude(h => h.Antags)
        .Include(p => p.Profiles).ThenInclude(h => h.Traits)
        .Include(p => p.Profiles).ThenInclude(h => h.Loadouts)
            .ThenInclude(l => l.Groups)
            .ThenInclude(g => g.Loadouts)
        .AsSplitQuery()                          // required with 3+ Includes
        .SingleOrDefaultAsync(p => p.UserId == userId.UserId, cancel);
}
```

**Important**: the method returns the EF entity `Preference?`, not the shared `PlayerPreferences?`. Conversion to the shared model happens at the `ServerPreferencesManager` level.

## Extension rules

1. When adding a new table, create the entity class in `Content.Server.Database/Model.cs` (or a separate file in the same namespace). Add a `DbSet<T>` to `ServerDbContext`.
2. If a property type needs engine-specific conversion, add a `ValueConverter` in `SqliteServerDbContext.OnModelCreating` and, if needed, a check constraint in `PostgresServerDbContext.OnModelCreating`.
3. For new business logic (read/write), create an `abstract` or `virtual` method in `ServerDbBase`. If the difference between engines is minimal, implement in `ServerDbBase`. If the difference is significant, make it abstract and implement in subclasses.
4. Do not add new logic to `ServerDbContext` — it is an EF Core context whose job is only to describe the model and configuration.
5. For new record types returned from the DB, create `sealed record` in `DatabaseRecords.cs` — do not return EF entities directly.
6. When changing the ban model, update both engines and `BanMatcher` in a single commit — desynchronizing `BanMatcher` with SQLite/Postgres implementations leads to missed or false ban matches.
