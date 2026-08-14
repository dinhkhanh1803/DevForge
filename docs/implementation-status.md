# DevForge Studio Implementation Status

**Current milestone:** M7 - Dynamic Create Project, Plan Preview, Execution Center, and LocalReady UX
**Status:** M0-M7 complete and locally verified
**Last updated:** 2026-08-14

## Current M7 scope

The approved design is `docs/superpowers/specs/2026-08-13-m7-create-execute-ux-design.md` and the executable TDD plan is `docs/superpowers/plans/2026-08-13-m7-create-execute-ux.md`. M7 is limited to the reviewed-plan WPF workflow over the existing M4 catalog/planner and M5 orchestration/recovery boundaries.

The recommended four-stage flow is Configure -> Review Plan -> Execute -> LocalReady. Git/GitHub controls remain disabled until M8; M7 never labels `LocalReady` as Domain `Completed`. Blueprint Catalog is a presentation of the existing catalog only. A temporary E2E fixture proves no-terminal generation without becoming a production blueprint; the three MVP production blueprints remain M9.

M7 is complete locally. Application owns guarded creation and exact recovery workflows; Desktop renders immutable snapshots and retains no workspace handles. All target operations use guarded workspace ports, all execution uses the M5 orchestrator/recovery boundary, every relevant edit invalidates the reviewed plan, progress is bounded and redacted, presets reject credential/`.env`/source content, and safe mode refuses every mutating route and action.

The real M7 E2E fixture exercises all four input kinds, guarded create/render/copy handlers, file/content validators, secret scanning, canonical reports, finalization, durable cancellation, and duplicate-free resume to `LocalReady` without invoking a terminal or process/package handler. Architecture, privacy, behavior, accessibility, target-state, recovery, and no-overwrite matrices are closed. ADR-0012 records the final boundary; M8 is the recommended next milestone.

## M7 final exit gate

