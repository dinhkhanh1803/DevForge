# DevForge Studio Implementation Status

**Current milestone:** M10 - Security, diagnostics, packaging, and release hardening (Task 1 review findings resolved; Task 2 next)
**Status:** M0-M8 complete; M9 implementation and Windows 10 matrix complete; M9 Windows 11 certification is accepted carry-forward environmental debt; M10 Task 1 affected gates are green
**Last updated:** 2026-08-26

## M10 entry and current scope

The owner explicitly requested M10 after the outstanding M9 Windows 11 certification had been recorded. Under section 18.1 of the detailed baseline, that environmental gate is carried as accepted technical debt; it is not reported green, M9 remains open, and M10 cannot close until the real Windows 11 evidence exists.

The approved design is `docs/superpowers/specs/2026-08-26-m10-security-diagnostics-packaging-release-hardening-design.md`; the executable TDD plan is `docs/superpowers/plans/2026-08-26-m10-security-diagnostics-packaging-release-hardening.md`. M10 is limited to security closure, structured local diagnostics and retention, privacy-safe support bundles/cleanup, Desktop accessibility/scaling, self-contained `win-x64` packaging/upgrade, documentation, and release evidence. M11 catalog expansion remains excluded.

M10 Task 1 is complete locally. Root provisioning is a typed `IFileSystem` operation; `LocalDataRootProvisioner` delegates without direct IO and maps expected port failures to scrubbed `DF-FS-001` while cancellation remains cancellation. The Windows implementation provisions one component at a time and holds verified non-reparse directory handles without delete sharing until `WorkspacePathGuard.Open` succeeds. This closes the check/create race instead of merely detecting an escape after an outside mutation. Blueprint action policy validates `executable` and `packageManager` against `ExecutableIdentity`, so PowerShell, cmd, bash, curl, msiexec, and every unknown identity fail during production package loading rather than reaching planning or execution. The consolidated loader fixture proves rejected hostile actions return no package and attempt zero workspace mutations. Architecture scans now include direct reads, opens, existence checks, and directory enumeration outside Infrastructure; WPF temporary-project suppression requires a canonical sibling and no longer hides arbitrary real `_wpftmp` projects.

Task 1 TDD evidence observed the missing injected provisioner/port as compile RED, six untrusted executable cases as behavioral RED, a junction-ancestor provisioning escape that created outside the requested root as RED, provisioner error-code leakage as RED, and over-broad `_wpftmp` suppression as RED. Review then identified the remaining same-user TOCTOU window; its regression proves an ancestor cannot be renamed while the provisioning lease is held and can be renamed after disposal. The fresh Release build passes all 12 projects with 0 warnings/errors. Full project results are Unit 636 passed, Integration 566 passed, and Blueprint 127 passed, all with 0 failed/skipped. Scoped format verification exits 0 with no output.

The authoritative full E2E gate is currently blocked by concurrent Desktop presentation work not owned by Task 1. The current run reports 187 passed and three RED presentation-contract cases: missing `EmptyState` in Dashboard and Blueprint Catalog, plus missing `StatusBadge` in Run History. Those files are preserved outside the Task 1 commit. This does not invalidate the Task 1 Unit/Integration/Blueprint and Release-build evidence, but the release gate remains red until the presentation owner completes the views.

## M8 final scope and closure

M8 is complete locally. Reviewed Git intent is immutable across Create Project, preset, recipe, preview, canonical plan hash, and checkpoint. Production local Git and optional fixed-account personal GitHub publication run only through closed typed `IProcessRunner` operations with isolated configuration/credentials, private-by-default visibility, ownership nonce, durable phase checkpoints, exact recovery, atomic receipts, and safe-mode refusal. Failure remains `PublishPending`; retry never invokes generation or duplicates the initial commit.

The Task 8 fixture composes the trusted M7 blueprint pipeline, SQLite checkpoint store, guarded Windows workspaces, production local Git service, publication coordinator, cross-process lease, and atomic receipt store. It proves generation and validation through `LocalReady`, an exact clean local Git repository and `Completed`, then a second full verification from durable evidence. Its deterministic GitHub boundary proves private publication interruption to `PublishPending` and successful retry with identical generation evidence, commit, branch policy, and persisted nonce. Focused existing matrices cover every durable coordinator phase, real local Git `init`/`add`/`commit`/`develop` kill window, nonce-owned empty/partial/complete remotes, exact orphan receipt adoption/refusal, cross-process contention, non-overwrite, architecture, and privacy. Automated tests did not create, mutate, or contact a real GitHub repository.

ADR-0014 records the final recoverable completion decision; ADR-0013 remains the narrower fixed GitHub CLI credential-handoff decision.

## M8 final exit gate

