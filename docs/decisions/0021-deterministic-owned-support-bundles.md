# ADR-0021: Deterministic Owned Support Bundles

**Status:** Accepted
**Date:** 2026-08-26

## Context

M10 must export useful local support evidence without customer source, databases, `.env` data, credentials, raw environment properties, archive-slip names, partial ZIP files, or path strings becoming deletion authority. Export must also recover exactly when the process stops before staging or final publication.

## Decision

- Application validates canonical non-secret run requests and exposes only typed receipts/results. The coordinator resolves the authoritative persisted checkpoint before invoking Infrastructure.
- Infrastructure generates a closed set of forward-slash archive entries: scrubbed recipe/checkpoint/plan summaries, blueprint identity and aggregate checksum, persisted generation reports, marker-verified run JSONL, optional tool status without environment properties, a bounded error-catalog excerpt, and `inventory.json`.
- Every text entry is strict UTF-8, BOM-free, LF-normalized, secret/source scanned, and individually capped at 4 MiB; the aggregate archive is capped at 16 MiB.
- Entry order, ZIP timestamps, compression mode, schemas, and inventory hashes are fixed. Bundle identity derives from the complete deterministic archive SHA-256.
- Publication uses a marker-owned run-specific staging directory, one cross-process export lease, a canonical ownership marker written before each atomic ZIP publication, exact retry adoption, and no-overwrite final paths under `support-bundles`.
- Cleanup accepts a typed receipt but authorizes deletion only after matching the canonical marker and complete archive digest. It is idempotent and never treats a canonical-looking filename as ownership.

## Consequences

The bundle intentionally contains summaries and safe evidence rather than full recipes, command arguments, raw tool output, environment properties, the SQLite database, or project source. A crash may leave marker-owned staging state, but a retry either completes the identical archive or fails closed; unowned or tampered artifacts remain untouched for manual review.
