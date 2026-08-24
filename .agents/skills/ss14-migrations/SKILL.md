---
name: ss14-migrations
description: Workflow for creating, verifying, and rolling back EF Core database migrations in SS14's dual SQLite/PostgreSQL setup - generating both contexts together, checking generated artifacts and snapshots, engine-specific type configuration, rollback, and upstream snapshot-conflict recovery. Use it when changing server-database models, adding tables or columns, or fixing server startup failures during migration apply.
---

# Database Migrations

Workflow for generating and maintaining EF Core migrations for the server database, which ships two synchronized migration sets: SQLite and PostgreSQL 🙂

## Mental model

One feature = one synchronized pair of migrations. The database project defines two EF Core contexts over a shared entity model:

1. SQLite context -> `Migrations/Sqlite` + its own model snapshot.
2. PostgreSQL context -> `Migrations/Postgres` + its own model snapshot.

Both sets apply automatically at server startup (`Database.Migrate()`), so a missing pair member shows up as a startup crash on exactly one engine while the other works fine.

**Single most important pattern:** generate both contexts together with the helper script run from *inside* the database project directory, then confirm four artifacts changed - two migration files plus two updated model snapshots.

## Source of truth and boundaries of trust

- Code is ground truth: models, contexts, and helper scripts all live in the server database project.
- Shared entities live in `Model*.cs` partial classes; engine-divergent configuration lives in the `ModelSqlite.cs` / `ModelPostgres.cs` partials.
- Context architecture and domain schema details belong to the sibling `ss14-databases` skill; this skill covers only the migration workflow.
- Prerequisite: the `dotnet-ef` tool is installed (`dotnet tool install --global dotnet-ef`). No live Postgres is required for generation: the design-time factories use dummy connection strings.

## Create a migration

### Steps

1. **Make the model change compile.** Edit the relevant `Model*.cs` partial (shared entities) or `ModelSqlite.cs` / `ModelPostgres.cs` (engine-only types/conversions). Generation reads the built assembly, so broken code means broken output.

2. **Go into the database project directory**, then run the helper script with a CamelCase name:

    ```powershell
    ./add-migration.ps1 MyNewFeature
    ```

    The scripts pass no `--project` argument, so they only work when the current working directory is the database project folder. If the script is unavailable, the equivalent manual pair is:

    ```powershell
    # SQLite set
    dotnet ef migrations add --context SqliteServerDbContext -o Migrations/Sqlite MyNewFeature

    # PostgreSQL set
    dotnet ef migrations add --context PostgresServerDbContext -o Migrations/Postgres MyNewFeature
    # Verify: add-migration.ps1 - grep "dotnet ef migrations add"
    ```

3. **Check what was generated.** Success looks like:

    - `Migrations/Sqlite/<timestamp>_MyNewFeature.cs` (+ `.Designer.cs`)
    - `Migrations/Postgres/<timestamp>_MyNewFeature.cs` (+ `.Designer.cs`)
    - Both `*ServerDbContextModelSnapshot.cs` files updated

    Timestamps are taken independently per context and usually differ by seconds; the migration *names* must match across engines.

4. **Review both generated files** with the inspection checklist below before committing.

### Inspection checklist

- **Data-loss warnings**: did EF Core emit destructive operations (drop column/table)? Rebuild-style operations on SQLite deserve extra scrutiny.
- **Engine-specific types**: Postgres-only column types (`inet`, `jsonb`) never appear literally in the shared model; they are mapped through converters/annotations in the `ModelPostgres.cs` / `ModelSqlite.cs` partials. If a generated migration shows an unmapped native type, fix the model config, not the generated file.
- **Raw SQL**: any `migrationBuilder.Sql(...)` call must be written per engine, in that engine's file only:

    ```csharp
    // Inside Migrations/Postgres/<timestamp>_MyNewFeature.cs (generated):
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Postgres dialect is safe here: this file only ever runs against PostgreSQL.
        migrationBuilder.Sql("UPDATE player SET some_column = 0 WHERE some_column IS NULL;");
    }
    // The sibling Migrations/Sqlite/<timestamp>_MyNewFeature.cs needs its own SQLite
    // dialect version - never copy the Postgres statement verbatim.
    // Verify: Migrations/Sqlite + Migrations/Postgres - grep "migrationBuilder.Sql"
    ```

## Patterns