Fresh local verification on 2026-08-25 used workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --force-evaluate --no-cache -m:1 --verbosity minimal` | Exit 0; all 12 projects restored from pinned lock files. |
| Format | `dotnet format DevForge.sln --no-restore` then `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; new C# lines normalized to repository CRLF policy; final verification produced no diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 -nodeReuse:false --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Focused M8 E2E | `dotnet test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter "FullyQualifiedName~DevForge.E2ETests.M8.ProjectPublicationE2ETests"` | Exit 0; 2 passed, 0 failed, 0 skipped. |
| Focused publication/architecture/privacy | `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter "FullyQualifiedName~Application.Publication\|FullyQualifiedName~Architecture\|FullyQualifiedName~Privacy"` | Exit 0; 139 passed, 0 failed, 0 skipped. |
| Focused Git/GitHub/publication/persistence/privacy | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter "FullyQualifiedName~Infrastructure.Git\|FullyQualifiedName~Infrastructure.GitHub\|FullyQualifiedName~Infrastructure.Publication\|FullyQualifiedName~CheckpointPublicationCodec\|FullyQualifiedName~RunCheckpointStore\|FullyQualifiedName~PersistencePrivacy"` | Exit 0; 108 passed, 0 failed, 0 skipped. |
| Full tests | Four Release project test commands with `--no-build --no-restore -m:1 -nodeReuse:false` | Exit 0; UnitTests 610, IntegrationTests 481, BlueprintTests 108, E2ETests 155; total 1,354 passed, 0 failed, 0 skipped. |
| EF model consistency | pinned SDK + local `dotnet-ef.dll migrations has-pending-model-changes --project src/DevForge.Infrastructure --startup-project src/DevForge.Infrastructure --context DevForgeDbContext --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |
| Static boundaries | `rg` scans over production and package files | No `cmd /c`, PowerShell execution, force/delete/token command, Desktop direct process/filesystem access, inline package version, wildcard/latest dependency, web shell, embedded browser, or AI/cloud integration found. |
| Diff check | `git diff --check` | Exit 0; no whitespace errors. |

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
- The M7 E2E package remains test-only. M9 has delivered the production WPF blueprint; React and Python remain pending.
- Environment probing and IDE launch support only the fixed trusted tool catalog; arbitrary executable discovery remains intentionally unsupported.
- SQLite's online backup API is synchronous during the copy itself; cancellation is honored immediately before and after the bounded call, and recovery always completes before post-mutation cancellation is propagated.
- GitHub publication is covered with deterministic typed fakes; automated tests intentionally do not exercise or mutate a real GitHub account/repository.
- Support bundles, production log browsing, Open Staging/folder handoff, packaging, and release hardening remain assigned to M10.
- Guarded Windows operations remain path-based. M5's owned staging, process-wide lease, detached verified packages, and closed sequential handlers exclude an in-scope blueprint actor from racing ancestor replacement; a future threat model that includes a separate hostile same-user process requires handle-relative no-follow native operations.

## Milestone progression

M8 is complete. Its independently reviewed implementation boundary is `docs/superpowers/specs/2026-08-14-m8-git-github-publishing-design.md`, its task-level TDD plan is `docs/superpowers/plans/2026-08-14-m8-git-github-publishing.md`, and ADR-0014 records closure. Production blueprints are the recommended M9 scope; CI/release hardening remains M10; catalog expansion remains M11.

M8 Task 1 is complete locally. The public ports now expose only closed Git bootstrap/verification and exact-account GitHub authentication/publication operations; reviewed identity is part of the canonical plan hash; publication snapshots are bounded and immutable; and `RunCheckpoint` rejects out-of-order, wrong-branch, wrong-identity, or evidence-free `Completed` states. Fresh verification passed format, Release build with 0 warnings/errors, UnitTests 563, IntegrationTests 387, BlueprintTests 108, and E2ETests 140. Task 2 adds the versioned durable publication snapshot and final-tree digest migration.

M8 Task 2 is complete locally. Successful finalization now durably records the exact final-tree digest before report persistence and `LocalReady`; every checkpoint recreation preserves publication state. Publication evidence is stored as bounded canonical UTF-8 JSON with a SHA-256 body checksum, strict duplicate/unknown-field rejection, guarded domain reconstruction, and null-paired columns. Migration `20260814025832_PersistPublicationCheckpoints` adds the nullable publication body/checksum pair so pre-M8 rows decode as non-publishable `NotRequested` state. Integration coverage round-trips all successful Git/GitHub/receipt fields through EF Core SQLite and rejects recomputed-checksum structural/state tampering, sensitive unknown fields, oversized bodies, non-canonical JSON, checksum changes, and mismatched null columns. Fresh verification passed locked restore, format, Release build with 0 warnings/errors, UnitTests 563, IntegrationTests 397, BlueprintTests 108, E2ETests 140, and EF model consistency with no pending changes. Independent review found no Critical or Important findings. Task 3 implements the closed production Git CLI service.

