# M5 Recoverable Orchestration Design

**Status:** Approved

**Date:** 2026-08-11

## Goal and scope

M5 executes immutable M4 plans inside run-owned staging, records transactional checkpoints, validates postconditions, supports bounded retry/resume, finalizes without overwrite, writes generation evidence, and recovers interrupted runs. It implements FR-050 through FR-063 and the specification's Execution Engine, recovery, and M5 exit gate.

Included handlers are create-directory, render-template, copy-overlay, closed JSON/YAML/XML patching, trusted run-process, package-install, validate-command, and the built-in finalization boundary. Git/GitHub behavior, WPF composition, production blueprints, packaging, and V1 catalog expansion remain deferred.

## Dependency and ownership boundaries

- Domain: immutable retry modes, attempt/checkpoint evidence, lifecycle resume/recovery invariants, validation/report values.
- Application: execution/checkpoint contracts, retry decision engine, orchestration sequence, cancellation/progress semantics.
- Infrastructure: SQLite checkpoint mapping, staging markers, guarded file transformations, trusted process handler adaptation, blueprint-content reopening, report output, atomic/copy-verified finalization.
- Desktop/CLI: no M5 process or file implementation.

Application never calls `File`, `Directory`, `Process`, `Environment`, `Guid`, or wall-clock APIs directly. Time and run IDs enter through injected ports. Every external command is a typed `CommandSpec` consumed by `IProcessRunner`.

## Execution request and immutable checkpoint

`ExecutionRequest` contains a `PlannedProject`, `ProjectRun`, target-parent workspace, canonical target-relative directory, run-artifact workspace, and explicit execution mode. `PlannedProject` carries the exact `BlueprintFingerprint` used by M4 hashing.

`RunCheckpoint` snapshots:

- run lifecycle and attempts;
- full immutable execution plan and plan hash;
- exact blueprint ID, version, source, package-relative directory, trust, and checksum;
- guarded staging/target descriptors and ownership marker ID;
- completed step/validator evidence and output digests;
- finalization/report state.

The checkpoint is persisted before the first step and after start/completion of every attempt, validation decision, finalization intent/result, and report result. Persistence is atomic per run.

## Staging and marker protocol

The staging manager creates `.devforge-staging\<run-id>` below the already-opened target parent and returns guarded parent, payload, and marker paths. The marker is bounded canonical JSON and is written with create-new semantics. It never contains source, secrets, raw command output, or absolute catalog paths.

Before any destructive cleanup or finalization, Infrastructure rereads and validates the marker against the checkpoint. Reparse points anywhere in the owned tree fail closed. Target must not exist; a pre-existing empty target is also rejected in MVP to keep no-overwrite semantics unambiguous.

Finalization first persists intent. Same-volume payload move uses `AtomicNoOverwriteFinalize`. If a different-volume implementation is supplied, it copies to a target-parent run-owned temporary directory, verifies every file's length and SHA-256 digest plus the exact ordinal tree, then atomically renames. Report/checkpoint persistence succeeds before the staging container is deleted.

## Blueprint content and placeholders

At execute/resume, `IBlueprintExecutionSource` reopens the source from the opaque fingerprint, reruns the M4 loader/checksum verification, and requires exact manifest identity and aggregate checksum. Missing or changed content returns `DF-EXEC-003` and blocks resume.

Typed placeholder maps from M4 are materialized only inside trusted Infrastructure handlers:

- `runtime.staging-path`: process-safe absolute payload path when a CLI requires it;
- `runtime.run-id`: current bounded run identifier;
- `project.target-path`: unavailable before finalization and accepted only by post-finalization built-in handlers.

Replacement is one pass and never reparsed. Ordinary path parameters stay canonical workspace-relative paths.

## Handler lifecycle

Every step runs the same six phases from the specification:

1. Prepare typed inputs, trusted tool identity, guarded working directory, declared environment, and redaction material.
2. Check dependency, workspace path, tool, marker, and handler-specific preconditions.
3. Execute with progress, timeout, and cancellation.
4. Validate handler-specific postconditions independently of process exit code.
5. Persist attempt outcome, duration timestamps, exit code, redacted output digest, error, and checkpoint.
6. Continue, warn, retry, fail validation, cancel safely, or terminate.

Handlers are resolved through a closed ordinal registry; reflection and package-selected .NET types are forbidden. Git/GitHub IDs return a stable unsupported-in-M5 failure.

