# M8 Git and GitHub Completion Implementation Plan

**Goal:** Convert reviewed `LocalReady` projects into recoverable, evidence-backed Git/GitHub completion without rerunning generation or exposing credentials.

**Design:** `docs/superpowers/specs/2026-08-14-m8-git-github-publishing-design.md`

**Excluded:** production blueprints (M9), CI automation and release hardening (M10), catalog expansion (M11), arbitrary remotes/commands, token handling, force push, and remote deletion.

Each task begins with focused RED tests, implements the smallest production slice, runs focused GREEN plus affected regression suites, and ends in a scoped local commit. No push is part of this plan.

## Task 1: Publication domain and Application contracts

**Expected files:** `src/DevForge.Domain/Runs/*`, `src/DevForge.Application/Contracts/{Git,GitHub,Publication,ExecutionCheckpoint}Contracts.cs`, matching UnitTests.

- Add immutable guarded publication phase/evidence models, final-tree/receipt digests, fixed account/repository identity, and exact lifecycle invariants.
- Replace broad Git/GitHub requests with closed typed operations, fixed bootstrap identity/message, strict personal repository identity, private default, bounded canonical receipts, and no credential-bearing fields.
- Bind publication intent to `PlanPreview.Git`; reject `Completed` without exact required evidence and integrity-bound receipt.
- Test null/enum/bound/hash/URL/integrity aggregation, cancellation contracts, and architecture/privacy surface.

**Exit:** focused Domain/Application tests pass; no Infrastructure behavior yet.

## Task 2: Durable publication checkpoint migration

**Expected files:** finalization receipt/completion coordinator/checkpoint contracts, persistence entity/configuration/mapper/codec, one EF migration and snapshot, Unit/IntegrationTests.

- Update the finalizer result and completion coordinator to capture and persist the exact bounded canonical final-tree digest before `LocalReady`; reject legacy/missing digest for publication.
- Add canonical publication JSON, SHA-256 checksum, final-tree digest, and bounded receipt reference/body checksum.
- Decode old M7 rows as `NotRequested` without upgrading them to `Completed`.
- Reject canonical-content tampering, plan-intent mismatch, invalid state combinations, sensitive shapes, and oversized data.

**Exit:** SQLite round-trip/tamper/backward-compatibility tests and migration consistency pass.

## Task 3: Production Git CLI service

**Expected files:** `src/DevForge.Infrastructure/Git/*`, DI registration, Unit/IntegrationTests.

- Implement only the closed init/add/commit/branch/status/rev-parse vocabulary through `IProcessRunner`.
- Isolate system/global config, templates, hooks, filters, pagers, prompts, signing and line-ending mutation; supply a fixed safe author identity.
- Verify clean worktree, exact final-tree digest, fresh secret scan, exact initial commit, required branches, cancellation, and retry idempotence.
- Reconcile kill-after-init/add/commit/branch windows only for the exact pristine repository, single fixed parentless commit, and reviewed branch subset; reject every unexpected config/commit/branch/tag/remote/dirty path.
- Refuse pre-existing or nested `.git` before intent, exclude only the M8-owned root `.git` after initialization, and map failures to scrubbed stable codes.
- Add a real temporary-workspace local Git integration test without touching user/global Git config.

**Exit:** injection, dirty tree, duplicate commit, branch, timeout/cancel, and real Git integration tests pass.

## Task 4: Production GitHub CLI service

**Expected files:** `src/DevForge.Infrastructure/GitHub/*`, DI registration, Unit/IntegrationTests with deterministic process runner.

- Implement fixed-`github.com` auth/account status, empty repository create, HTTPS origin, ordinary branch push, and exact recovery verification through typed `gh`/`git` requests.
- Enforce reviewed personal account/repository identity, private default, persisted 128-bit ownership nonce in the atomic create description, repository/origin refusal, exact commit identity, bounded URL parsing, isolated minimal auth environment, and no token/force/delete commands.
- Model auth/network/timeout/cancel, nonce-owned empty/partial/complete remote interruption reconciliation, and missing/mismatched nonce or remote failure.

**Exit:** complete command/security/recovery matrix passes; tests create no real GitHub remote.

## Task 5: Recoverable publication workflow

**Expected files:** `src/DevForge.Application/Publication/*`, workspace factory adapter in Infrastructure, Unit/IntegrationTests.

- Acquire a guarded OS-exclusive publication lease, reload the authoritative checkpoint, share the execution activity gate, validate finalized target/tree/plan intent, run a fresh secret scan, and persist intent before mutation.
- Drive Git and optional GitHub phases; persist every state change with `CancellationToken.None` on cancellation/failure.
- Keep failures/cancellation recoverable as `PublishPending`; retry only publication and never generation.
- Persist deterministic receipt intent/reference/body checksum before atomic no-overwrite write, reconcile exact orphan receipts, and transition to `Completed` only after re-reading and verifying required evidence.

**Exit:** app-kill phase matrix, retry/no-duplication, target drift/reparse, safe-mode, cancellation, and terminal invariants pass.

## Task 6: Enable reviewed Git intent in Create Project

**Expected files:** creation contracts/workflow/preset codec/planner tests and persistence compatibility tests.

- Add Git initialization, branch policy, publish, reviewed GitHub personal account, repository name, and visibility to the immutable draft and preset format.
- Remove the M7 Git-disabled invariant and bind exact options into recipe, preview, plan hash, and checkpoint.
- Default Git on, publish off, private on; invalid combinations aggregate before target mutation.

**Exit:** every option invalidates preview; round-trip/privacy/backward preset behavior and hash changes pass.

## Task 7: Desktop completion UX and composition

**Expected files:** Create Project, Plan Preview, Execution Center, LocalReady/Completed, Run History, host registration, E2ETests.

- Render reviewed Git controls and immutable preview.
- Continue one-button execution into publication, show `PublishPending`, Retry Publish, auth remediation, and evidence-backed `Completed`.
- Keep Desktop free of process/workspace/persistence orchestration and disable mutation in safe mode.
- Preserve virtualized evidence, automation names, keyboard behavior, and bounded redacted notifications.

**Exit:** exact status/action/safe-mode/accessibility/recovery matrices pass.

## Task 8: M8 integration and closure

**Expected files:** E2E fixture, ADR-0013, implementation plan/status, README, CHANGELOG.

- Prove generate -> validate -> finalize -> local Git clean -> `Completed` without GitHub or a terminal.
- Prove deterministic fake-GitHub private publish, publish failure -> `PublishPending`, app-kill recovery across every local Git phase, nonce-owned empty/partial remotes and orphan receipt windows, cross-process contention, no duplicate generation/commit, and non-overwrite.
- Run locked restore, format verify, Release build, full/focused tests, EF migration consistency, diff check, and architecture/privacy scans.
- Record only observed results and recommend M9 only after all gates pass.

**Exit:** all M8 acceptance and security gates green; clean worktree; local commits only.
