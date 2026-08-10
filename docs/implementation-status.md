# DevForge Studio Implementation Status

**Current milestone:** M2 - Persistence
**Status:** Complete
**Last updated:** 2026-08-10

## Current M2 scope

M2 implements EF Core SQLite metadata persistence, the required schema and migrations, privacy-safe settings/metadata repositories, the existing run-journal port, and migration backup/restore. The approved design is `docs/superpowers/specs/2026-08-10-m2-persistence-design.md`; the executable TDD plan is `docs/superpowers/plans/2026-08-10-m2-persistence.md`; the architectural decision is ADR-0004.

M2 is complete locally: exact EF Core SQLite dependencies, guarded Application persistence contracts, two versioned migrations, short-lived metadata repositories, the atomic run journal, recoverable migration coordination, and privacy/concurrency hardening all passed the exit gate. M3 process/file-system infrastructure, planner/orchestrator behavior, UI, Git/GitHub, and blueprint expansion remain out of scope.

## M2 progress

- Package ownership is pinned centrally, including the direct non-vulnerable SQLite native bundle dependency.
- Fresh and sequential SQLite migrations preserve historical data and create the required tables, foreign keys, and lookup indexes.
- Settings, IDE, environment-tool, blueprint, team-profile, preset, and recent-project repositories use one short-lived context per operation and return detached immutable snapshots.
- Repository integration tests cover round-trip/upsert/removal, pre-cancelled writes, detached snapshots, non-canonical stored enums, and scrubbed fail-closed corruption handling.
- The run journal atomically replaces immutable run/attempt/error snapshots and rehydrates only through Domain factories; tests cover deterministic ordering, failed/redacted diagnostics, invalid status, normalized duplicate attempts, and secret-shaped stored data.
- The migration coordinator creates guarded SQLite online backups before upgrades, verifies integrity, restores on migration/integrity failure, preserves recovery artifacts when restore fails, and restores before propagating post-mutation cancellation.
- Raw SQLite audits find no forbidden credential, `.env`, connection-string, database-path, source, or raw-output fixtures. Concurrent reads use independent contexts; conflicting AppSettings writes are serialized and converge on the newest timestamp with a canonical tie-break.

## M2 exit gate evidence

Fresh local verification on 2026-08-10 used the workspace-local .NET SDK 10.0.302 and completed in this order:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all 11 projects restored from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln --configuration Release --no-restore --verbosity minimal` | Exit 0; all 11 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln --configuration Release --no-build` | Exit 0; UnitTests 280, BlueprintTests 76, IntegrationTests 27; total 383 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Focused UnitTests | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-build --no-restore` | Exit 0; 280 passed, 0 failed, 0 skipped. |
| Focused IntegrationTests | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore` | Exit 0; 27 passed, 0 failed, 0 skipped. |
| EF model consistency | `dotnet-ef migrations has-pending-model-changes ... --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |

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

## M1 exit gate evidence

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
- The E2E host remains empty because end-to-end desktop workflows belong to later milestones.
- SQLite's online backup API is synchronous during the copy itself; cancellation is honored immediately before and after the bounded call, and recovery always completes before post-mutation cancellation is propagated.
- Production startup composition and safe-mode UI remain assigned to M6.

## Milestone progression

M2 is complete. The recommended next milestone is M3 - Core Infrastructure, limited to the process runner, guarded file-system workspace, secret-scanner integration, environment detection, and the other Infrastructure implementations defined by the specification.