Fresh local verification on 2026-08-14 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| SDK | `dotnet --version` | Exit 0; `10.0.302`. |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all 12 projects restored from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore --verbosity minimal -m:1` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution | `dotnet test DevForge.sln -c Release --no-build --no-restore --verbosity minimal -m:1` | Exit 0; UnitTests 545, IntegrationTests 386, BlueprintTests 108, E2ETests 140; total 1,179 passed, 0 failed, 0 skipped. |
| Focused creation/architecture/privacy | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Creation\|FullyQualifiedName~Architecture\|FullyQualifiedName~Privacy" -m:1` | Exit 0; 127 passed, 0 failed, 0 skipped. |
| Focused creation/execution/blueprints | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Creation\|FullyQualifiedName~Execution\|FullyQualifiedName~Blueprints" -m:1` | Exit 0; 215 passed, 0 failed, 0 skipped. |
| Focused Desktop/M7 | `dotnet test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Desktop\|FullyQualifiedName~M7" -m:1` | Exit 0; 140 passed, 0 failed, 0 skipped. |
| EF model consistency | `dotnet-ef migrations has-pending-model-changes --project src/DevForge.Infrastructure --startup-project src/DevForge.Infrastructure --context DevForgeDbContext --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |
| Diff check | `git diff --check` | Exit 0; no whitespace errors. |

The first EF invocation could not locate the workspace runtime because `DOTNET_ROOT` was absent from that child process. Re-running the same model-consistency command with `DOTNET_ROOT=E:\MyProjects\DevForge\.tools\dotnet` exited 0 with no pending model changes. No product code or model change was required.

## Current M6 scope

The approved design is `docs/superpowers/specs/2026-08-12-m6-desktop-shell-settings-environment-doctor-design.md`, the executable TDD plan is `docs/superpowers/plans/2026-08-12-m6-desktop-shell-settings-environment-doctor.md`, and ADR-0010/0011 fix the native WPF host, theme, startup, cache, and safe-mode decisions.

M6 provides a Generic Host composition root; migration-first and recovery-first startup; persisted typed settings; Settings onboarding; System/Light/Dark theming; a 15-minute cached Environment Doctor; Dashboard aggregation; bounded notifications; and a persistent left-rail WPF shell. Functional routes are Dashboard, Environment Doctor, and Settings. Create Project, Projects, and Blueprint Catalog are present only as clearly disabled M7 destinations. Desktop presentation code does not directly access files, processes, EF Core, or Infrastructure implementations.

## M6 final exit gate

Fresh local verification on 2026-08-12 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| SDK | `dotnet --version` | Exit 0; `10.0.302`. |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all 12 projects restored from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore --verbosity minimal -m:1` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution | `dotnet test DevForge.sln -c Release --no-build --no-restore --verbosity minimal -m:1` | Exit 0; UnitTests 496, IntegrationTests 372, BlueprintTests 108, E2ETests 63; total 1,039 passed, 0 failed, 0 skipped. |
| Focused Desktop | `dotnet test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-build --no-restore --filter FullyQualifiedName~Desktop --verbosity minimal -m:1` | Exit 0; 63 passed, 0 failed, 0 skipped. |
| EF model consistency | `.tools/dotnet-tools/dotnet-ef.exe migrations has-pending-model-changes --project src/DevForge.Infrastructure --startup-project src/DevForge.Infrastructure --context DevForgeDbContext --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |

M6 coverage includes Generic Host start/stop and fixed startup ordering; real SQLite migration/settings/environment-cache round trips; interrupted-run normalization before navigation; safe-mode write/scan refusal; closed navigation; settings validation; System/Light/Dark switching; exact cache TTL/concurrency; Dashboard empty/unavailable/action states; privacy and architecture boundaries; keyboard/accessibility metadata; and WPF resource/measure smoke at 960x640, 1200x800, and 1440x960. M7 is the next specification milestone.

## Current M5 scope

The approved design is `docs/superpowers/specs/2026-08-11-m5-recoverable-orchestration-design.md`, the executable TDD plan is `docs/superpowers/plans/2026-08-11-m5-recoverable-orchestration.md`, and ADR-0008 fixes checkpoint, marker, retry/resume, interruption, validation, and finalization decisions. M5 is limited to the execution engine and recovery boundary. WPF composition, Git/GitHub, production blueprints, release packaging, and catalog expansion remain deferred.

M5 is complete locally. Domain provides explicit bounded retry modes, canonical attempt output digests, interruption normalization, guarded Planning/idle-Executing resume, staging-cleanup eligibility, warning validation/report evidence, retry-mode-aware plan hashing, and deterministic template context snapshots. Application owns a process-wide checkpointed orchestrator with plan-first persistence, ordered handler phases, durable cancellation, bounded automatic/manual retry, postcondition-driven resume, exact persisted-mode validation, completion coordination, progress isolation, and authoritative startup recovery under the same activity gate. Infrastructure persists canonical checkpoints, manages atomic run-owned staging, reopens exact verified M4 blueprint bytes, dispatches only the closed trust-scoped handler set, executes guarded file/process actions, writes bounded privacy-safe reports, and performs marker-verified no-overwrite finalization. Replay and finalized cleanup remove exact marker-owned siblings. M6 WPF shell, settings, and environment doctor is the recommended next milestone.

## M5 final exit gate

Fresh local verification on 2026-08-11 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| SDK | `dotnet --version` | Exit 0; `10.0.302`. |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all projects were up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution | `dotnet test DevForge.sln -c Release --no-build --no-restore --verbosity minimal -m:1` | Exit 0; UnitTests 495, BlueprintTests 108, IntegrationTests 369; total 972 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Focused M5 unit/security | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Application.Execution\|FullyQualifiedName~RecoverableExecution\|FullyQualifiedName~ProcessSecurityReviewTests\|FullyQualifiedName~PrivacyTests"` | Exit 0; 164 passed, 0 failed, 0 skipped. |
| Focused M5 integration/security | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Infrastructure.Execution\|FullyQualifiedName~Infrastructure.Security\|FullyQualifiedName~Infrastructure.Processes\|FullyQualifiedName~Infrastructure.FileSystem"` | Exit 0; 182 passed, 0 failed, 0 skipped. |
| EF model consistency | `dotnet-ef migrations has-pending-model-changes ... --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |

All M5 kill/resume, failure-injection, target-absence, no-overwrite finalization, marker ownership, report/checkpoint ordering, and zero-skipped gates pass. Solution test projects are serialized with `-m:1` because unrelated fail-closed regex and 300 ms process-output boundary tests are intentionally sensitive to cross-host CPU contention; both were also rerun individually and passed without product changes.

## M5 Task 11 checkpoint evidence

Fresh local verification on 2026-08-11 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all projects were up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln -c Release --no-build --no-restore --verbosity minimal -m:1` | Exit 0; UnitTests 495, BlueprintTests 108, IntegrationTests 369; total 972 passed, 0 failed, 0 skipped. The future E2E host contains no tests. Project serialization avoids unrelated cross-host timing contention in the process-output and fail-closed regex boundary tests. |
| Focused Task 11 unit | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~RunRecoveryServiceTests\|FullyQualifiedName~CheckpointedExecutionOrchestratorTests\|FullyQualifiedName~RecoverableExecutionContractTests"` | Exit 0; 50 passed, 0 failed, 0 skipped. |
| Focused Task 11 integration | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-restore --filter "FullyQualifiedName~AppKillRecoveryResumesThroughRealOwnershipAndRefusesTamperedCleanup\|FullyQualifiedName~StartupRecoveryDurablyClosesInterruptedAttemptAndIsIdempotent\|FullyQualifiedName~BlueprintExecutionSourceTests\|FullyQualifiedName~OwnedStagingWorkspaceManagerTests"` | Exit 0; 35 passed, 0 failed, 0 skipped. |

Task 11 coverage includes authoritative checkpoint reload, stale-snapshot refusal, shared execution/recovery exclusion, idempotent startup scans, durable SQLite interruption normalization, cancelled and validation-failed resume, exact ownership validation, unavailable exact-blueprint remediation, no duplicate attempts, marker-tampered cleanup refusal, cancellation, and preservation of the finalized checkpoint when only staging cleanup remains. Independent static review found no remaining Critical or Important findings.

## M5 Task 10 checkpoint evidence

Fresh local verification on 2026-08-11 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode` | Exit 0; all projects were up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln -c Release --no-build --no-restore` | Exit 0; UnitTests 484, BlueprintTests 108, IntegrationTests 367; total 959 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Focused Task 10 unit | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj -c Release --no-restore --filter "FullyQualifiedName~ValidatedRunCompletionCoordinatorTests\|FullyQualifiedName~CheckpointedExecutionOrchestratorTests\|FullyQualifiedName~RecoverableExecutionContractTests"` | Exit 0; 49 passed, 0 failed, 0 skipped. |
| Focused Task 10 integration | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-restore --filter "FullyQualifiedName~CanonicalGenerationReportWriterTests\|FullyQualifiedName~OwnedStagingWorkspaceManagerTests"` | Exit 0; 35 passed, 0 failed, 0 skipped. |

Task 10 coverage includes required/optional validator ordering, scanner findings and operational failures, durable cancellation, finalization/report failure retention, the non-dispatched finalization boundary, lease release before cleanup, target collision, marker tampering/missing markers, same-volume source binding, exact ordinal detached tree/hash verification, injected copy corruption, explicit payload bounds, scrubbed guarded-I/O failures, report privacy/completeness, and exact canonical/replay/previous cleanup. Independent static review found no remaining Critical or Important findings.

## M5 Task 9 checkpoint evidence

Fresh local verification on 2026-08-11 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all projects were up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics after CRLF normalization. |
| Release build | `dotnet build DevForge.sln --configuration Release --no-restore --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln --configuration Release --no-build --no-restore --verbosity minimal` | Exit 0; UnitTests 472, BlueprintTests 108, IntegrationTests 354; total 934 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Focused Task 9 unit | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~CheckpointedExecutionOrchestratorTests\|FullyQualifiedName~RetryDecisionEngineTests\|FullyQualifiedName~ProjectPlannerTests\|FullyQualifiedName~RecoverableExecutionModelTests\|FullyQualifiedName~RecoverableExecutionContractTests"` | Exit 0; 83 passed, 0 failed, 0 skipped. |
| Focused Task 9 integration | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~OwnedStagingWorkspaceManagerTests\|FullyQualifiedName~ClosedExecutionHandlerRegistryTests"` | Exit 0; 38 passed, 0 failed, 0 skipped. |