## Closed file handlers

- create-directory: creates one canonical relative path in payload; postcondition requires a directory.
- render-template: reads a bounded verified package template, renders through `ITemplateRenderer`, and writes one payload file. Retry overwrites only its declared target.
- copy-overlay: enumerates a verified package subtree and copies an exact bounded tree into payload; retry replaces only declared outputs.
- patch-json: supports bounded `set` and `remove` operations over a canonical JSON Pointer subset, rejects duplicate keys, and writes canonical JSON atomically.
- patch-yaml: supports the same scalar/map operation subset through pinned YamlDotNet; aliases, tags, merge keys, duplicate keys, and arbitrary object activation stay forbidden.
- patch-xml: supports bounded element/attribute paths made only of canonical names; DTD, entities, external resolution, XPath supplied by packages, and raw regex replacement are forbidden.

All transforms write a sibling temporary file, flush, reparse/verify, then replace through a guarded abstraction. No handler can escape payload.

## Process and validation handlers

run-process, package-install, and validate-command map only the already-whitelisted typed payload to `ExecutableIdentity`, individual arguments, guarded working directory, declared safe environment entries, timeout, allowed exit codes, and redaction needles. No combined command line exists.

Process success requires an allowed exit code and handler postconditions. Timeout maps to `DF-EXEC-002`; cancellation propagates after the process tree is terminated and the attempt/checkpoint is recorded; disallowed exit or postcondition failure maps to `DF-EXEC-001` or `DF-VALID-001`.

Validators execute after generation actions. Required failure transitions to `ValidationFailed`; optional failure becomes a warning. A whole-payload `ISecretScanner` pass is mandatory before finalization and any finding maps to `DF-SECRET-001`.

## Retry, resume, cancellation, and recovery

- AutomaticLimited retries only `IsRetryable` failures, waits via injected `TimeProvider`, and never exceeds policy attempts.
- Manual mode records failure and waits for an explicit retry/resume request.
- Before retry, the handler verifies ownership and cleans only its declared outputs or proves idempotence.
- Resume verifies checkpoint/marker/fingerprint first, then rechecks successful-step postconditions in plan order. Drifted steps rerun; valid steps are skipped.
- Cancellation stops the current handler/process tree, records a cancelled attempt, saves the run, and leaves owned staging for resume/cleanup.
- Startup recovery scans persisted `Executing` runs. A persisted running attempt is closed with retryable `DF-EXEC-003`; no process is assumed alive merely from journal state.
- Only one orchestrator lease may be active in MVP. Concurrent execution requests fail before mutation.

## Generation report

The report writer emits bounded canonical JSON and human-readable Markdown to the run-artifact workspace. Evidence includes run/plan hash, blueprint identity, tool status, step attempts, validator outcomes, artifact summary, ordered warnings/errors, and timestamps. It contains no source, raw output, environment values, credentials, or absolute catalog path.

M5 transitions to `LocalReady` only after required validators, secret scan, finalization, checkpoint persistence, and report persistence succeed. Cleanup of staging follows those writes.

## Stable errors

- `DF-EXEC-001`: handler/process/pre/postcondition failure.
- `DF-EXEC-002`: timeout after process-tree termination.
- `DF-EXEC-003`: interrupted or invalid resume checkpoint/fingerprint.
- `DF-VALID-001`: required quality gate failed.
- `DF-SECRET-001`: staged secret finding blocked finalization.
- `DF-FINAL-001`: target policy, copy verification, atomic move, or cleanup-finalization boundary failed.

Public errors retain scrubbed summaries/details only.

## Testing and exit gate

Unit tests cover lifecycle, retry decisions, checkpoint invariants, orchestration ordering, progress isolation, optional/required validation, cancellation, and resume selection. Integration tests use real guarded workspaces/processes for markers, junctions, staging, handler outputs, timeout/tree kill, copy verification, no-overwrite finalization, SQLite checkpoint round trips, app-kill recovery, and cleanup refusal. Security tests cover traversal, marker spoofing, plan/fingerprint drift, command injection, malicious patches, `.env`/secret findings, and redacted reports.

M5 completes only after locked restore, format verification, Release build with zero warnings/errors, full solution tests, focused Domain/Application orchestration tests, focused Infrastructure execution/failure/security tests, migration consistency, and zero skipped M5 tests.
