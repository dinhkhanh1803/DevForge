# Milestone M5 Recoverable Orchestration Implementation Plan

**Goal:** Execute immutable M4 plans inside owned staging with transactional checkpoints, bounded retry/resume, validation evidence, and no-overwrite finalization.

**Status:** Tasks 1-10 implemented and verified locally; Task 11 recovery is next.

**Architecture:** Application owns lifecycle and orchestration decisions. Infrastructure owns guarded staging, exact blueprint reopening, handlers, reports, finalization, and concrete checkpoint persistence. Domain remains immutable and I/O-free.

**Tech stack:** .NET SDK 10.0.302, C# 14, WPF/.NET 10, EF Core SQLite, Windows BCL/native guarded filesystem APIs, xUnit 2.9.3.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-11-m5-recoverable-orchestration-design.md`
- Task plan: `docs/superpowers/plans/2026-08-11-m5-recoverable-orchestration.md`
- Decision: `docs/decisions/0008-recoverable-run-orchestration.md`

## Scope

M5 includes execution lifecycle/checkpoints, owned staging, exact blueprint reopening, a closed handler registry, guarded file/template/patch/process handlers, retry/resume orchestration, validation, reporting, atomic no-overwrite finalization, and interrupted-run recovery.

M5 excludes WPF workflow composition, Git/GitHub behavior, production blueprints, packaging, cloud backends, AI APIs, and V1 catalog expansion.

## Progress

- [x] Model recoverable Domain lifecycle and retry invariants.
- [x] Define execution, checkpoint, staging, handler, finalization, and recovery ports.
- [x] Persist complete canonical run checkpoints through EF Core SQLite.
- [x] Implement atomic run-owned staging, exact markers, cross-run leases, resume checks, and guarded cleanup.
- [x] Reopen exact verified blueprint content for execution.
- [x] Build the closed handler registry and typed value materialization.
- [x] Implement guarded file, template, overlay, and structured patch handlers.
- [x] Implement trusted process and validator handlers.
- [x] Implement checkpointed retry/resume orchestration.
- [x] Implement validation, report persistence, and atomic finalization.
- [ ] Implement interrupted-run recovery.
- [ ] Run and record the complete M5 exit gate.

## Current exit gate

Task 10 now treats `finalize-workspace` as the non-dispatched completion boundary, runs ordered validators and the mandatory whole-payload secret scan, persists bounded privacy-safe tool/warning/attempt/validation/artifact/error reports, and publishes only after an exact ownership-marker check. Same-volume publication binds the payload workspace to the descriptor; detached publication uses bounded ordinal tree/hash verification and an atomic no-overwrite rename. The orchestrator releases the global staging lease before exact finalized cleanup and surfaces retained cleanup debt with the durable `LocalReady` checkpoint. Task 11 is limited to startup discovery and normalization/resume of interrupted checkpoints.

### Task 10 implementation boundary

- Scope: execute ordered required/optional validators, scan the complete staged payload, persist bounded JSON/Markdown evidence, finalize without overwrite, transition to `LocalReady`, then remove only the exact finalized staging container.
- Expected production files: Application completion orchestration/contracts plus focused Infrastructure report-writer, finalizer, and finalized-staging cleanup implementations. Existing execution/checkpoint models change only where the approved transaction requires an explicit state transition.
- Tests: unit orchestration matrix; real guarded-workspace same-volume/report tests; cross-volume copy/tree/hash fakes or fixtures; failure injection for validator, scan, report, finalizer, cleanup, cancellation, and target collision.
- Exit gate: target absent on every pre-finalization failure; required validation/secret findings block finalization; optional failures are ordered warnings; report and checkpoint are durable before cleanup; finalization never overwrites; successful run is `LocalReady`; focused and full gates pass with zero skipped M5 tests.

M5 completes only after all remaining tasks pass kill/resume and failure-injection coverage, target absence on every pre-finalization failure, verified no-overwrite finalization, report/checkpoint persistence before cleanup, and all full/focused gates with zero skipped M5 tests.