Task 9 coverage includes Planning/Executing checkpoint order, all handler phases, attempt evidence, transient-only retry decisions, explicit manual retry, cancellation during handlers/blueprint/retry delay, process-wide exclusion, observer isolation, request/checkpoint mode binding, missing or changed ownership/blueprints, postcondition skip/drift, declared-output cleanup, opaque fresh-staging replay, interrupted replay-swap recovery, and initial-journal failure cleanup. Independent static review found no remaining Critical or Important findings.

## M5 Task 8 checkpoint evidence

Fresh local verification on 2026-08-11 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all projects were up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln --configuration Release --no-restore --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln --configuration Release --no-build --no-restore --verbosity minimal` | Exit 0; UnitTests 433, BlueprintTests 108, IntegrationTests 350; total 891 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Focused Task 8 unit | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ProcessContractTests\|FullyQualifiedName~ProcessSecurityReviewTests\|FullyQualifiedName~RecoverableExecutionContractTests"` | Exit 0; 61 passed, 0 failed, 0 skipped. |
| Focused Task 8 integration | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ProcessExecutionHandlerTests\|FullyQualifiedName~WindowsProcessRunnerTests\|FullyQualifiedName~ProcessOutputRedactionTests\|FullyQualifiedName~BlueprintActionPolicyTests"` | Exit 0; 89 passed, 0 failed, 0 skipped. |

