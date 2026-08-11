# M5 Recoverable Orchestration Implementation Plan

**Goal:** Execute immutable M4 plans safely in owned staging with transactional checkpoints, bounded retry/resume, validation evidence, and no-overwrite finalization.

**Source:** Full DOCX specification sections 6, 7.6-7.9, 9.5-9.6, 10, 12, 14-16, 18-20; Markdown companion; ADR-0008; approved M5 design.

**Excluded:** WPF UX, Git/GitHub behavior, production blueprints, packaging, and V1 catalog expansion.

## Task 1: Evolve Domain execution and recovery invariants

- [x] RED tests for explicit retry modes, bounded automatic/manual policy, attempt output digest, interruption closure, cancelled/validation-failed resume, terminal/finalized cleanup guards, and warning validation status.
- [x] Implement immutable guarded Domain values without I/O dependencies.
- [x] GREEN Domain tests; commit `feat(domain): model recoverable execution lifecycle`.

## Task 2: Define execution, checkpoint, staging, and handler ports

- [x] RED contract/reflection/null/immutability tests for execution requests, checkpoint snapshots, blueprint provenance, staging descriptors, handler lifecycle/results, finalizer/report writer, recovery and cleanup APIs.
- [x] Evolve `IExecutionOrchestrator`, add the full `IRunCheckpointStore` successor boundary, define guarded workspace descriptors, and carry exact `PlannedProject` provenance while preserving dependency direction.
- [x] GREEN Application/architecture tests; commit `feat(application): define recoverable execution contracts`.

## Task 3: Persist complete run checkpoints

- [x] RED migration and repository tests for plan hash/body, blueprint fingerprint, staging/target descriptors, marker/finalization/report state, attempt digest, old-schema upgrade, corruption/privacy, chronological ordering, and atomic replacement.
- [x] Add one versioned EF Core migration and bounded canonical plan/checkpoint mapping.
- [x] GREEN fresh/upgrade/round-trip/concurrency tests and pending-model check; commit `feat(persistence): store execution checkpoints`.

## Task 4: Implement owned staging and cleanup guards

- [ ] RED real-workspace tests for create-new marker, exact identity validation, target-exists refusal, junction/symlink escape, spoofed/malformed marker, finalized cleanup refusal, cancellation, and concurrent lease.
- [ ] Add guarded subworkspace support and staging manager under Infrastructure.
- [ ] GREEN twice; commit `feat(infrastructure): manage run-owned staging workspaces`.

## Task 5: Reopen exact blueprint content for execution

- [ ] RED tests for exact source/package/checksum/trust/identity, changed/missing package, cancellation, no absolute path exposure, and verified-byte reopening.
- [ ] Implement `IBlueprintExecutionSource` through the existing M4 source/loader boundary.
- [ ] GREEN catalog regressions; commit `feat(infrastructure): reopen verified blueprint content`.

## Task 6: Build closed handler registry and value materialization

- [ ] RED matrix for every handler ID, trust restriction, typed placeholder materialization, unavailable target placeholder, bounds, malformed maps, registry duplicates, and no reflection/direct process.
- [ ] Implement closed registry/context/result/pre/postcondition contracts and trusted materializer.
- [ ] GREEN architecture/security tests; commit `feat(infrastructure): dispatch closed execution handlers`.

## Task 7: Implement guarded file/template/patch handlers

- [ ] RED integration fixtures for create/render/copy and closed JSON/YAML/XML set/remove operations, atomic write/reparse, retry cleanup, traversal, `.env`, DTD/entity/tag/alias/duplicate-key, bounds, locked files, and cancellation.
- [ ] Implement handlers using only guarded workspaces, verified blueprint content, restricted renderer, BCL parsers, and pinned YamlDotNet.
- [ ] GREEN twice; commit `feat(infrastructure): execute guarded file handlers`.

## Task 8: Implement trusted process and validator handlers

- [ ] RED tests for executable/ArgumentList separation, working-directory containment, allowed exit codes, timeout, cancellation/tree kill, bounded redacted progress/digest, package install, postcondition failure, and retry classification.
- [ ] Adapt typed plan payloads to `CommandSpec` and `IProcessRunner`; never accept a shell string.
- [ ] GREEN real process/security tests; commit `feat(infrastructure): execute trusted process handlers`.

## Task 9: Implement retry/resume orchestration and checkpoints

- [ ] RED orchestration tests for plan-first persistence, six-phase ordering, save after every state change, automatic/manual retry, retry cleanup, skip-after-postcondition, rerun-on-drift, missing blueprint, plan/marker mismatch, observer failure isolation, cancellation, and single active lease.
- [ ] Implement Application retry decision engine and orchestrator with injected time/ID/ports.
- [ ] GREEN twice; commit `feat(application): orchestrate checkpointed execution`.

## Task 10: Validate, report, and finalize atomically

- [ ] RED tests for required/optional validators, secret scan, no target on failure, same-volume atomic move, cross-volume copy/hash/tree verification, report-before-cleanup, report failure retention, LocalReady transition, and no finalized cleanup.
- [ ] Implement validation pipeline, report writer, finalizer, and staging cleanup transaction.
- [ ] GREEN failure/security matrix; commit `feat(infrastructure): finalize validated project runs`.

## Task 11: Recover interrupted runs

- [ ] RED startup recovery tests for stale running attempts, exact checkpoint/marker/fingerprint validation, cancelled/validation-failed resume, no duplicate successful effects, unavailable blueprint remediation, and cleanup refusal.
- [ ] Implement interruption normalization and explicit resume/cleanup services.
- [ ] GREEN app-kill/resume integration tests; commit `feat(application): recover interrupted project runs`.

## Task 12: Close M5

- [ ] Update ADR, implementation plan/status, changelog, and exact test evidence.
- [ ] Run SDK, locked restore, format verify, Release build, full solution, focused M5 unit/integration/security, migration consistency, and zero-skipped gates.
- [ ] Commit `docs: complete M5 recoverable orchestration milestone`; require clean worktree and no push.

## M5 exit gate

Kill/resume and failure injection paths must pass; target remains absent/unchanged on all pre-finalization failures; finalization is verified/no-overwrite; cleanup requires marker ownership; mandatory quality evidence and report persistence precede `LocalReady`; all operations remain behind guarded abstractions.
