# ADR-0012: Reviewed-plan-driven project creation

**Status:** Accepted

**Date:** 2026-08-14

## Context

M7 must expose M4 planning and M5 recoverable execution through the native WPF shell without allowing mutable form state, unguarded paths, or presentation-owned workspace handles to influence execution after review. The UX must remain useful in read-only safe mode, preserve exact recovery evidence, and stop at `LocalReady` until M8 supplies Git and GitHub completion.

## Decision

- Project creation has four explicit stages: Configure, Review Plan, Execute, and LocalReady. `Create & Validate` executes only the immutable reviewed `ProjectCreationPlanSnapshot`; every relevant form edit invalidates that snapshot and returns to Configure.
- Application owns creation and recovery workflows. Desktop sends typed drafts, plan snapshots, run IDs, and user intent only. Infrastructure opens guarded target, artifact, final-project, and staging workspaces behind Application ports.
- Target preflight requires an absolute guarded parent and an absent output target. Invalid, reserved, reused, non-empty, or reparse-shaped targets fail before run artifacts or staging mutation.
- Blueprint input controls are selected from the closed Text, Choice, Boolean, and WholeNumber schema kinds. Adding another supported field instance requires no Create Project XAML change; unsupported kinds fail closed.
- Presets are versioned, canonical, bounded, and privacy-safe. They retain blueprint/version, typed inputs, features, and IDE intent, but never target handles, runs, plans, process output, credentials, `.env` content, or source content.
- Catalog execution is limited to current BuiltIn and TrustedLocal packages. Review displays the exact trust, plan hash, ordered actions, validators, artifacts, tools, inputs, features, and warnings.
- Execution Center exposes exact Cancel, Resume, Retry, and Cleanup eligibility from Application recovery inspection. It retains bounded redacted progress and never retains workspace handles. Open Staging and Support Bundle remain disabled until M10.
- LocalReady displays the authoritative target, blueprint, plan hash, elapsed attempts, ordered evidence, warnings, and exact JSON/Markdown report references. It never presents Domain `Completed` without M8 completion evidence.
- A temporary TrustedLocal E2E package proves all four input kinds, guarded create/render/copy actions, non-process validators, secret scanning, report persistence, finalization, cancellation, and resume. It is test-only and does not expand the production catalog.

## Consequences

Users approve a deterministic plan before any generation mutation, and the same hashed plan and exact blueprint fingerprint drive execution and recovery. Desktop remains a native projection layer; all filesystem, persistence, process, and recovery decisions stay behind guarded typed ports. Safe mode can inspect local state but cannot provision sources, plan, execute, resume, retry, cleanup, save settings, or rescan.

## Deferred scope

- M8 activates Git initialization, branch policy, Git provider selection, GitHub authentication/publish, `PublishPending`, and true `Completed` transitions.
- M9 supplies the three production MVP blueprints. The M7 package remains test-only.
- M10 supplies support bundles, production log browsing, Open Staging/folder handoff, packaging, and release hardening.

## Rejected alternatives

- Executing directly from mutable form state without Review Plan.
- Letting Desktop construct or retain filesystem workspaces, process requests, EF contexts, or Infrastructure implementations.
- Silently upgrading a missing preset blueprint or resuming against a changed package fingerprint.
- Treating LocalReady as Completed before Git/GitHub evidence exists.
- Shipping the E2E fixture as a production blueprint or expanding the catalog early.
