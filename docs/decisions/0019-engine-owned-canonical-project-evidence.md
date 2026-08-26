# ADR-0019: Engine-owned canonical project evidence

## Status

Accepted for M9 Task 5 on 2026-08-26.

## Context

Every production blueprint must hand a team both truthful project documentation and durable evidence of what DevForge reviewed, planned, validated, and generated. Letting blueprint packages author that evidence would allow package content to forge engine results. Writing evidence after finalization would also exclude it from the final-tree and publication digest boundary.

Retries can observe a partially completed four-file atomic sequence after interruption. A retry must continue only when every existing evidence file is byte-identical to the canonical bytes derived from the persisted checkpoint and report. It must never overwrite or adopt different bytes.

## Decision

DevForge owns exactly these target-relative files, in canonical order:

1. `.devforge/project.recipe.yaml`
2. `devforge.lock.json`
3. `generation-report.json`
4. `policy.snapshot.json`

Blueprint packages cannot author any canonical evidence filename, and the planner revalidates resolved output targets after interpolation so a variable cannot bypass that reservation. The completion coordinator builds all four bounded UTF-8 payloads in memory after validation and the whole-payload scan, rejects secret-shaped evidence before persistence, and writes only through the guarded atomic workspace contract before finalization. Existing canonical bytes are adopted during retry; an existing directory, oversized file, or byte mismatch fails closed without writing any missing file.

The recipe records effective reviewed inputs, selected features, the complete reviewed Git intent, project-name provenance, the canonical team-profile snapshot and its `recorded`/`none`/`not-recorded` status, and exact blueprint identity. Missing legacy context is emitted as `null`; the writer never fabricates a project name or treats unavailable historical team provenance as a reviewed no-team choice. The lock binds the plan hash, blueprint version and checksum, truthful engine-version provenance, dependencies, detected tool policy, generated artifacts, and SHA-256 digests for the recipe, report, and policy without creating a self-referential digest cycle.

The target report uses the distinct `devforge-project-generation-report-v1` schema and declares its immutable historical capture phase as `validated-pre-finalization`; the existing run-artifact `devforge-generation-report-v1` schema remains unchanged. The project report records deterministic engine provenance, blueprint identity, tool statuses, step outcomes, persisted duration milliseconds, safe warnings/errors, validation severity and requiredness, validation output digests, and the reviewed artifact summary. Mutable run, finalization, and report-persistence states are deliberately excluded, so generating the same report before intent persistence, after intent persistence, or after finalization produces identical bytes. Absolute timestamps and private technical diagnostics do not enter target evidence. The policy snapshot records selected features and the exact typed step, validator, dependency, and tool policy. The write receipt binds all four canonical paths to their individual SHA-256 digests.

Artifact evidence, tool statuses, and warnings are derived only from the authoritative bounded, persisted plan preview; the request preview is a legacy fallback only when no preview was historically persisted. A `BlueprintArtifact` declares one generated file, including extensionless files, never a directory. Completion canonicalizes and checks every declared file path through the guarded workspace API and fails closed when any reviewed artifact is missing, is directory-only, or is reserved; it never enumerates the installed payload to build the report. Package loading and planning reserve only exact canonical engine output/action/artifact paths, so unrelated unused package source files with the same basename remain harmless.

On completion resume, every validator and the whole-payload secret scan run again because the ownership marker does not bind the current payload bytes. Previously persisted successful validator evidence means `Passed`, or `Warning` for an optional validator. It is retained, including original timing and safe error metadata, only when the rerun has the exact same status and output digest. Secret-scan evidence remains reusable only for an exact repeated pass. A changed status or digest fails closed before finalization with a stable redacted error. Failed required evidence is never reused for a manual retry. This keeps kill-window recovery byte-identical without trusting an unverified staged payload.

Checkpoints created before Task 5 may not contain `engine.version`. Resume never substitutes the current engine version for missing historical provenance. Their target generation report writes JSON `null` with `engineVersionStatus` set to `not-recorded`; newly planned runs continue to bind their validated engine version in deterministic plan context.

New timed execution evidence enforces status-consistent safe error metadata: a pass has no error, while warning/failure evidence requires a bounded code and redacted summary. Runtime execution always captures one completion timestamp and uses the persisted attempt start/completion for strict timed step evidence. The internal, persistence-only `RehydrateLegacy` boundary decodes the exact historical four-property JSON shape without retroactively inventing timing or error values; no public factory can create untimed evidence, and partially upgraded shapes fail closed.

All four files enter the staging payload before the existing atomic finalizer computes the final-tree digest. Publication therefore continues to verify the same final-tree boundary; no publication contract or raw filesystem escape is added.

## Consequences

- Successful generated projects carry reviewable, integrity-bound evidence alongside the seven handoff documents.
- Same persisted plan/report state produces byte-identical evidence and final trees; volatile timestamps and run identities are excluded from target evidence.
- Interrupted writes are recoverable without weakening overwrite or tamper protection.
- Engine version becomes deterministic plan context and therefore participates in the plan hash.
- Additional evidence formats or target paths require an explicit contract and ADR change; M10 packaging and M11 catalog expansion remain out of scope.