M8 Task 3 is complete locally. `LocalGitService` exposes only the typed bootstrap/verification port and invokes the fixed Git vocabulary through `IProcessRunner` with separated arguments, an empty inherited environment, disabled system/global config, templates, hooks, filters, pagers, prompts, credentials, signing, and line-ending mutation. It binds the parentless fixed bootstrap commit to the exact guarded final-tree path/byte set by reading loose commit/tree/blob objects, requires the loose-object set to be exactly reachable, verifies the version-2 index and cache-tree checksum/structure, and closes config/ref/reflog/branch/message evidence before accepting recovery. Kill-window adoption is limited to exact init/add/commit/develop states; nested/pre-existing repositories, extra objects/history/refs/remotes/tags/hooks/control files, dirty trees, hidden ignored files, attribute byte normalization, secrets, ambient filter execution, timeouts, and cancellation fail closed with scrubbed stable codes.

Fresh Task 3 verification on 2026-08-14 used the workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode` | Exit 0; all projects up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore` | Exit 0; 0 warnings, 0 errors. |
| Focused Git | `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-restore --filter "FullyQualifiedName~Infrastructure.Git"` | Exit 0; 33 passed, 0 failed, 0 skipped. |
| Full tests | Four Release project test commands with `--no-build --no-restore` | Exit 0; UnitTests 563, IntegrationTests 430, BlueprintTests 108, E2ETests 140; total 1,241 passed, 0 failed, 0 skipped. |
| EF model consistency | pinned SDK + local `dotnet-ef.dll migrations has-pending-model-changes ... --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |
| Review | independent read-only Task 3 review | Approved; no Critical or Important findings. |

M8 Task 4 is complete locally. `GitHubCliService` exposes only typed authentication and publication operations through `IProcessRunner`, pins `github.com`, requires the exact reviewed account, creates private repositories by default, and binds ownership to the persisted nonce. Fresh, partial, and complete remote states are reconciled from bounded typed evidence; every missing branch is preceded by a fresh local tree/secret/config verification and a final exact-account check, then pushed by immutable commit ID to the canonical HTTPS destination. Git credential handoff is limited to the fixed `gh auth git-credential` protocol with a trusted resolved `gh.exe`, isolated `GH_CONFIG_DIR`, strict shell-safe path grammar, and no token observation or logging. Empty repositories avoid the GitHub refs endpoint's ambiguous failure path; nonempty repositories require exact bounded branch evidence. Retry accepts only an absent origin or the exact canonical origin and rejects fetch/push URL drift.

Fresh Task 4 verification on 2026-08-14 used the workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --disable-build-servers` | Exit 0; all projects up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 /nodeReuse:false` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full tests | Four Release project test commands with `--no-build --no-restore` | Exit 0; UnitTests 563, IntegrationTests 475, BlueprintTests 108, E2ETests 140; total 1,286 passed, 0 failed, 0 skipped. |
| Review | independent read-only Task 4 review | Approved; no Critical or Important findings. |

M8 Task 5 is complete locally. `ProjectPublicationCoordinator` serializes publication with the shared activity gate and a guarded OS-exclusive per-run lease, reloads the authoritative checkpoint, validates immutable reviewed intent and finalized workspaces, and persists every Git, GitHub, and receipt phase with `CancellationToken.None`. Failures and cancellation remain durably recoverable as `PublishPending`; retries adopt only exact local/remote effects and never rerun generation. Persisted Git/GitHub success is reverified before receipt creation or terminal `Completed`, and the atomic receipt store either adopts byte-identical orphan content or fails closed without overwrite. Safe-read-only mode refuses before lease or mutation. No GitHub repository was created, changed, or contacted by the Task 5 test suite.

Fresh Task 5 verification on 2026-08-14 used the workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --disable-build-servers --verbosity minimal` | Exit 0; all projects up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 /nodeReuse:false` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Focused tests | Publication/Architecture Unit, Publication/Git Integration, and Desktop DI E2E filters | Exit 0; 92 + 97 + 2 passed, 0 failed, 0 skipped. |
| Full tests | Four Release project test commands with `--no-build --no-restore` | Exit 0; UnitTests 599, IntegrationTests 480, BlueprintTests 108, E2ETests 140; total 1,327 passed, 0 failed, 0 skipped. |
| EF model consistency | local `dotnet-ef migrations has-pending-model-changes ... --configuration Release --no-build` with pinned `DOTNET_ROOT`/`PATH` | Exit 0; `No changes have been made to the model since the last migration.` |
| Review | independent read-only Task 5 review | Approved; no Critical or Important findings. |

M8 Task 6 is complete locally. `ProjectCreationDraft` and `ProjectCreationPresetDraft` now carry immutable guarded Git options with Git-on, `main`, publish-off, private-on defaults. GitHub publication requires repository initialization plus a canonical reviewed personal account/repository; public visibility is valid only as an explicit reviewed publish choice. `ProjectCreationPlanSnapshot` rejects recipe or preview Git substitution, and the planner's canonical serialization binds initialization, branch policy, publish choice, visibility, account, and repository into the plan hash before execution. Preset schema v2 deterministically persists the exact intent; legacy schema v1 remains readable and upgrades to safe defaults that must be reviewed in a new plan. SQLite round-trip coverage proves both forms retain the expected behavior without a migration.

Fresh Task 6 verification on 2026-08-14 used the workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --disable-build-servers --verbosity minimal` | Exit 0; all projects up-to-date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 /nodeReuse:false` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Focused tests | Creation/Planning/M8 Git Unit filter plus SQLite preset compatibility | Exit 0; 121 + 1 passed, 0 failed, 0 skipped. |
| Full tests | Four Release project test commands | Exit 0; UnitTests 608, IntegrationTests 481, BlueprintTests 108, E2ETests 140; total 1,337 passed, 0 failed, 0 skipped. |
| EF model consistency | local `dotnet-ef migrations has-pending-model-changes ... --configuration Release --no-build` with pinned `DOTNET_ROOT`/`PATH` | Exit 0; `No changes have been made to the model since the last migration.` |
| Review | scoped static Task 6 security/design review | No Critical or Important findings. |

M8 Task 7 is complete locally. Create Project now renders Git initialization, exact `main`/`main + develop`, optional reviewed personal GitHub identity, and private-default visibility. Every choice invalidates the reviewed plan, whose preview displays the immutable Git/GitHub intent. Successful generation continues through an Application-owned publication facade; `PublishPending` keeps the local project visible with bounded authentication/remediation guidance and Retry Publish, while `Completed` displays the initial commit, ordered branches, publication receipt, and optional repository URL. Run History exposes publication retry through the same typed facade, and safe-read-only startup disables all Desktop publication mutation. Desktop retains no workspace, process, or persistence orchestration.

Fresh Task 7 verification on 2026-08-21 used the workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --force-evaluate --no-cache -m:1` | Exit 0; all 12 projects restored from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore -m:1 -nodeReuse:false` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Focused tests | Creation/Planning/Publication Unit and Desktop completion filters | Exit 0; 169 and 153 passed respectively, 0 failed, 0 skipped. |
| Full tests | Four Release project test commands with `--no-build --no-restore` plus final authoritative-reload and safe-mode Desktop regressions | Exit 0; UnitTests 610, IntegrationTests 481, BlueprintTests 108, E2ETests 153; total 1,352 passed, 0 failed, 0 skipped. |
| EF model consistency | pinned SDK + local `dotnet-ef.dll migrations has-pending-model-changes ... --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |

M8 Task 8 is complete locally. Two composed E2E tests add full trusted generation-to-real-local-Git/receipt completion and deterministic private fake-GitHub interruption/retry coverage. The final focused and full gates passed with 1,354 tests, 0 failures, and 0 skips; EF reports no pending model changes. Static scans found no forbidden shell/token/force/delete/package/privacy surface, and no real GitHub remote was contacted. M9 production blueprints are the recommended next milestone.

M9 Production Blueprints started on 2026-08-25. The complete baseline has been reread, the existing blueprint loader/execution/Desktop composition gaps have been audited, and the recommended versioned static-package design was approved and committed as `3dbda0b`. M9 is constrained to `desktop.csharp-wpf-tool`, `web.react-vite-ts`, and `tool.python-cli`; catalog expansion, installer/updater work, automatic deployment, arbitrary commands, and real GitHub mutation remain out of scope. Task 1 is now active: immutable built-in distribution, dual-source Desktop composition, and the production package contract harness. No M9 implementation or new passing test is claimed yet.

M9 Task 1 is complete locally. Build and publish output now carries the canonical `blueprints\built-in` assets, Desktop opens that immutable root as `BuiltIn` before provisioning the separate trusted-local application-data source, and source trust remains composition-owned. Missing built-in assets fail before local blueprint mutation. A production BlueprintTests fixture opens the shipped bytes through the guarded Windows filesystem and real `BlueprintCatalog`; ADR-0015 records the static package and exact-ID directory decision. The source-of-truth package shape was corrected to `blueprints/<id>` with semantic version in the manifest/checksum, not an unsupported version subdirectory.

