# Milestone M5 Recoverable Orchestration Implementation Plan

**Goal:** Execute immutable M4 plans inside owned staging with transactional checkpoints, bounded retry/resume, validation evidence, and no-overwrite finalization.

**Status:** Tasks 1-7 implemented and verified locally; Task 8 is next.

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
- [ ] Implement trusted process and validator handlers.
- [ ] Implement checkpointed retry/resume orchestration.
- [ ] Implement validation, report persistence, and atomic finalization.
- [ ] Implement interrupted-run recovery.
- [ ] Run and record the complete M5 exit gate.

## Current exit gate

Task 7 requires guarded create/render/overlay handlers, closed JSON/YAML/XML transforms, deterministic plan-bound render context, atomic verified publication, exact `.env` policy, bounded retry cleanup, transient-only retry classification, cancellation, locked restore, format, Release build with zero warnings/errors, and full regression tests.

M5 completes only after all remaining tasks pass kill/resume and failure-injection coverage, target absence on every pre-finalization failure, verified no-overwrite finalization, report/checkpoint persistence before cleanup, and all full/focused gates with zero skipped M5 tests.
