# ADR-0004: EF Core SQLite Persistence and Recoverable Migrations

- Status: Accepted
- Date: 2026-08-10
- Milestone: M2

## Context

The specification requires local SQLite metadata, EF Core migrations, short-lived database contexts, a persisted run journal, and database backup before a risky upgrade. The database must never contain source code, credentials, `.env` content, connection strings, `gh auth token` output, or unredacted logs.

SQLite also has migration and type limitations that affect the design: it does not support every relational schema operation, `DateTimeOffset` ordering is limited, and runtime migrations use a SQLite migration lock.

## Decision

- Pin `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`, and direct `Microsoft.Data.Sqlite` usage at version `10.0.10` through central package management.
- Keep all EF Core entities, configurations, migrations, connections, and repositories inside Infrastructure.
- Expose only validated immutable contracts from Application; never expose EF entities, `DbContext`, `IQueryable`, connections, or connection strings.
- Create a short-lived `DevForgeDbContext` for each repository operation.
- Use EF Core's migration history mechanism with table name `SchemaMigrations` rather than maintaining a second migration ledger.
- Persist UTC instants as Unix milliseconds and enum-like values as validated bounded strings.
- Store only explicit metadata columns. JSON payloads must cross a bounded canonical `PersistableJson` privacy boundary before persistence.
- Save a run summary and its attempt/error snapshot in one transaction, and rehydrate Domain objects only through guarded factories.
- Before upgrading an existing database, use SQLite online backup into the guarded local data root. If migration or integrity verification fails, close active connections, restore the backup, verify integrity, and return scrubbed error code `DF-DB-001`.
- Keep migration execution and backup transport separable internally so failure injection can prove recovery without shipping a deliberately broken migration.

## Alternatives considered

1. JSON-blob-centric tables reduce mapping code but weaken indexing, retention queries, constraints, and privacy review.
2. Raw `Microsoft.Data.Sqlite` repositories and hand-written migration scripts provide more direct control but reject the specification's EF Core baseline and increase schema-maintenance cost.
3. Copying the database file with direct file APIs is unsafe while SQLite connections may be active. SQLite online backup provides a consistent snapshot without adding an M3 workspace file-system implementation prematurely.
4. Applying migrations without a pre-upgrade backup relies only on transactional behavior and does not satisfy the explicit recovery requirement.

## Consequences

- Infrastructure gains the only EF Core/SQLite dependencies in production code.
- Migration and repository tests require real temporary SQLite databases on Windows CI.
- Any future schema change must add a reviewed migration and upgrade-preservation test.
- Persistence contracts are intentionally stricter than arbitrary JSON/key-value storage.
- Production startup composition and safe-mode UI remain M6 responsibilities; M2 delivers the reusable migration coordinator and repositories.

## Validation

M2 verification on 2026-08-10 completed locked restore, format verification, Release build with zero warnings/errors, 383 solution tests with zero failures/skips, focused Unit and Integration suites, real fresh/upgrade/restore SQLite scenarios, raw-database privacy inspection, and EF Core pending-model verification. The model snapshot matches the two tracked migrations.
