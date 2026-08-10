# M2 Persistence Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the complete M2 EF Core SQLite persistence slice with privacy-safe contracts, recoverable migrations, repositories, and real integration tests.

**Architecture:** Application defines validated persistence inputs and store interfaces without EF types. Infrastructure maps those contracts to private EF entities through short-lived contexts, applies two tracked SQLite migrations, and protects upgrades with online backup and restore. Integration tests operate on test-owned real SQLite files.

**Tech Stack:** .NET 10.0.302, C# 14, EF Core SQLite 10.0.10, Microsoft.Data.Sqlite 10.0.10, xUnit 2.9.3.

---

## File map

- `src/DevForge.Application/Contracts/Persistence/DatabaseLocation.cs`: canonical local-data database location guard.
- `src/DevForge.Application/Contracts/Persistence/PersistableJson.cs`: bounded canonical JSON and secret-content boundary.
- `src/DevForge.Application/Contracts/Persistence/SettingContracts.cs`: typed setting value and settings store.
- `src/DevForge.Application/Contracts/Persistence/MetadataContracts.cs`: immutable IDE/tool/blueprint/profile/preset/recent-project snapshots and store interfaces.
- `src/DevForge.Application/Contracts/JournalContracts.cs`: retain the existing journal surface while documenting persistence cancellation/ordering requirements.
- `src/DevForge.Infrastructure/Persistence/DevForgeDbContext.cs`: EF context and migration-history-table configuration.
- `src/DevForge.Infrastructure/Persistence/Entities/*.cs`: private persistence rows only.
- `src/DevForge.Infrastructure/Persistence/Configurations/*.cs`: table/column/key/index/check configuration.
- `src/DevForge.Infrastructure/Persistence/Migrations/*.cs`: two production migrations and the model snapshot.
- `src/DevForge.Infrastructure/Persistence/Mapping/*.cs`: fail-closed contract/domain mapping.
- `src/DevForge.Infrastructure/Persistence/Repositories/*.cs`: short-lived-context stores.
- `src/DevForge.Infrastructure/Persistence/Migrations/SqliteMigrationCoordinator.cs`: backup, migrate, integrity check, restore.
- `tests/DevForge.UnitTests/Application/Persistence/*.cs`: value/privacy/architecture tests.
- `tests/DevForge.IntegrationTests/Persistence/*.cs`: real SQLite schema/repository/recovery tests.

