---
name: ss14-migrations
description: Workflow for creating, verifying, and rolling back EF Core database migrations in SS14's dual SQLite/PostgreSQL setup - generating both contexts together, checking generated artifacts and snapshots, engine-specific type configuration, rollback, and upstream snapshot-conflict recovery. Use it when changing server-database models, adding tables or columns, or fixing server startup failures during migration apply.
---

# Claude Bridge

Canonical source skill file:
../../../.agents/skills/ss14-migrations/SKILL.md.

Use that file as the entrypoint and load resources from the same source skill directory.
