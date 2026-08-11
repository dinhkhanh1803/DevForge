# ADR-0008: Recoverable run orchestration and owned staging

**Status:** Accepted

**Date:** 2026-08-11

## Context

M5 must execute immutable M4 plans without creating a half-finished target, persist every checkpoint, support bounded retry/resume after cancellation or app termination, and clean only run-owned data. The specification fixes the public run statuses and requires file/process operations to remain behind guarded abstractions.

## Decision

- Application owns the orchestration state machine and calls injected ports only; it performs no direct file, process, clock, random, or database operation.
- Infrastructure owns staging workspaces, ownership markers, blueprint-content revalidation, step handlers, finalization, report writing, and concrete persistence.
- Every execution starts from a `PlannedProject` carrying the exact blueprint fingerprint. The checkpoint persists the immutable plan before the first handler runs.
- Effect-bearing renderer context is a deterministic immutable part of the plan, canonical hash, and checkpoint. Handler requests carry the owning plan and derive context from it; hyphenated planning-variable segments use one documented underscore alias because the restricted renderer accepts identifier segments only. Checkpoints created before this hash input existed fail closed and must be replanned.
- Staging uses `<target-parent>\.devforge-staging\<run-id>\payload` when possible, so final rename is same-volume. The ownership marker stays outside `payload` and contains only run ID, plan hash, blueprint identity/checksum, and lifecycle intent.
- A guarded atomic create-if-absent directory capability proves run-container ownership. A target-parent lease outside the run container serializes staging operations and remains held through cleanup; an unproven or lost ownership race is never deleted.
- Same-volume finalization is an atomic no-overwrite move. Cross-volume fallback copies into a run-owned target-side temporary directory, verifies ordinal relative paths, lengths, and SHA-256 digests, then atomically renames. A partial target is never accepted as final.
- Cleanup requires an exact valid ownership marker, matching run/checkpoint identity, a non-finalized state, and a guarded relative path. Finalized project directories are never cleanup candidates.
- Retry modes are `None`, `Manual`, and `AutomaticLimited`. Automatic retry is allowed only for explicitly classified transient errors and remains bounded by the immutable policy. Every retry invokes handler cleanup/idempotence preparation first.
- Resume reopens the exact checkpoint, revalidates ownership marker, plan hash, blueprint ID/version/checksum, and postconditions of every previously successful step. A passed step is skipped only when its postcondition still passes.
- The official `RunStatus` enum remains unchanged. A stale running attempt found at startup becomes a failed retryable attempt with `DF-EXEC-003`; the run remains `Executing` until explicit resume or safe cleanup. This represents interruption without inventing an unsupported status.
- Mandatory validator or secret-scan failure prevents finalization and transitions to `ValidationFailed`. Optional validators become ordered report warnings. M5 ends a successful local generation at `LocalReady`; Git/GitHub completion remains M8.
- Git/GitHub handlers stay reserved but non-executable in M5. They are composed only after local quality gates in M8.
- File transforms publish only through guarded sibling-temporary atomic replacement after serialization is reparsed. Exact `.env` path segments are forbidden while benign names such as `.env.example` remain available; automatic retry is limited to classified I/O failures, never malformed content or template policy failures.

## Consequences

Execution can be killed and recovered without trusting stale process state or UI-supplied paths. Target creation is all-or-nothing, retry is evidence-based, and journal/report evidence precedes staging cleanup. M5 adds persistence/schema work for immutable checkpoints but does not expand the blueprint catalog or implement Git/GitHub behavior.

## Rejected alternatives

- Writing directly into the target directory.
- Treating exit code zero as sufficient postcondition evidence.
- Skipping prior passed steps on resume without revalidation.
- Recursive deletion based on an absolute path supplied by UI or database alone.
- General-purpose handler reflection, arbitrary shell commands, or script execution.
- Adding an `Interrupted` enum value contrary to the fixed specification status set.
- Retrying GitHub publish by regenerating source or rerunning local build steps.
