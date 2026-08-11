# ADR-0007: Deterministic guarded blueprint planning

**Status:** Accepted

**Date:** 2026-08-11

## Context

M4 must turn versioned blueprint packages into immutable execution plans without executing package-controlled behavior. Packages may be malformed, malicious, conflicting, stale, or locally modified. Planning must be reproducible across machines and cultures while preserving the M3 process, filesystem, privacy, and template boundaries.

## Decision

- Infrastructure alone discovers and parses packages through guarded workspace roots.
- YamlDotNet 18.1.0 is pinned centrally and used only by Infrastructure; JSON uses the BCL.
- Trust derives from source provenance plus persisted identity/checksum state, never manifest content.
- Invalid packages are quarantined with stable scrubbed issue codes. Untrusted, quarantined, disabled, and conflicting packages are inspect-only.
- Blueprint/action/input/rule identifiers use the existing lowercase ASCII dot-or-hyphen grammar.
- Package files are verified against complete SHA-256 declarations before control content is parsed.
- Blueprint rules use a bounded closed parser/evaluator; no script engine, reflection, regex supplied by packages, file/process/environment/network access, or implicit coercion exists.
- Execution payloads use immutable JSON-like plan values, preserving typed argument arrays and objects rather than flattening command strings.
- Catalog refresh publishes one complete immutable snapshot atomically or retains the previous snapshot.
- Exact ID and semantic version are required. There is no `latest` or fallback resolution.
- Canonical UTF-8 JSON and SHA-256 cover every effect-bearing structural input while excluding timestamps, absolute machine paths, detected tool paths/versions, and non-effect-bearing warning outcomes.
- M4 creates no production blueprint and executes no step.

## Consequences

Planning is deterministic, previewable, and safe to persist or compare. Package authors have a deliberately narrow schema and rule/action vocabulary. Local modifications require renewed trust. Schema or grammar expansion requires explicit contract/security tests and a decision update rather than permissive parsing.

## Rejected alternatives

- Trust declared inside a manifest or source-order shadowing of duplicate packages.
- General-purpose expressions, Scriban rules, reflection-based handlers, or arbitrary command strings.
- Parsing package files before checksum verification.
- Mutable catalog collections or partial refresh publication.
- Machine-specific paths, timestamps, random identifiers, or detected tool versions in the plan hash.
- Selecting the newest compatible package when an exact recipe version is absent.
