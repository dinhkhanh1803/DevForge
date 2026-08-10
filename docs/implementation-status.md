# DevForge Studio Implementation Status

**Current milestone:** M2 - Persistence
**Status:** In Progress
**Last updated:** 2026-08-10

## Current M2 scope

M2 implements EF Core SQLite metadata persistence, the required schema and migrations, privacy-safe settings/metadata repositories, the existing run-journal port, and migration backup/restore. The approved design is `docs/superpowers/specs/2026-08-10-m2-persistence-design.md`; the executable TDD plan is `docs/superpowers/plans/2026-08-10-m2-persistence.md`; the architectural decision is ADR-0004.

M2 Tasks 1-6 are implemented locally: exact EF Core SQLite dependencies, guarded Application persistence contracts, the versioned schema and migrations, short-lived metadata repositories, the atomic run journal, and recoverable migration coordination. The M2 exit gate is not claimed yet; final privacy/concurrency hardening and fresh full verification remain. M3 process/file-system infrastructure, planner/orchestrator behavior, UI, Git/GitHub, and blueprint expansion remain out of scope.

## M2 progress

- Package ownership is pinned centrally, including the direct non-vulnerable SQLite native bundle dependency.
- Fresh and sequential SQLite migrations preserve historical data and create the required tables, foreign keys, and lookup indexes.
- Settings, IDE, environment-tool, blueprint, team-profile, preset, and recent-project repositories use one short-lived context per operation and return detached immutable snapshots.
- Repository integration tests cover round-trip/upsert/removal, pre-cancelled writes, detached snapshots, non-canonical stored enums, and scrubbed fail-closed corruption handling.
- The run journal atomically replaces immutable run/attempt/error snapshots and rehydrates only through Domain factories; tests cover deterministic ordering, failed/redacted diagnostics, invalid status, normalized duplicate attempts, and secret-shaped stored data.
- The migration coordinator creates guarded SQLite online backups before upgrades, verifies integrity, restores on migration/integrity failure, preserves recovery artifacts when restore fails, and restores before propagating post-mutation cancellation.

## M1 delivered baseline

M1 now provides immutable, validated domain models; dependency-free blueprint manifest contracts; validation/error results; and the twelve core Application ports. Security-shaped contracts separate executables from arguments, keep sensitive process values behind an Infrastructure-only friend boundary, scope file operations to an opaque workspace root, validate relative Windows paths, and distinguish whole-workspace from explicit-path secret scans.

Planner/rule/hash behavior, infrastructure implementations, persistence, blueprint catalog content, Git/GitHub automation, and UI expansion remain deferred to later milestones.

The approved design is recorded in `docs/superpowers/specs/2026-07-31-m1-domain-contracts-design.md`; the completed task plan and exit gate are recorded in `docs/superpowers/plans/2026-07-31-m1-domain-contracts.md`.

## TDD and hardening evidence

- Domain tests cover validation aggregation, immutable snapshots, exact run statuses and transitions, retry invariants, redaction, environment snapshots, reports, and diagnostics.
- Blueprint tests cover identifier and semantic-version validation, engine ranges, duplicate definitions, positive timeouts, trust states, and immutable snapshots.
- Application tests cover all twelve ports, cancellation-token requirements, request snapshots, separated executable/arguments, bounded process inputs, internal sensitive-value reveal, root-scoped file operations, canonical Windows paths, and secret-scan scopes.
- The final checkpoint initially failed to compile because two test stubs still implemented the superseded workspace interface. After synchronizing them with `Root`, enumeration, guarded cleanup, and guarded move operations, 91 of 92 Application tests passed.
- The remaining test proved the sensitive reveal method was internal but had no friend assembly. Adding `InternalsVisibleTo("DevForge.Infrastructure")` closed that boundary and all 92 focused Application tests passed.

## Exit gate evidence

Fresh local verification on 2026-07-31 used the workspace-local .NET SDK 10.0.302 and completed in this order:

| Gate | Command | Exact result |
| --- | --- | --- |
| Format | `dotnet format DevForge.sln --no-restore` followed by `--verify-no-changes --no-restore` | Exit 0; verification produced no diagnostics. |
| Locked restore | `dotnet restore DevForge.sln --locked-mode` | Exit 0; all 11 projects up-to-date. |
| Release build | `dotnet build DevForge.sln --configuration Release --no-restore` | Exit 0; 11 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln --configuration Release --no-build` | Exit 0; UnitTests 229 passed and BlueprintTests 76 passed; 0 failed, 0 skipped. Integration and E2E remain empty future-milestone hosts. |
| Focused UnitTests | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-build --no-restore` | Exit 0; 229 passed, 0 failed, 0 skipped. |
| Focused BlueprintTests | `dotnet test tests/DevForge.BlueprintTests/DevForge.BlueprintTests.csproj --configuration Release --no-build --no-restore` | Exit 0; 76 passed, 0 failed, 0 skipped. |

## Environment

Verification used the workspace-local SDK at `.tools/dotnet/dotnet.exe` with `DOTNET_CLI_HOME=.tools/dotnet-cli-home`, `NUGET_PACKAGES=.tools/nuget-packages`, and telemetry, logo, and first-time experience disabled. Test execution ran outside the filesystem sandbox because the Windows test host stalls on the sandbox's named-pipe boundary.

## Known limitations

- The Windows GitHub Actions workflow mirrors the mandatory quality gates, but CI was not run remotely in this task.
- Integration, blueprint implementation, and end-to-end workflows remain assigned to later milestones; IntegrationTests and E2ETests are intentionally empty hosts.
- No Application port has an Infrastructure implementation yet.

## Milestone progression

M2 is active. M3 - Core Infrastructure may begin only after the M2 fresh/upgrade/backup-recovery, privacy, repository, build, and test gates pass and this document is updated with exact evidence.