Task 8 coverage includes plan-item ownership; handler-kind separation; executable/argument/environment bounds; all-position raw-mode rejection; guarded root and relative working directories; trusted tool preflight; allowed/disallowed exits; timeout and cancellation; bounded redacted progress and deterministic digests; validator revalidation; package-manager and lifecycle-script policy; production Node/script-prefix resolution without shell execution; and fail-closed fresh-staging replay for mutating process retry/recovery. Task 9 must replace owned staging and replay the immutable plan when it sees `ReplayFromFreshStaging`.

## M5 Task 7 checkpoint evidence

Fresh local verification on 2026-08-11 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all projects were up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln --configuration Release --no-restore --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln --configuration Release --no-build --no-restore --verbosity minimal` | Exit 0; UnitTests 429, BlueprintTests 108, IntegrationTests 320; total 857 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Focused Task 7 unit | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~ProjectPlannerTests\|FullyQualifiedName~RecoverableExecutionContractTests\|FullyQualifiedName~TemplateRenderRequestTests"` | Exit 0; 28 passed, 0 failed, 0 skipped. |
| Focused Task 7 integration | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~FileExecutionHandlerTests\|FullyQualifiedName~WindowsWorkspaceFileSystemTests.AtomicFileWrite\|FullyQualifiedName~RunCheckpointStoreTests.PlanRoundTrips"` | Exit 0; 24 passed, 0 failed, 0 skipped. |

Task 7 coverage includes create/render/copy lifecycle and postconditions; JSON/YAML/XML set/remove idempotence; JSON duplicates; YAML aliases, anchors, tags, merge keys, and duplicate mappings; XML DTD/entities, namespaces, and invalid names; traversal and exact `.env` rejection with `.env.example` allowed; locked files; cancellation and size bounds; secret-shaped destination keys; declared-output retry cleanup; deterministic context hashing/persistence; and plan-owned handler requests. Development checkpoints written before template context entered the plan hash intentionally fail closed and must be replanned.

Task 4 focused coverage includes target directories and files, target and payload junctions, nested reparse entries, copied/spoofed/noncanonical/malformed markers, cross-run lease contention, cleanup/reopen contention, mid-write cancellation, finalized cleanup refusal, exact run-path binding, and atomic ownership-loss preservation. Final checkpoint evidence is recorded after the fresh Task 4 quality gate below.

## M5 Task 6 checkpoint evidence

Fresh local verification on 2026-08-11 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all projects were up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln --configuration Release --no-restore --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln --configuration Release --no-build --no-restore --verbosity minimal` | Exit 0; UnitTests 428, BlueprintTests 108, IntegrationTests 296; total 832 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Focused registry/materializer | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~ClosedExecutionHandlerRegistryTests\|FullyQualifiedName~RuntimePlanValueMaterializerTests"` | Exit 0; 23 passed, 0 failed, 0 skipped. |

Task 6 coverage includes the full handler-ID/trust matrix, required BuiltIn finalization injection, deferred integration failures, registration snapshots, duplicate/missing/unknown maps, recursive typed replacement, unavailable target placeholders, malformed sentinel maps, aggregate bounds, privacy, cancellation, literal non-reparsing, and source checks forbidding reflection or direct process dispatch. Task 9 must select the registry from the reopened blueprint's exact trust and must never reuse a BuiltIn registry for TrustedLocal execution.

## M5 Task 5 checkpoint evidence

Fresh local verification on 2026-08-11 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all 12 projects restored from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln --configuration Release --no-restore --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln --configuration Release --no-build --no-restore --verbosity minimal` | Exit 0; UnitTests 428, BlueprintTests 108, IntegrationTests 273; total 809 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Focused exact-reopen/catalog regression | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-restore --filter "FullyQualifiedName~BlueprintExecutionSourceTests\|FullyQualifiedName~BlueprintPackageLoaderTests\|FullyQualifiedName~BlueprintCatalogTests"` | Exit 0; 25 passed, 0 failed, 0 skipped. |

Task 5 coverage includes exact source/package/reference/fingerprint matching, changed and missing packages, built-in disablement, local trust revocation before and during capture, cancellation before and after the final verified read, immutable verified-byte reopening, read-only mutation rejection, and absolute-path redaction.

## M5 Task 4 checkpoint evidence

