# ADR-0011: Desktop startup transaction and read-only safe mode

**Status:** Accepted

**Date:** 2026-08-12

## Context

The desktop shell must not expose editable workflows until persistence is migrated and interrupted runs are normalized. Startup failure must preserve user data and still provide useful local diagnostics without introducing an unsafe recovery path.

## Decision

- Startup order is fixed: start host, migrate persistence, recover interrupted runs, load settings, apply theme, load environment state, aggregate Dashboard state, then select the initial route.
- Migration or recovery failure enters read-only safe mode with a scrubbed user message. Safe mode disables settings persistence, environment rescans, generation, resume, cleanup, and storage-dependent routes.
- Safe mode may display already-loaded cache and copy bounded scrubbed diagnostics. It performs no environment scan or database mutation.
- Environment Doctor cache is fresh for exactly 15 minutes. Startup reuses fresh cache, scans stale or missing cache once, and refuses concurrent scans. Explicit rescan remains available only outside safe mode.
- Settings writes validate the complete immutable draft before persistence. Supported IDE and culture identifiers are closed sets, and sensitive-shaped identifiers or values are rejected.
- Shutdown cancels outstanding startup/scan work, awaits cooperative host shutdown, and disposes the host without synchronous dispatcher blocking.

## Consequences

The application never edits against an unknown schema or stale interruption state. A failed startup remains inspectable without risking further writes, and Environment Doctor work is bounded, cached, cancellation-aware, and non-concurrent.

## Rejected alternatives

- Continuing normally after migration or recovery failure.
- Retrying migration, cleanup, or scans from the safe-mode UI.
- Scanning on every startup regardless of cache freshness.
- Allowing repeated Rescan clicks to create concurrent external probes.
- Persisting partially valid settings or raw exception/process output.