Fresh Task 1 verification on 2026-08-25 used the workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
| --- | --- | --- |
| Locked restore | `dotnet restore DevForge.sln --locked-mode --disable-build-servers -m:1 --verbosity minimal` | Exit 0; all 12 projects restored from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0 after CRLF normalization; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Focused tests | production Blueprint distribution, Desktop host/source, and project dependency filters | Exit 0; 1 + 5 + 8 passed, 0 failed, 0 skipped. |
| Full tests | Four Release project test commands with `--no-build --no-restore` | Exit 0; UnitTests 610, IntegrationTests 481, BlueprintTests 109, E2ETests 158; total 1,358 passed, 0 failed, 0 skipped. |
| EF model consistency | local `dotnet-ef migrations has-pending-model-changes ... --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |
| Diff/security review | `git diff --check` plus scoped file/process/shell/latest scan and read-only diff review | Exit 0; no whitespace, unguarded runtime file operation, shell, arbitrary process, secret, or package-version issue found. Reviewer sub-agent was not dispatched because current orchestration policy forbids delegation without an explicit user request. |

Task 2 is now recommended: the production `desktop.csharp-wpf-tool` package and its real Windows/.NET 10 validation matrix.

M9 Task 2 is complete locally. `desktop.csharp-wpf-tool@1.0.0` is now a checksum-complete built-in package with a native .NET 10 WPF/MVVM/Clean Architecture solution, Generic Host/DI/config/logging, central pinned NuGet versions, five exact lockfiles, analyzers/nullable enforcement, unit coverage, publish profile, and seven production handoff documents. Its manifest has a Windows-only rule, exact .NET 10 range, deterministic declarative actions, locked restore, and required format/build/test/publish validators. The process boundary adds only one immutable `dotnet publish` argument sequence; mutated project/profile values and `run-process` reuse fail before `IProcessRunner` is invoked.

The production-composition E2E opens the shipped package through the guarded catalog, plans it twice, executes through owned staging and the closed handler registry, finalizes without Git, and proves identical plan hashes and final-tree digests. A separate standalone temp-tree matrix avoids inheriting DevForge build configuration and exposed a publish profile that originally escaped four parent directories; the regression now requires output under the generated project's `artifacts\publish`. Architecture discovery now excludes `bin`/`obj` payload copies so shipped blueprint `.csproj` files cannot be mistaken for DevForge projects. Root Git attributes force blueprint authoring bytes to LF, preventing `core.autocrlf` from invalidating package checksums on checkout.

Fresh Task 2 verification on 2026-08-25 used the workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
|---|---|---|
| Generated locked restore | standalone temp tree: `dotnet restore TeamTool.slnx --locked-mode --disable-build-servers -m:1` | Exit 0; all 5 generated projects restored. |
| Generated format | standalone temp tree: `dotnet format TeamTool.slnx --verify-no-changes --no-restore` | Exit 0; no formatting changes required. |
| Generated Release build | standalone temp tree: `dotnet build TeamTool.slnx --configuration Release --no-restore ...` | Exit 0; 0 warnings, 0 errors. |
| Generated tests | standalone temp tree: `dotnet test TeamTool.slnx --configuration Release --no-build --no-restore ...` | Exit 0; 1 passed, 0 failed, 0 skipped. |
| Generated publish smoke | standalone temp tree: `dotnet publish src\TeamTool.Desktop\TeamTool.Desktop.csproj --configuration Release --no-restore --property:PublishProfile=WindowsSmoke ...` | Exit 0; output produced under `artifacts\publish`. |
| DevForge locked restore | `dotnet restore DevForge.sln --locked-mode --disable-build-servers -m:1` | Exit 0; all projects up to date. |
| DevForge format | `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0. |
| DevForge Release build | serialized `dotnet build DevForge.sln --configuration Release --no-restore ...` | Exit 0; 0 warnings, 0 errors. |
| Full tests | four Release project test commands with `--no-build --no-restore` | Exit 0; UnitTests 610, IntegrationTests 485, BlueprintTests 115, E2ETests 159; total 1,369 passed, 0 failed, 0 skipped. |
| EF model consistency | local `dotnet-ef migrations has-pending-model-changes ... --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |

M9 Task 3 is complete locally. `web.react-vite-ts@1.0.0` is now a checksum-complete built-in package with React 19, Vite 8, strict TypeScript, synchronized `@` aliases, ESLint, Prettier, Zod-validated public environment input, an explicit API client boundary, Vitest/jsdom coverage, exact pinned dependencies, a pnpm lockfile, and seven production handoff documents. `.env*` is excluded while the empty-value `.env.example` remains tracked. ADR-0017 records the static-package and closed-pnpm decision.

The production command boundary accepts only `pnpm install --frozen-lockfile --ignore-scripts` for installation and exact `pnpm run lint|typecheck|test|build` validators. Extra arguments, deploy, exec, dlx, config, registry, credential, and lifecycle-script escape surfaces fail before `IProcessRunner` invocation. Two production-composition executions prove stable plan hashes and final-tree digests. A standalone temp tree proves the real package matrix without inheriting DevForge configuration. The full E2E run exposed that the new React and existing WPF composition tests could contend on the intentional process-wide execution gate; both now share a non-parallel M9 collection, and the full regression passes.

Fresh Task 3 verification on 2026-08-25 used Node 22.21.1, pnpm 10.24.0, and the workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
|---|---|---|
| Generated frozen install | standalone temp tree: `pnpm install --frozen-lockfile --ignore-scripts` with CI mode | Exit 0; lockfile current; 236 packages installed from the exact graph; lifecycle scripts disabled. |
| Generated format | standalone temp tree: `pnpm run format:check` | Exit 0; all matched files use Prettier style. |
| Generated lint | standalone temp tree: `pnpm run lint` | Exit 0; no ESLint diagnostics. |
| Generated typecheck | standalone temp tree: `pnpm run typecheck` | Exit 0; strict project-reference TypeScript compilation completed without diagnostics. |
| Generated tests | standalone temp tree: `pnpm run test` | Exit 0; 2 files and 2 tests passed. |
| Generated production build | standalone temp tree: `pnpm run build` | Exit 0; Vite 8.2.2 transformed 16 modules and emitted `dist`. |
| DevForge locked restore | `dotnet restore DevForge.sln --locked-mode --disable-build-servers -m:1` | Exit 0; all 12 projects restored or up to date from pinned lock files. |
| DevForge format | write pass followed by `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0; no formatting diagnostics. |
| DevForge Release build | serialized `dotnet build DevForge.sln --configuration Release --no-restore ...` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full tests | four Release project test commands with `--no-build --no-restore` | Exit 0 after the M9 non-parallel regression; UnitTests 610, IntegrationTests 495, BlueprintTests 120, E2ETests 160; total 1,385 passed, 0 failed, 0 skipped. |
| EF model consistency | local `dotnet-ef migrations has-pending-model-changes ... --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |

Task 4 is now recommended: the closed uv boundary and production `tool.python-cli` blueprint.

M9 Task 4 is complete locally. `tool.python-cli@1.0.0` is now a checksum-complete built-in package with a Python `src` layout, Hatchling packaging, strict mypy, Ruff format/lint, pytest coverage, immutable typed environment configuration, configured standard-library logging, an exact `uv.lock`, and seven production handoff documents. ADR-0018 records the certified CPython 3.14.6/uv 0.12.1 pair and closed-uv decision.

Python and uv are typed executable identities resolved only as direct trusted executables and included in the fixed environment doctor. Raw Python `-c`/`-m` modes are rejected at the `CommandSpec` boundary. The execution handlers admit only `uv sync --frozen --no-config` and six exact frozen/no-sync/no-config validators; dependency mutation, config/index overrides, evaluation, fix mode, path escape, arbitrary entrypoints, and the non-working direct `uv build --no-build-isolation` form fail before `IProcessRunner` invocation. The build validator instead uses the exact locked `pyproject-build --no-isolation` entrypoint inside the project environment.

Two production-composition executions prove stable plan hashes, exact command vocabulary, and identical final-tree digests. An independent rendered tree passes the real frozen toolchain matrix. The real matrix exposed that uv 0.12.1's interpreter catalog ends at CPython 3.14.6 and that direct non-isolated `uv build` cannot see Hatchling in `.venv`; both findings are captured in the certified pair and regression-protected build vocabulary.

Fresh Task 4 verification on 2026-08-26 used CPython 3.14.6, uv 0.12.1, and the workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
|---|---|---|
| uv binary integrity | SHA-256 of official `uv-x86_64-pc-windows-msvc.zip` plus `uv --version` | Exit 0; digest `8fcb0cb46e1229065e344758980924e569bef5882ef45f46fada8fb24e06b74a`; uv 0.12.1. |
| Generated frozen sync | standalone rendered tree: `uv sync --frozen --no-config` | Exit 0; CPython 3.14.6 selected; 19 locked packages installed. |
| Generated format/lint | `uv run --frozen --no-sync --no-config ruff format --check .` and `ruff check .` | Exit 0; 14 files formatted; all lint checks passed. |
| Generated typecheck | `uv run --frozen --no-sync --no-config mypy src tests` | Exit 0; no issues in 7 source files. |
| Generated tests | `uv run --frozen --no-sync --no-config pytest` | Exit 0; 4 passed, 0 failed. |
| Generated package build | `uv run --frozen --no-sync --no-config pyproject-build --no-isolation` | Exit 0; sdist and universal wheel built. |
| Generated CLI smoke | `uv run --frozen --no-sync --no-config team-tool --help` | Exit 0; reviewed help surface rendered. |
| DevForge locked restore | `dotnet restore DevForge.sln --locked-mode --disable-build-servers -m:1` | Exit 0; all 12 projects restored or up to date from pinned lock files. |
| DevForge format | `dotnet format DevForge.sln --verify-no-changes --no-restore` | Exit 0; no formatting diagnostics after CRLF normalization. |
| DevForge Release build | serialized `dotnet build DevForge.sln --configuration Release --no-restore ...` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full tests | four Release project test commands with `--no-build --no-restore` | Exit 0; UnitTests 611, IntegrationTests 499, BlueprintTests 124, E2ETests 161; total 1,395 passed, 0 failed, 0 skipped. |
| EF model consistency | local `dotnet-ef migrations has-pending-model-changes ... --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |
| Integrity/security/diff | checksum recomputation, forbidden-surface scan, and `git diff --check` | Exit 0; 27 declared package files, 0 missing/extra/mismatched; no forbidden product/package matches or whitespace errors. |