Fresh local verification on 2026-08-11 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all 12 projects were up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics after CRLF normalization. |
| Release build | `dotnet build DevForge.sln --configuration Release --no-restore --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln --configuration Release --no-build --no-restore --verbosity minimal` | Exit 0; UnitTests 428, BlueprintTests 108, IntegrationTests 264; total 800 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Focused staging/atomic integration | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~OwnedStagingWorkspaceManagerTests\|FullyQualifiedName~AtomicDirectoryCreation"` | Exit 0; 21 passed, 0 failed, 0 skipped. |
| Focused filesystem/execution contracts | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~RecoverableExecutionContractTests\|FullyQualifiedName~FileSystemContractTests"` | Exit 0; 29 passed, 0 failed, 0 skipped. |

## M4 completed scope

M0-M4 are complete and green. The approved M4 design is `docs/superpowers/specs/2026-08-10-m4-planner-rules-blueprint-catalog-design.md`, the completed TDD plan is `docs/superpowers/plans/2026-08-11-m4-planner-rules-blueprint-catalog.md`, and ADR-0007 records the deterministic catalog/trust/rule/hash boundary.

All ten M4 tasks are complete. Domain provides bounded immutable typed plan values plus ordered execution validators; Blueprint abstractions provide SemVer 2.0 and normalized package contracts; Application provides exact catalog/planning contracts, the closed compatibility engine, effective-input validation, single-pass typed variables, deterministic planner orchestration, enriched privacy-safe previews, canonical UTF-8 serialization, and lowercase SHA-256 hashes. Infrastructure owns exact YamlDotNet 18.1.0, bounded closed readers, verified control-byte snapshots, guarded checksums/action policy, normalized package loading, and atomic catalog publication. Invalid, conflicting, disabled, untrusted, changed-checksum, traversal, `.env`, oversized, junction-escaping, malformed-rule, and unsafe packages remain inspect-only with stable scrubbed issues. M4 adds no production blueprint and executes no generation action.

Plan hashes include every tested effect-bearing structural policy and exclude target-machine roots, staging/run paths, timestamps, detected tool versions/paths, and warning outcomes. Exact snapshot, culture, map-order, concurrent-planning, mutation, privacy, cancellation, aggregation, and no-direct-I/O/process tests pass.

## M4 exit gate evidence

Fresh local verification on 2026-08-11 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| SDK | `dotnet --version` | Exit 0; `10.0.302`. |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --verbosity minimal` | Exit 0; all 12 projects restored from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln --configuration Release --no-restore --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full solution test | `dotnet test DevForge.sln --configuration Release --no-build --no-restore` | Exit 0; UnitTests 395, BlueprintTests 108, IntegrationTests 233; total 736 passed, 0 failed, 0 skipped. The future E2E host contains no tests. |
| Blueprint contracts | `dotnet test tests/DevForge.BlueprintTests/DevForge.BlueprintTests.csproj --configuration Release --no-build --no-restore` | Exit 0; 108 passed, 0 failed, 0 skipped. |
| Planning/Blueprint/Architecture | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~Planning\|FullyQualifiedName~Blueprint\|FullyQualifiedName~Architecture"` | Exit 0; 117 passed, 0 failed, 0 skipped. |
| Blueprint integration/security | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~Blueprints` | Exit 0; 82 passed, 0 failed, 0 skipped. |

The next specification milestone is M5. Execution, staging, retry/resume, rollback/finalization, and run orchestration are now the active scope.

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
- The M7 E2E package is test-only; the three production MVP blueprints remain assigned to M9.
- Environment probing and IDE launch support only the fixed trusted tool catalog; arbitrary executable discovery remains intentionally unsupported.
- SQLite's online backup API is synchronous during the copy itself; cancellation is honored immediately before and after the bounded call, and recovery always completes before post-mutation cancellation is propagated.
- Git/GitHub completion remains disabled until M8; successful M7 runs stop at `LocalReady`.
- Support bundles, production log browsing, Open Staging/folder handoff, packaging, and release hardening remain assigned to M10.
- Guarded Windows operations remain path-based. M5's owned staging, process-wide lease, detached verified packages, and closed sequential handlers exclude an in-scope blueprint actor from racing ancestor replacement; a future threat model that includes a separate hostile same-user process requires handle-relative no-follow native operations.

## Milestone progression

M8 is now active. Its independently reviewed implementation boundary is `docs/superpowers/specs/2026-08-14-m8-git-github-publishing-design.md` and its task-level TDD plan is `docs/superpowers/plans/2026-08-14-m8-git-github-publishing.md`. The scope is limited to reviewed Git intent, guarded post-finalization Git/`gh` operations, integrity-bound publication persistence, `PublishPending` recovery, and evidence-backed `Completed`. Production blueprints remain M9; CI/release hardening remains M10; catalog expansion remains M11.
