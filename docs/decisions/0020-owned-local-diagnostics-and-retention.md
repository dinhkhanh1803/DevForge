# ADR-0020: Owned Local Diagnostics and Retention

**Status:** Accepted
**Date:** 2026-08-26

## Context

M10 requires useful production diagnostics without cloud telemetry, secret persistence, arbitrary filesystem authority, partial JSONL records, or cleanup of user-owned data. Multiple Desktop processes can target the same local-data root, and a canonical-looking filename alone is not ownership evidence.

## Decision

- Application owns bounded immutable diagnostic events, retention policies, and redacted partial results.
- Infrastructure revalidates every structured value and message immediately before fixed-order UTF-8 JSONL serialization. Secret-shaped values are replaced and oversized messages are rejected before proportional allocation.
- Daily and run-specific logs are created only through guarded workspaces. Each log requires an exact canonical sidecar ownership marker.
- Writers and retention share one OS-exclusive workspace lease. Contention is bounded and cancellable; it does not silently drop an event.
- Retention considers only canonical JSONL paths with a matching marker, preserves the active day, deletes deterministically, and stops between candidates on cancellation or the first failed delete.
- Desktop applies the persisted retention policy during normal startup and emits a bounded startup-ready event. Diagnostic failures remain best-effort and do not make an otherwise usable Desktop fail startup.
- No external telemetry, cloud logging, token capture, raw process output, database cleanup, or customer-project traversal is introduced.

## Consequences

Retention can leave a scrubbed deferred result and owned artifacts for a later retry, but it cannot infer ownership from names. Atomic whole-file publication costs bounded rewrite work, capped at 16 MiB per log, in exchange for parseable kill-window behavior. Support bundles remain a separate M10 Task 3 capability and must independently verify ownership and privacy.