Independent read-only review approved Task 4 with no remaining Critical, Important, or Minor findings after the composed E2E was strengthened to pin the canonical 21-path output and stable aggregate tree digest. The separately executed real-tool matrix remains intentionally distinct from the always-on recording-runner composition suite; Task 6 will consolidate those release matrices across all three blueprints.

Task 5 is now recommended: shared handoff and engine-owned run evidence across all three production blueprints. Catalog expansion and M10 remain out of scope.

M9 Task 5 implementation is in focused verification. The three production packages now share one truthful seven-document handoff contract with their exact local quality commands and empty-value environment examples. The completion boundary owns `.devforge/project.recipe.yaml`, `devforge.lock.json`, `generation-report.json`, and `policy.snapshot.json`; blueprint output actions cannot target those paths. Canonical evidence binds deterministic engine/blueprint identity, checksum and plan hash, reviewed inputs/features, exact dependencies/tools, typed steps and validators, step outcomes, validation results, generated files, and non-self-referential SHA-256 integrity material. Artifacts, tool statuses, and warnings come from the authoritative persisted preview; the project report captures the immutable `validated-pre-finalization` boundary and excludes mutable checkpoint state. All bytes are built and privacy-checked before atomic staging writes, matching partial state is resumed byte-for-byte, and mismatched existing state fails without overwrite. Coordinator-level fault injection covers interruption after each of zero through four atomic evidence writes and after finalization intent persistence, with exact-byte/digest recovery to `LocalReady`. Legacy four-property evidence is rehydrated only through the persistence-only compatibility API; new runtime evidence is strict and timed. Focused results: Unit 67 passed, evidence/persistence Integration 42 passed, coordinator recovery Integration 1 passed, Blueprint 27 passed, and M9 E2E 3 passed. Full repository gates remain pending. ADR-0019 records the ownership, recovery, and final-tree digest decision.

