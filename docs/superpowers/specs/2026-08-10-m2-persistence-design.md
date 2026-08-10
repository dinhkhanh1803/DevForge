# M2 Persistence Design

**Status:** Approved for implementation planning
**Date:** 2026-08-10
**Milestone:** M2 - Persistence

## Purpose

Implement the local metadata persistence slice required by the DevForge Studio specification using EF Core 10 and SQLite. M2 provides a versioned schema, privacy-safe persistence records, short-lived database contexts, repository implementations, and recoverable database migration. It does not implement M3 file-system/process infrastructure, planning, orchestration, Git/GitHub, or UI behavior.

## Source requirements

The design implements the persistence requirements in sections 3, 7, 9, 10, 14, 15, 16, 18, 19, and 20 of `DevForge_Studio_Codex_Implementation_Specification_V1.0`:

- SQLite with EF Core for local metadata.
- The tables `AppSettings`, `IdeInstallations`, `EnvironmentTools`, `Blueprints`, `TeamProfiles`, `Presets`, `ProjectRuns`, `RunSteps`, `RecentProjects`, and `SchemaMigrations`.
- Short-lived `DbContext` instances rather than an application-lifetime context.
- A persisted run journal and checkpoint/attempt history.
- No source code, credentials, `.env` contents, connection strings, unredacted logs, or `gh auth token` output in the database.
- Database backup before migration and safe recovery on migration failure.
- Fresh migration, sequential upgrade, and rollback-backup integration tests.

## Chosen approach

Use EF Core SQLite `10.0.10` with explicit entity configuration, dedicated persistence records, repository implementations, and a migration coordinator that performs SQLite online backup and restore.

This approach is preferred over JSON-blob-centric storage because DevForge needs deterministic indexing, retention queries, and independently testable privacy boundaries. It is preferred over hand-written raw-SQL persistence because EF Core SQLite is the product baseline and tracked migrations provide a maintainable upgrade path.

## Architecture

### Dependency direction

- `DevForge.Application` owns persistence-facing interfaces and immutable request/snapshot contracts that higher layers can consume without EF Core types.
- `DevForge.Infrastructure` owns `DevForgeDbContext`, EF entities/configurations, migrations, mapping, repositories, SQLite backup, and migration coordination.
- `DevForge.Domain` remains unaware of EF Core and SQLite.
- `DevForge.IntegrationTests` exercises the real Infrastructure implementation against temporary SQLite databases.

No EF entity is exposed outside Infrastructure. No repository returns a tracked entity or live query.

### Context lifetime

Each repository operation creates and disposes its own `DevForgeDbContext` through a factory. Transactions are scoped to one atomic repository operation. A `DbContext` is never retained across the desktop application lifetime or a complete generation run.

SQLite connections use a caller-supplied, validated local database location. Production composition of `%LocalAppData%\DevForge\data\devforge.db` is deferred until the Generic Host is implemented, but M2 supplies a guarded database-location value object and the production-ready factory.

## Schema

### Migration history

EF Core's migration history table is renamed to `SchemaMigrations`. This satisfies the required table while retaining EF Core's migration tracking semantics. DevForge does not create a second competing migration ledger.

### Metadata tables

All primary identifiers are normalized strings with explicit maximum lengths. UTC timestamps are persisted as integer Unix milliseconds to avoid SQLite `DateTimeOffset` comparison limitations. Enum-like values are stored as bounded strings and validated while mapping.

- `AppSettings`: normalized key, typed value kind, serialized non-secret value, updated UTC.
- `IdeInstallations`: IDE identifier/type, executable path, optional version, validation state, scan UTC.
- `EnvironmentTools`: tool identifier, optional executable path/version, status, scan UTC, cache expiry UTC.
- `Blueprints`: blueprint ID/version, source, trust, checksum, disabled flag, discovered UTC.
- `TeamProfiles`: profile ID/name, versioned scrubbed policy JSON, updated UTC.
- `Presets`: preset ID/name, versioned scrubbed recipe JSON, updated UTC.
- `ProjectRuns`: run ID, recipe ID, status, current step, started/updated/completed UTC, optional staging/target paths, scrubbed report metadata.
- `RunSteps`: run ID, step ID, attempt number, outcome, timings, nullable exit code, stable error code, scrubbed error summary/detail/context.
- `RecentProjects`: normalized project path identity, display name, optional repository URL, optional IDE ID, last-opened UTC.

Foreign keys cascade `ProjectRuns` to `RunSteps`. Unique constraints protect logical identities, and indexes cover run status/updated time, environment cache expiry, blueprint identity, and recent-project ordering.

### Migration sequence

M2 contains two real migrations so upgrade behavior is exercised rather than simulated:

1. `InitialPersistenceSchema` creates the required tables and core constraints.
2. `AddRetentionAndLookupIndexes` adds the retention/cache/recent-project indexes and final bounded metadata needed by the M2 repositories.

The first migration is a supported historical schema, not placeholder functionality. Upgrade tests insert valid historical data after migration 1, apply migration 2, and prove the data remains readable.

## Contracts and repositories

### Settings

`IAppSettingsStore` reads, upserts, and removes typed settings. Setting keys and string values pass a persistence privacy guard before write. The store rejects secret-shaped keys and credential-shaped values with stable validation/error codes.

### Metadata catalogs

Dedicated stores cover IDE installations, cached environment tools, blueprint metadata, team profiles, presets, and recent projects. Each store snapshots input collections, validates field bounds, and uses deterministic upsert behavior. JSON payloads are accepted only through a `PersistableJson` value object that:

- requires valid JSON;
- enforces a maximum UTF-8 byte length;
- rejects property names associated with secrets;
- rejects credential-shaped string values;
- rejects `.env` content/dump structures; and
- canonicalizes the JSON before storage for deterministic round trips.

### Run journal

`SqliteRunJournalStore` implements the existing `IRunJournalStore` contract. Saving a `ProjectRun` replaces the run summary and its immutable attempt/error snapshot in a single transaction. Loading reconstructs `StepAttempt`, `DevForgeError`, and `ProjectRun` only through their guarded rehydration factories. Invalid database rows fail closed with `DF-DB-001`; they are never returned as partially valid aggregates.

The M1 `IRunJournalStore` returns a complete immutable run list. M2 preserves that API and orders runs deterministically by update time and ID. Pagination and retention deletion behavior are deferred until the relevant application workflow is designed.

## Privacy and security

- Connection strings are constructed internally from the validated database path and are never returned or logged.
- Persistence DTOs do not contain token, password, private-key, connection-string, `.env`, raw process output, or source-code fields.
- JSON-backed metadata must cross the `PersistableJson` privacy boundary.
- Journal diagnostics persist only existing `RedactedText`/stable error data; raw exceptions and raw logs have no persistence mapping.
- Migration failures return stable `DF-DB-001` information without embedding database connection strings or exception data in user-facing details.
- No arbitrary file-copy command, shell command, `cmd /c`, or PowerShell execution is introduced.

## Migration safety and recovery

`SqliteMigrationCoordinator` performs the following flow:

1. Open the configured SQLite database and inspect pending migrations.
2. If the database exists and an upgrade is pending, create an online SQLite backup in the same guarded data root.
3. Apply migrations through EF Core under its migration lock.
4. Run integrity and schema-version checks.
5. On success, retain one rollback backup for recovery policy; later retention cleanup belongs to M10.
6. On failure, close active contexts/connections, restore the online backup into the primary database, verify integrity, and return a `DF-DB-001` failure.

A new/fresh database does not need a pre-migration backup because there is no user data to preserve. If fresh creation fails, the coordinator leaves the failed database untrusted and reports failure; it does not claim successful initialization.

Migration execution and backup transport are separated behind internal interfaces so failure injection can prove restoration without adding a deliberately broken production migration.

## Error handling

- Expected validation failures return stable issues rather than throwing.
- SQLite/EF exceptions are caught only at the Infrastructure boundary, mapped to `DF-DB-001`, and retain no unsanitized exception text in persistence-facing results.
- Cancellation is propagated through every async database operation.
- Repository writes use transactions and fail atomically.
- Duplicate/upsert races use database constraints as the final authority and return deterministic failures.

## Testing strategy

### Unit tests

- Database-location path validation and immutable snapshots.
- Setting key/value validation and privacy rejection.
- `PersistableJson` canonicalization, size limits, secret-property rejection, credential-shape rejection, and safe false-positive cases.
- Mapping tests for every enum/status and invalid-row fail-closed behavior.
- Architecture tests proving EF Core remains Infrastructure-only and package versions remain centrally pinned.

### Integration tests

- Apply all migrations to a fresh temporary SQLite database and assert required tables, constraints, foreign keys, and indexes.
- Apply only migration 1, insert historical data, upgrade to latest, and verify data preservation.
- Inject an upgrade failure after database mutation and prove the pre-upgrade online backup restores the original data and integrity.
- Round-trip typed settings and every metadata store.
- Round-trip a `ProjectRun` with attempts and redacted errors through `IRunJournalStore`.
- Prove run and step replacement is atomic and caller mutation cannot affect stored snapshots.
- Inspect raw SQLite text/blob values to confirm test credentials, `.env` contents, and connection strings are absent.
- Verify cancellation and concurrent short-lived context behavior.

Test databases live only in test-owned temporary directories and are removed by fixture cleanup after their resolved paths are verified.

## Expected implementation files

- Modify `Directory.Packages.props` and Infrastructure/IntegrationTests project files for centrally pinned EF Core packages and lock files.
- Add Application persistence contracts and privacy-safe persistence value objects under `src/DevForge.Application/Contracts/Persistence`.
- Add EF context, entities, configurations, mapping, repositories, migration coordinator, and migrations under `src/DevForge.Infrastructure/Persistence`.
- Add unit tests under `tests/DevForge.UnitTests/Application/Persistence`.
- Add real SQLite tests under `tests/DevForge.IntegrationTests/Persistence`.
- Add `docs/decisions/0004-ef-core-sqlite-persistence-and-recoverable-migrations.md`.
- Update `docs/implementation-plan.md`, `docs/implementation-status.md`, and `CHANGELOG.md`.

## Exit gate

M2 is complete only when:

1. The required schema and repositories exist with no EF dependency outside Infrastructure.
2. Fresh migration, sequential upgrade, and failed-upgrade backup restoration tests pass.
3. Repository and journal round trips pass, including invalid-row and privacy tests.
4. Raw database inspection finds no test secrets, `.env` contents, connection strings, source code, or unredacted log payloads.
5. Locked restore, format verification, Release build, full tests, and focused unit/integration suites pass with zero warnings and zero errors.
6. `docs/implementation-status.md`, ADR-0004, and `CHANGELOG.md` contain exact verification evidence.

## Deferred scope

- M3 guarded workspace file-system and process-runner implementations.
- M4 blueprint catalog loading and planner/rule behavior.
- M5 orchestration, staging ownership, retention cleanup, resume decisions, and finalization.
- M6 WPF host registration, settings screens, startup migration UI, and safe-mode UX.
- M10 retention policy execution, support bundles, packaging, and release hardening.
