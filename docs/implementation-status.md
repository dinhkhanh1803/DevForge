# DevForge Studio Implementation Status

**Current milestone:** M4 - Planner, Rules, and Blueprint Catalog
**Status:** Active implementation; M4 Tasks 1-4 complete
**Last updated:** 2026-08-11

## Current M4 scope

M0-M3 remain complete and green. M4 is now the only active milestone. The approved design is `docs/superpowers/specs/2026-08-10-m4-planner-rules-blueprint-catalog-design.md`, the executable TDD plan is `docs/superpowers/plans/2026-08-11-m4-planner-rules-blueprint-catalog.md`, and ADR-0007 fixes deterministic catalog/trust/rule/hash decisions before production code.

M4 Tasks 1-4 are complete. Domain has bounded immutable typed plan values; Blueprint abstractions provide SemVer 2.0 and normalized package contracts; Application exposes opaque catalog/planning contracts; Infrastructure now owns exact YamlDotNet 18.1.0 and bounded closed YAML/JSON control readers. Parser fixtures reject duplicate/unknown fields, anchors, aliases, merge keys, tags, non-scalar keys, unsupported/remote JSON Schema features, malformed UTF-8, excessive scalar/depth/file sizes, and return only scrubbed stable issues. The latest Task 4 checkpoint passed locked restore, format verification, Release build with 0 warnings/errors, and 599 solution tests (324 Unit, 108 Blueprint, 167 Integration) with 0 failed/0 skipped; the future E2E host remains empty. Tasks 5-10 and the full M4 exit gate remain open. M5-M11 remain deferred until the M4 exit gate passes.

## Current M3 scope

The original five-boundary M3 checkpoint was green, but the full DOCX audit found one omitted M3 exit item: the restricted Scriban template renderer. The renderer implementation, contract hardening, exact dependency pin, closed AST policy, focused security tests, and fresh six-boundary exit gate now pass. Its approved design is `docs/superpowers/specs/2026-08-10-m3-restricted-template-renderer-closure-design.md`, its executable TDD plan is `docs/superpowers/plans/2026-08-11-m3-restricted-template-renderer-closure.md`, and the accepted runtime boundary is ADR-0006.

Delivered scope now includes the guarded workspace file system, trusted process runner, bounded secret scanner, typed environment doctor, trusted IDE handoff, the restricted Scriban renderer, and their unit/integration/security tests. Planner/catalog, orchestration, UI composition, production templates, Git, GitHub, packaging, cloud backends, and AI APIs remain out of scope.

## M3 delivered

- Workspace operations resolve canonical relative paths under an opaque root, reject reparse escapes, require explicit destructive intent, and preserve no-overwrite move semantics.
- Process execution resolves only typed trusted tools, uses `ArgumentList` with no shell or elevation, drains bounded redacted stdout/stderr, and terminates the complete child tree on timeout or cancellation.
- Secret scanning is bounded by file size, line length, text extensions, and regex timeouts; findings contain only categories, guarded paths, line numbers, and redacted descriptions.
- Environment inspection runs fixed probes for the supported M3 tools. IDE launch accepts only the closed trusted catalog and performs a non-elevated detached handoff.
- Template rendering uses Scriban 7.2.5 only inside Infrastructure, an empty strict runtime, frozen string-only context objects, a closed conditional grammar, bounded AST/output, cancellation, and stable scrubbed failures.
- Adversarial tests exercise real Windows processes and junctions, structured JSON/XML credentials, argument metacharacters, locked files, throwing progress observers, continuous-output cancellation, and privacy-safe failure paths with no skipped Infrastructure test.

## M3 final six-boundary exit gate evidence

Fresh local verification on 2026-08-11 used the workspace-local .NET SDK 10.0.302 and completed in this order:

| Gate | Command | Exact result |
| --- | --- | --- |
| SDK | `dotnet --version` | Exit 0; `10.0.302`. |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all projects were up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln --configuration Release --no-restore --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln --configuration Release --no-build --no-restore` | Exit 0; UnitTests 304, BlueprintTests 76, IntegrationTests 149; total 529 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Renderer contract/privacy | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~TemplateRenderRequestTests\|FullyQualifiedName~TemplateRendererDependencyTests\|FullyQualifiedName~PrivacyTests"` | Exit 0; 40 passed, 0 failed, 0 skipped. |
| Renderer integration/security | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~Templates` | Exit 0; 58 passed, 0 failed, 0 skipped. |
| Focused Unit architecture/Infrastructure | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~Infrastructure\|FullyQualifiedName~Architecture"` | Exit 0; 42 passed, 0 failed, 0 skipped. |
| Focused Infrastructure integration | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~Infrastructure` | Exit 0; 122 passed, 0 failed, 0 skipped. |

## Historical five-boundary checkpoint

The table below is historical evidence for the original five-boundary checkpoint on 2026-08-10. It does not include the restricted template renderer and is not the final six-boundary M3 exit gate.

Fresh local verification on 2026-08-10 used the workspace-local .NET SDK 10.0.302 and completed in this order:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; 12 projects restored or confirmed up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln --configuration Release --no-restore --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln --configuration Release --no-build --no-restore` | Exit 0; UnitTests 289, BlueprintTests 76, IntegrationTests 91; total 456 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Focused Unit security | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~Infrastructure\|FullyQualifiedName~Architecture"` | Exit 0; 40 passed, 0 failed, 0 skipped. |
| Focused Infrastructure integration | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~Infrastructure` | Exit 0; 64 passed, 0 failed, 0 skipped. |

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
- Environment probing and IDE launch support only the fixed M3 catalog; catalog compatibility and planning belong to M4.
- SQLite's online backup API is synchronous during the copy itself; cancellation is honored immediately before and after the bounded call, and recovery always completes before post-mutation cancellation is propagated.
- Production startup composition and safe-mode UI remain assigned to M6.

## Milestone progression

The recommended next milestone is M4 - Planner, Rules, and Blueprint Catalog, limited to catalog loading, compatibility evaluation, deterministic planner rules, and plan hashing defined by the specification.