Fresh focused verification on 2026-08-26 used the workspace-local .NET SDK 10.0.302 with build servers disabled and serial MSBuild. Planner/completion Unit coverage passed 48 tests; evidence/action-policy/package-loader Integration coverage passed 70 tests; production blueprint handoff/checksum coverage passed 16 tests; and deterministic WPF/React/Python composition passed 3 tests, including the observed canonical Python 25-path tree digest. Format verification exited 0 and the Release solution build completed with 0 warnings and 0 errors.

M9 Task 5 is complete locally. The final implementation derives evidence from the authoritative persisted preview, uses a distinct deterministic project-report schema, records exact engine/blueprint/tool/dependency/policy/validation/generated-file evidence, and keeps the established final-tree/publication digest boundary. Blueprint actions and declared artifacts cannot target the four reserved files. Matching partial evidence is adopted byte-for-byte; divergent validator, secret-scan, file, digest, or legacy-shape state fails closed. Coordinator-level guarded fault injection proves recovery after atomic evidence writes zero through four and after finalization-intent persistence. Legacy four-property evidence remains readable only through an internal persistence compatibility boundary. Independent specification and quality/security re-reviews found no remaining Critical, Important, or Minor issues.

Fresh final verification on 2026-08-26 used the workspace-local .NET SDK 10.0.302:

| Gate | Command | Exact result |
|---|---|---|
| Locked restore | `dotnet restore DevForge.sln --locked-mode --disable-build-servers -m:1 --verbosity minimal` | Exit 0; all 12 projects up to date from pinned lock files. |
| Format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| Release build | `dotnet build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full tests | `dotnet test DevForge.sln -c Release --no-build --no-restore --disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false` | Exit 0; Unit 632, Integration 526, Blueprint 126, E2E 161; total 1,445 passed, 0 failed, 0 skipped. |
| EF model consistency | local pinned `dotnet-ef.dll migrations has-pending-model-changes ... --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |
| Integrity/security/diff | scoped added-line shell/process/file/platform scan, empty-value `.env.example` check, package contract tests, and `git diff --check` | Exit 0; 0 shell/direct-process, unguarded file API, or forbidden platform/AI matches; both environment examples have 0 valued assignments; package checksum contracts and whitespace check pass. |

M9 Task 6 implementation is complete locally. The former per-blueprint E2E fixtures are consolidated into one production release matrix driven by a single typed case catalog. Desktop's real built-in/local source composition discovers exactly `desktop.csharp-wpf-tool`, `web.react-vite-ts`, and `tool.python-cli`; each case proves review, mutation-sensitive planning, exact ordered command materialization, two byte-identical finalized trees, engine evidence, no-overwrite refusal, owned failure recovery, typed forbidden-surface policy, and production local-Git commit verification without a remote. Specification and quality/security review finished with no remaining Critical, Important, or Minor findings.

The cross-blueprint matrix exposed two production defects. React declared `dist/index.html` as final evidence while `.gitignore` excluded it, preventing the committed Git tree from matching the finalized project. The package now intentionally commits its integrity-bound production bundle and every handoff document states that policy. A fresh real Vite build also exposed that Prettier scanned the four Task 5 engine-owned evidence paths; `.prettierignore` now excludes exactly `.devforge/`, `devforge.lock.json`, `generation-report.json`, and `policy.snapshot.json`, while source and project configuration remain checked. Both fixes have package checksum and regression coverage.