1. Generate both contexts in one script invocation - prevents divergent engine schemas by making the pair atomic instead of two forgettable manual commands.
2. Compile the model before generating - prevents migrations produced from a stale/broken assembly that silently mismatch the intended schema.
3. After generation, diff both snapshots - prevents shipping a half pair when the script fails midway on the second context.
4. Put engine-divergent column types and value converters only in the `ModelSqlite` / `ModelPostgres` partial overrides - prevents dialect-native types leaking into the shared model and crashing the other engine's generation.
5. Fix bugs in already-applied migrations with a new follow-up migration - prevents permanent divergence for servers whose migration history is already recorded.
6. Write engine-specific raw SQL separately per generated file - prevents runtime SQL dialect crashes at startup.
7. Prefer additive changes (new column, obsolete old one) for large existing tables - prevents slow and risky SQLite table-rebuild copy operations.

## Anti-patterns

1. **Shipping one engine only.**
   Symptom: works locally on SQLite, crashes on production Postgres (or vice versa).
   Do: every feature lands with both files present and both snapshots updated.

2. **Expecting identical timestamps across engines.**
   The two `migrations add` calls run sequentially, so timestamps legitimately differ; only the name must match. Treating differing timestamps as corruption leads to false alarms and pointless "fixes".

3. **Editing a migration that is already merged/applied.**
   EF Core records the migration ID string in `__EFMigrationsHistory`; editing file contents leaves applied databases silently out of sync forever. Always add a corrective migration instead.

4. **Complex in-place column changes on big SQLite tables.**
   SQLite cannot rename/retype columns in place; EF Core emulates it via create-temp-table/copy/drop/rename, which is slow and dangerous at scale. Additive columns beat destructive retypes.

5. **Running the helper scripts from the repository root.**
   They resolve everything from the current working directory; running them one level up generates into the wrong tree or fails outright. Enter the database project folder first.

6. **Hand-editing snapshots or `.Designer.cs` files.**
   These are generator-owned artifacts. A hand-stitched snapshot looks green but corrupts every future generation.

## Rollback of an uncommitted migration

If a freshly created migration has not been committed/pushed yet:

```powershell
dotnet ef migrations remove --context SqliteServerDbContext
dotnet ef migrations remove --context PostgresServerDbContext
# Remove BOTH sides: dropping only one leaves a half pair behind.
# Verify: add-migration.ps1 - same context IDs as above
```

For anything already committed, roll forward with a new migration instead (anti-pattern 3).

## Upstream sync and snapshot conflicts

Upstream regularly adds migrations, so rebases collide on the append-only snapshot/designer chains.

1. Expect conflicts in `*ServerDbContextModelSnapshot.cs` and `*.Designer.cs`; git cannot merge these meaningfully.
2. Prefer regeneration over hand-merging: take the incoming snapshot/designer versions, remove your local unpushed migration files, re-enter the database project folder, and re-run the helper script so your migrations regenerate cleanly on top.
   `[unverified: exact recovery flow used historically in this fork - check recent upstream-sync commits before relying on step order]`
3. Never hand-stitch snapshot bodies to "resolve" a conflict; a subtly corrupted snapshot breaks all future generations while the build stays green.

## Extending this skill

1. Add new workflow facts as point entries in Patterns/Anti-patterns, keeping the symptom/why/do shape.
2. Move a genuinely new topic (data seeding, backups, migration testing harness) into a separate sibling skill instead of growing this one past its budget.
3. After any edit: refresh the footer date, re-run the reverse walkthrough, and re-run the bridge check.

## SS14 dimension checklist

All rows are N/A: the database layer runs outside the ECS runtime and is not predicted, networked, or ordered with simulation systems.

| Dimension | Covered | Notes |
|---|---|---|
| Prediction gating (`InPrediction` / `IsPredictionEnabled`) | N/A | Database layer, no ECS runtime code |
| Server / Client / Shared split + `[NetworkedComponent]` | N/A | Server-only persistence concern |
| Event and `UpdatesBefore`/`UpdatesAfter` ordering | N/A | No entity system involved |
| Component lifecycle (Add/Remove/Initialize/Shutdown) | N/A | No components involved |
| Hot path and allocations (`EntityQuery`, by-ref events, `DirtyField`) | N/A | Startup-time operation only |
| PVS / network visibility of client-side logic | N/A | Nothing client-visible |

Verified against code state: 2026-08-23
