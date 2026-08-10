# Milestone M2 Persistence Implementation Plan

**Goal:** Deliver the versioned, privacy-safe SQLite persistence slice required by the DevForge specification.

**Architecture:** Application owns persistence-facing contracts and validated snapshots. Infrastructure owns EF Core entities, mappings, migrations, repositories, SQLite backup, and recovery. Domain remains free of EF Core and SQLite.

**Tech stack:** .NET SDK 10.0.302, C# 14, EF Core SQLite 10.0.10, xUnit, temporary real SQLite databases.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-10-m2-persistence-design.md`
- Task plan: `docs/superpowers/plans/2026-08-10-m2-persistence.md`
- Decision: `docs/decisions/0004-ef-core-sqlite-persistence-and-recoverable-migrations.md`

## Scope

M2 includes the required local SQLite schema, EF Core migrations, short-lived contexts, privacy-safe metadata repositories, the `IRunJournalStore` implementation, and migration backup/recovery.

M2 excludes M3 process/file-system implementations, M4 planning/catalog behavior, M5 orchestration/finalization, WPF composition, Git/GitHub automation, and blueprint catalog expansion.

## Planned files

- Modify `Directory.Packages.props`, Infrastructure and IntegrationTests project files, and lock files.
- Add Application persistence contracts under `src/DevForge.Application/Contracts/Persistence/`.
- Add EF Core implementation under `src/DevForge.Infrastructure/Persistence/`.
- Add unit tests under `tests/DevForge.UnitTests/Application/Persistence/`.
- Add integration tests under `tests/DevForge.IntegrationTests/Persistence/`.
- Add ADR-0004 and update milestone status/changelog after verification.

## Tasks

### Task 1: Persistence dependency boundary

- [x] Write architecture tests proving EF Core packages are Infrastructure-only and centrally pinned.
- [x] Capture the expected RED result.
- [x] Pin EF Core SQLite/Design 10.0.10 and update project lock files.
- [x] Reach focused GREEN without changing the approved project-reference graph.

### Task 2: Persistence contracts and privacy values

- [x] Write failing tests for guarded database locations, typed settings, `PersistableJson`, immutable metadata snapshots, and secret rejection.
- [x] Implement the minimum Application contracts and values.
- [x] Run focused tests and refactor only while green.

### Task 3: EF schema and migrations

- [ ] Write failing integration tests for required tables, keys, constraints, foreign keys, and indexes.
- [ ] Implement `DevForgeDbContext`, entities, configurations, and two tracked migrations.
- [ ] Prove fresh migration and sequential upgrade with historical data preservation.

### Task 4: Metadata repositories

- [ ] Write failing round-trip/upsert/removal/cancellation tests for settings and metadata stores.
- [ ] Implement short-lived-context repositories and deterministic mappings.
- [ ] Prove callers cannot mutate stored or returned snapshots.

### Task 5: Run journal

- [ ] Write failing round-trip and atomic-replacement tests for `ProjectRun`, attempts, and redacted errors.
- [ ] Implement `SqliteRunJournalStore` with guarded rehydration.
- [ ] Add invalid-row regression tests and fail closed with `DF-DB-001`.

### Task 6: Migration backup and recovery

- [ ] Write a failure-injection test that mutates an existing database and fails an upgrade.
- [ ] Implement SQLite online backup, migration execution, integrity checking, and restoration.
- [ ] Prove the original data is restored and the returned error contains no connection string or raw exception.

### Task 7: Privacy and concurrency hardening

- [ ] Inspect raw database values for forbidden test secrets, `.env` contents, connection strings, source, and unredacted logs.
- [ ] Prove short-lived contexts, cancellation propagation, and safe concurrent reads.
- [ ] Add regression tests for every reproducible defect found.

### Task 8: Exit gate and documentation

- [ ] Run locked restore, format, Release build, full tests, focused UnitTests, and focused IntegrationTests.
- [ ] Require zero warnings, zero errors, zero failed/skipped M2 tests.
- [ ] Update `docs/implementation-status.md`, ADR-0004, `CHANGELOG.md`, and this checklist with exact evidence.

## Exit gate

M2 passes only when fresh migration, sequential upgrade, failed-upgrade backup restore, repository/journal round trips, invalid-row handling, raw-database privacy checks, architecture checks, locked restore, format, Release build, and all tests pass with exact recorded evidence.