Fresh Task 6 verification on 2026-08-26 ran on Windows 10 Pro build 19045 with .NET SDK 10.0.302, Node 22.21.1, pnpm 10.24.0, Git 2.52.0, CPython 3.14.6, and uv 0.12.1:

| Gate | Command | Exact result |
|---|---|---|
| Focused cross-blueprint E2E | `dotnet test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-build --no-restore --disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false --filter "FullyQualifiedName~DevForge.E2ETests.M9.ProductionBlueprintReleaseMatrixE2ETests"` | Exit 0; 13 passed, 0 failed, 0 skipped. |
| Production package contracts | corresponding Release BlueprintTests command filtered to `DevForge.BlueprintTests.Production` | Exit 0; 18 passed, 0 failed, 0 skipped; all current package checksums match. |
| Generated WPF | fresh production-composed tree: locked restore, format verification, serialized Release build/test, and `WindowsSmoke` publish | Exit 0 for every command; 5 projects restored, build 0 warnings/0 errors, 1 test passed with 0 failed/skipped, publish emitted under `artifacts\publish`. |
| Generated React | fresh production-composed tree: frozen script-disabled install, format, lint, typecheck, test, and build | Exit 0 for every command; 236 locked packages installed, Prettier/ESLint/TypeScript clean, 2 files and 2 tests passed, Vite 8.2.2 transformed 16 modules and emitted 3 reviewed `dist` files. |
| React real-output Git repeatability | isolated local root commit of the complete real generated tree, second `pnpm run build`, `git status --porcelain=v1`, and `git remote` | Exit 0; 36 files committed, second build emitted identical asset names, status empty, no remote configured or contacted. |
| Generated Python | fresh production-composed tree: frozen sync, Ruff format/lint, mypy, pytest, package build, and CLI help | Exit 0 for every command; 19 locked packages installed, 14 files formatted, no type issues in 7 source files, 4 tests passed, sdist and universal wheel built, reviewed help rendered. |
| DevForge locked restore | `dotnet restore DevForge.sln --locked-mode --force-evaluate --no-cache --disable-build-servers -m:1 --verbosity minimal` | Exit 0; all 12 projects restored from pinned lock files. |
| DevForge format | `dotnet format DevForge.sln --verify-no-changes --no-restore --verbosity minimal` | Exit 0; no formatting diagnostics. |
| DevForge Release build | `dotnet build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false --verbosity minimal` | Exit 0; all 12 projects built; 0 warnings, 0 errors. |
| Full tests | `dotnet test DevForge.sln -c Release --no-build --no-restore --disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false --verbosity minimal` | Exit 0; Unit 632, Integration 526, Blueprint 127, E2E 171; total 1,456 passed, 0 failed, 0 skipped. |
| EF model consistency | pinned local `dotnet-ef.dll migrations has-pending-model-changes --project src/DevForge.Infrastructure --startup-project src/DevForge.Infrastructure --context DevForgeDbContext --configuration Release --no-build` | Exit 0; `No changes have been made to the model since the last migration.` |
| Security/privacy/diff | focused typed action/control tests, scoped dependency/platform/control scan, empty-value `.env.example` scan, and `git diff --check` | Exit 0; 0 forbidden project dependency/platform or blueprint-control matches, 0 valued environment examples, no Task 6 `src` mutation, and no whitespace errors. |

The available host is Windows 10 build 19045, so the approved M9 Windows 11 WPF/React/Python release matrix was not executed and is not reported green. Task 6 code and the available-host closure are complete, but the M9 release exit gate remains open until those Windows 11 commands pass with exact evidence. Per the specification, M10 must not begin before that certification is recorded.

## Post-Task 6 Desktop launch regression

A real Release launch on 2026-08-26 exposed two startup defects that build and headless composition tests had not exercised. First, Desktop declared the external blueprint tree as WPF `Content`; the shipped WPF blueprint's `App.xaml` consequently appeared as root `app.xaml` assembly content and shadowed DevForge's compiled application resource. The Desktop project now copies blueprint payload as non-WPF `None` items, preserving the exact output tree without adding blueprint files to WPF content metadata. Second, the four read-only Settings checklist booleans used `CheckBox.IsChecked`'s default two-way binding and threw when WPF attempted to update their getter-only properties. All four bindings are now explicitly one-way.

The packaging regression was observed RED with `app.xaml` present in `AssemblyAssociatedContentFileAttribute`, then GREEN after the item-classification fix. The binding regression was observed RED against all four missing `Mode=OneWay` declarations, then GREEN after the XAML correction. Fresh locked restore and format verification exit 0; the Release solution build completes with 0 warnings and 0 errors; the full suite passes Unit 632, Integration 526, Blueprint 127, and E2E 173, for 1,458 passed with 0 failed or skipped. The launched `DevForge.Desktop` process remained responsive with the visible title `DevForge Studio — Leader Edition` after startup and the complete verification run.