## Task 1: Pin EF Core and protect the dependency boundary

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/DevForge.Infrastructure/DevForge.Infrastructure.csproj`
- Modify: `tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj`
- Test: `tests/DevForge.UnitTests/Architecture/PersistenceDependencyTests.cs`

- [ ] **Step 1: Write the failing architecture test**

Create a test that loads repository project metadata and asserts that package names beginning with `Microsoft.EntityFrameworkCore` or `Microsoft.Data.Sqlite` appear only in `DevForge.Infrastructure` and `DevForge.IntegrationTests`, and that the central versions are exactly `10.0.10`.

```csharp
[Fact]
public void EfCoreAndSqliteRemainInsideThePersistenceBoundary()
{
    var violations = PersistenceArchitecture.FindViolations(RepositoryModel.LoadFrom(AppContext.BaseDirectory));
    Assert.Empty(violations);
}
```

Implement `PersistenceArchitecture.FindViolations` as a private test helper in the same file. It reads only `RepositoryModel` metadata and returns one deterministic string per package/project/version violation.

- [ ] **Step 2: Run RED**

Run:

```powershell
dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --filter FullyQualifiedName~PersistenceDependencyTests
```

Expected: FAIL because the required central packages/references do not exist.

- [ ] **Step 3: Add exact package pins and references**

Add central versions `10.0.10` for `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.Design`, and `Microsoft.Data.Sqlite`. Reference runtime packages only from Infrastructure; give Design `PrivateAssets=all`. IntegrationTests may reference `Microsoft.Data.Sqlite` directly for raw schema/privacy assertions.

- [ ] **Step 4: Restore and run GREEN**

Run locked-file update restore, then the focused test. Expected: package policy and focused architecture tests PASS with no warnings.

- [ ] **Step 5: Commit**

```powershell
git add Directory.Packages.props src/DevForge.Infrastructure tests/DevForge.IntegrationTests tests/DevForge.UnitTests/Architecture/PersistenceDependencyTests.cs
git commit -m "build: add pinned M2 persistence dependencies"
```

## Task 2: Guard persistence inputs and privacy-sensitive JSON

**Files:**
- Create: `src/DevForge.Application/Contracts/Persistence/DatabaseLocation.cs`
- Create: `src/DevForge.Application/Contracts/Persistence/PersistableJson.cs`
- Create: `src/DevForge.Application/Contracts/Persistence/SettingContracts.cs`
- Create: `src/DevForge.Application/Contracts/Persistence/MetadataContracts.cs`
- Test: `tests/DevForge.UnitTests/Application/Persistence/DatabaseLocationTests.cs`
- Test: `tests/DevForge.UnitTests/Application/Persistence/PersistableJsonTests.cs`
- Test: `tests/DevForge.UnitTests/Application/Persistence/PersistenceContractTests.cs`

- [ ] **Step 1: Write failing value-object tests**

Cover absolute local paths, rejection of relative/UNC/device/alternate-stream/null-character paths, immutable normalization, setting kinds, JSON canonicalization, duplicate JSON properties, maximum UTF-8 size, secret-shaped property names, PEM/JWT/Bearer/OpenAI/AWS/GitHub credential shapes, `.env` dumps, and safe false positives such as `Foreign key: FK_Run`.

```csharp
[Theory]
[InlineData("{\"password\":\"value\"}")]
[InlineData("{\"log\":\"Authorization: Bearer aaa.bbb.ccc\"}")]
public void PersistableJsonRejectsSecretBearingPayloads(string json)
{
    var result = PersistableJson.Create(json);
    Assert.False(result.IsValid);
    Assert.Contains(result.Issues, issue => issue.Code == "persistence.json.secret-detected");
}
```

- [ ] **Step 2: Run RED**

Expected: compile failure because the persistence contracts do not exist.

- [ ] **Step 3: Implement minimal guarded contracts**

Use guarded factories returning Domain `ValidationResult<T>`, immutable collection snapshots, explicit enum values beginning at 1, bounded strings, and no implicit raw-string conversions. `PersistableJson` parses with `JsonDocument`, rejects duplicate names during traversal, validates every property/value, and serializes a deterministic compact representation.

- [ ] **Step 4: Run focused GREEN and refactor**

Run all `Application.Persistence` unit tests. Expected: PASS, zero warnings.

- [ ] **Step 5: Commit**

```powershell
git add src/DevForge.Application/Contracts/Persistence tests/DevForge.UnitTests/Application/Persistence
git commit -m "feat(application): add privacy-safe persistence contracts"
```

## Task 3: Create the EF model and two real migrations

**Files:**
- Create: `src/DevForge.Infrastructure/Persistence/DevForgeDbContext.cs`
- Create: `src/DevForge.Infrastructure/Persistence/DevForgeDbContextFactory.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Entities/*.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Configurations/*.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Migrations/*InitialPersistenceSchema*.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Migrations/*AddRetentionAndLookupIndexes*.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Migrations/DevForgeDbContextModelSnapshot.cs`
- Test: `tests/DevForge.IntegrationTests/Persistence/SqliteSchemaTests.cs`
- Test: `tests/DevForge.IntegrationTests/Persistence/SqliteMigrationUpgradeTests.cs`

- [ ] **Step 1: Write failing fresh-schema test**

Open a test-owned SQLite file, call the wished-for migration entry point, and query `sqlite_master` plus `PRAGMA` metadata. Assert all ten required tables, the run-step foreign key, unique identities, and final indexes.

- [ ] **Step 2: Run RED**

Expected: compile failure because the context/migrations do not exist.

- [ ] **Step 3: Implement context, rows, and explicit configurations**

Configure EF migration history as `SchemaMigrations`; use bounded `TEXT`, `INTEGER` Unix-millisecond timestamps, explicit keys, check constraints, cascade only from `ProjectRuns` to `RunSteps`, and no lazy-loading proxies.

- [ ] **Step 4: Generate/review two migrations**

Migration 1 creates the complete usable table set. Migration 2 adds retention/cache/lookup indexes and final metadata constraints. Review generated code for SQLite-supported operations and ensure no raw user input enters migration SQL.

- [ ] **Step 5: Write and run sequential-upgrade RED/GREEN**

Apply migration 1 only, insert a valid historical setting/run row, apply latest, and assert exact data preservation plus final indexes. Expected final focused IntegrationTests: PASS.

- [ ] **Step 6: Commit**

```powershell
git add src/DevForge.Infrastructure/Persistence tests/DevForge.IntegrationTests/Persistence/SqliteSchemaTests.cs tests/DevForge.IntegrationTests/Persistence/SqliteMigrationUpgradeTests.cs
git commit -m "feat(persistence): add SQLite schema and migrations"
```

## Task 4: Implement metadata repositories

**Files:**
- Create: `src/DevForge.Infrastructure/Persistence/Mapping/MetadataMapper.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Repositories/AppSettingsStore.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Repositories/IdeInstallationStore.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Repositories/EnvironmentToolStore.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Repositories/BlueprintMetadataStore.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Repositories/TeamProfileStore.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Repositories/PresetStore.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Repositories/RecentProjectStore.cs`
- Test: `tests/DevForge.IntegrationTests/Persistence/MetadataRepositoryTests.cs`

- [ ] **Step 1: Write failing repository round-trip tests**

For each store, test create/upsert/list/get/remove, deterministic ordering, immutable return snapshots, cancellation, and unique-key replacement. Use the real migrated SQLite database.

- [ ] **Step 2: Run RED**

Expected: compile failure for missing Infrastructure store implementations.

- [ ] **Step 3: Implement one short-lived-context repository at a time**

Each public operation validates cancellation first, creates one context, executes async EF APIs, saves once, disposes the context, and returns detached immutable contracts. Do not return `IQueryable`, entities, connections, or connection strings.

- [ ] **Step 4: Run focused GREEN after each repository**

Run `MetadataRepositoryTests` after each minimal implementation. Refactor shared upsert/mapping helpers only while green.

- [ ] **Step 5: Commit**

```powershell
git add src/DevForge.Infrastructure/Persistence/Mapping src/DevForge.Infrastructure/Persistence/Repositories tests/DevForge.IntegrationTests/Persistence/MetadataRepositoryTests.cs
git commit -m "feat(persistence): add local metadata repositories"
```

## Task 5: Implement the run journal atomically

**Files:**
- Modify: `src/DevForge.Application/Contracts/JournalContracts.cs`
- Create: `src/DevForge.Infrastructure/Persistence/PersistenceDataException.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Mapping/RunJournalMapper.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Repositories/SqliteRunJournalStore.cs`
- Test: `tests/DevForge.IntegrationTests/Persistence/RunJournalStoreTests.cs`

- [ ] **Step 1: Write failing journal tests**

Create real Domain runs through guarded transitions. Cover empty run, completed attempts, failed attempt with redacted error, multiple saves replacing the immutable snapshot, deterministic list order, and cancellation.

```csharp
await store.SaveAsync(run, cancellationToken);
var loaded = Assert.Single(await store.ListAsync(cancellationToken));
Assert.Equal(run.Id, loaded.Id);
Assert.Equal(run.Status, loaded.Status);
Assert.Equal(run.Attempts.Select(a => a.AttemptNumber), loaded.Attempts.Select(a => a.AttemptNumber));
```

- [ ] **Step 2: Run RED**

Expected: compile failure because `SqliteRunJournalStore` does not exist.

- [ ] **Step 3: Implement atomic save and guarded load**

Upsert the run summary and replace attempts/errors within one transaction. Rehydrate `RedactedText`, `DevForgeError`, `StepAttempt`, and `ProjectRun` only through supported guarded factories. Map any invalid persisted state to a scrubbed `PersistenceDataException` carrying code `DF-DB-001`.

- [ ] **Step 4: Add invalid-row regression tests**

Insert invalid status, duplicate attempts, and secret-shaped diagnostic data through raw test SQL. Assert the store fails closed and does not return a partial aggregate or raw offending value.

- [ ] **Step 5: Run GREEN and commit**

```powershell
git add src/DevForge.Application/Contracts/JournalContracts.cs src/DevForge.Infrastructure/Persistence tests/DevForge.IntegrationTests/Persistence/RunJournalStoreTests.cs
git commit -m "feat(persistence): persist guarded run journals"
```

## Task 6: Back up, migrate, verify, and restore safely

**Files:**
- Create: `src/DevForge.Infrastructure/Persistence/Migrations/DatabaseMigrationResult.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Migrations/ISqliteBackupTransport.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Migrations/SqliteBackupTransport.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Migrations/IDatabaseMigrationExecutor.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Migrations/EfDatabaseMigrationExecutor.cs`
- Create: `src/DevForge.Infrastructure/Persistence/Migrations/SqliteMigrationCoordinator.cs`
- Test: `tests/DevForge.IntegrationTests/Persistence/MigrationRecoveryTests.cs`

- [ ] **Step 1: Write the failing restoration test**

Create a migration-1 database with sentinel metadata. Inject an executor that mutates the primary database and throws. Assert the coordinator reports `DF-DB-001`, restores the original sentinel, passes `PRAGMA integrity_check`, and does not include the path/connection string/raw exception in its result.

- [ ] **Step 2: Run RED**

Expected: compile failure for the missing migration coordinator.

- [ ] **Step 3: Implement SQLite online backup/restore**

Use `SqliteConnection.BackupDatabase` between validated primary and backup locations. Never shell out or use raw file-copy commands. Close contexts before restore, verify integrity after migrate/restore, and preserve cancellation semantics without masking a required restore after mutation.

- [ ] **Step 4: Test happy paths and failure phases**

Cover fresh creation (no backup), upgrade (backup created), successful integrity verification, migration failure restore, integrity failure restore, and restore-failure reporting that keeps both database artifacts for manual recovery.

- [ ] **Step 5: Run GREEN and commit**

```powershell
git add src/DevForge.Infrastructure/Persistence/Migrations tests/DevForge.IntegrationTests/Persistence/MigrationRecoveryTests.cs
git commit -m "feat(persistence): add recoverable SQLite migrations"
```

## Task 7: Privacy, cancellation, concurrency, and architecture hardening

**Files:**
- Test: `tests/DevForge.IntegrationTests/Persistence/PersistencePrivacyTests.cs`
- Test: `tests/DevForge.IntegrationTests/Persistence/PersistenceConcurrencyTests.cs`
- Modify only the scoped M2 implementation files needed by failing regressions.

- [ ] **Step 1: Write raw-database privacy tests**

Attempt every public persistence write with credential-shaped fixtures and `.env` content. Assert rejection. Then read every SQLite `TEXT`/`BLOB` value and assert forbidden fixture needles, the generated connection string, source-code fixture, and raw log fixture do not occur.

- [ ] **Step 2: Write cancellation/concurrency tests**

Assert pre-cancelled tokens do not mutate data and multiple store instances can perform concurrent reads without sharing a context. Serialize conflicting writes through SQLite constraints and assert deterministic final state.

- [ ] **Step 3: Capture RED and implement minimal regressions**

For every failure, record the expected failing assertion before modifying production code. Change only the smallest relevant validator, mapping, or repository operation.

- [ ] **Step 4: Run focused and full persistence GREEN**

Run all M2 unit and integration tests twice to expose ordering/context-lifetime flakes. Expected: zero failed/skipped M2 tests.

- [ ] **Step 5: Commit**

```powershell
git add src/DevForge.Application/Contracts/Persistence src/DevForge.Infrastructure/Persistence tests/DevForge.UnitTests/Application/Persistence tests/DevForge.IntegrationTests/Persistence
git commit -m "test(persistence): harden privacy and concurrency"
```

## Task 8: ADR, milestone evidence, and exit gate

**Files:**
- Create: `docs/decisions/0004-ef-core-sqlite-persistence-and-recoverable-migrations.md`
- Modify: `docs/implementation-plan.md`
- Modify: `docs/implementation-status.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Write ADR-0004**

Record EF Core SQLite 10.0.10, private EF entities, short contexts, `SchemaMigrations`, Unix-millisecond timestamps, online backup/restore, and rejected alternatives.

- [ ] **Step 2: Run final verification from the repository root**

```powershell
dotnet restore DevForge.sln --locked-mode
dotnet format DevForge.sln --no-restore
dotnet format DevForge.sln --verify-no-changes --no-restore
dotnet build DevForge.sln --configuration Release --no-restore
dotnet test DevForge.sln --configuration Release --no-build --no-restore
dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~Persistence
dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore
```

Expected: every command exits 0; build has zero warnings/errors; all M2 tests pass with zero skipped.

- [ ] **Step 3: Update exact evidence**

Replace the status document's current milestone with M2 Complete only after copying exact command results and test counts. Set the recommended next milestone to M3 only if every M2 exit condition passes.

- [ ] **Step 4: Audit scope and secrets**

Run `git diff --check`, inspect every changed path, confirm no database/test artifact/connection string/credential is staged, and confirm the two pre-existing CRLF-only worktree marks are not accidentally committed.

- [ ] **Step 5: Commit milestone documentation**

```powershell
git add docs/decisions/0004-ef-core-sqlite-persistence-and-recoverable-migrations.md docs/implementation-plan.md docs/implementation-status.md CHANGELOG.md docs/superpowers/plans/2026-08-10-m2-persistence.md
git commit -m "docs: complete M2 persistence milestone"
```
