# Milestone M8 Git and GitHub Completion Implementation Plan

**Goal:** Convert reviewed `LocalReady` projects into recoverable, evidence-backed Git/GitHub completion through trusted CLI boundaries.

**Status:** M8 design independently approved; Tasks 1-6 complete and Task 7 Desktop completion UX is next.

**Architecture:** Application owns post-finalization publication and durable state; Infrastructure implements closed Git/`gh` operations over `IProcessRunner` and guarded workspaces; Desktop projects immutable publication status and actions. The persisted reviewed plan remains authoritative.

**Tech stack:** .NET SDK 10.0.302, C# 14, WPF/.NET 10, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.Hosting 10.0.10, EF Core SQLite, xUnit 2.9.3.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-14-m8-git-github-publishing-design.md`
- Task plan: `docs/superpowers/plans/2026-08-14-m8-git-github-publishing.md`
- Decision to create at closure: `docs/decisions/0013-recoverable-git-github-completion.md`

## Current scope and progress

- [x] Read the complete baseline and isolate M8 from M9-M11.
- [x] Define files, RED/GREEN tests, commit boundaries, and exit gate before code.
- [x] Add publication domain/Application contracts and checkpoint invariants.
- [x] Persist integrity-bound publication state through a versioned migration.
- [x] Implement closed Git and GitHub CLI services through `IProcessRunner`.
- [x] Implement recoverable post-finalization publication orchestration.
- [x] Enable reviewed Git intent in Create Project contracts, presets, and plans.
- [ ] Enable Desktop completion UX.
- [ ] Close integration, security, privacy, migration, and full-solution gates.

## Exit gate

M8 exits only after the reviewed Git intent is hash-bound; Git is clean with the exact bootstrap commit and branch policy; private-by-default GitHub publication is evidence-backed; failure/cancellation remains recoverable as `PublishPending`; retry duplicates neither generation nor commit; no token/force/delete/shell path exists; and every full/focused/EF/WPF/privacy gate passes. Production blueprints remain M9.

## Task 3 execution boundary

**Scope:** production local Git only. Implement `Infrastructure/Git` behind the existing `IGitService`; reuse the finalizer's bounded ordinal tree digest; invoke only typed Git operations through `IProcessRunner`; register the service in Desktop composition. GitHub, publication orchestration, reviewed-input UI, receipt writing, and production blueprints remain out of scope.

**Expected files:** `src/DevForge.Infrastructure/Git/*`, the shared canonical project-tree helper extracted from `AtomicProjectFinalizer`, `DesktopHostBuilder`, and focused Unit/Integration tests.

**TDD matrix:** exact separated arguments and minimal environment; no shell/token/force/delete surface; pre-existing/nested repository refusal; final-tree drift and secret finding refusal before mutation; fixed author/message; clean status; exact commit/branch evidence; timeout/cancellation mapping; and phase-specific adoption after init/add/commit/develop/main kill windows. The real integration fixture uses only a guarded temporary workspace and verifies user/global Git configuration is untouched.

**Task 3 exit:** focused Git security/recovery tests, the real local Git integration matrix, affected finalizer/process/DI regressions, format, Release build, full solution tests, EF consistency, and diff checks pass before a scoped local commit. No push.

**Completion:** implemented and independently approved on 2026-08-14. The next bounded slice is Task 4, the production GitHub CLI service; no GitHub behavior is included in Task 3.

## Task 4 execution boundary

**Scope:** production GitHub CLI only. Implement `Infrastructure/GitHub` behind the existing `IGitHubService`, extend the closed local Git command vocabulary only for exact `origin` inspection/addition and ordinary required-branch pushes, and register the service in Desktop composition. Publication checkpoint orchestration, WPF controls, receipt writing, production blueprints, and real remote creation remain out of scope.

**Expected files:** `src/DevForge.Infrastructure/GitHub/*`, narrowly scoped typed additions to `Infrastructure/Git/GitCommandFactory`, `DesktopHostBuilder`, deterministic Integration tests, and DI regression tests.

**TDD matrix:** fixed `github.com` auth/account verification; minimal typed environment containing no token; strict bounded output/JSON/ref parsing; private default and exact public opt-in; ownership nonce marker in atomic create; exact personal owner/name/visibility/HTTPS URL; empty/partial/complete nonce-owned remote recovery; exact local `origin`; ordinary non-force push of missing branches; missing/mismatched nonce, organization/fork/archive, unexpected refs/commits/origin, auth/network/timeout/cancellation, injection strings, and forbidden token/force/delete/switch/login operations.

**Task 4 exit:** focused GitHub command/auth/recovery tests, affected Git/DI regressions, format, locked restore, Release build, full solution tests, EF consistency, diff checks, and independent review pass before a scoped local commit. Tests use a deterministic runner and never contact GitHub. No push.

**Completion:** implemented and independently approved on 2026-08-14. The next bounded slice is Task 5, the recoverable publication workflow; Desktop controls and reviewed-input enablement remain deferred.

## Task 5 execution boundary

**Scope:** Application-owned post-finalization publication only. Acquire the shared in-process activity gate and a guarded OS-exclusive publication lease, reload the authoritative checkpoint, validate the immutable reviewed intent and finalized target, drive the existing typed Git/GitHub services, persist every publication phase, and atomically publish the integrity-bound receipt. Create Project inputs and WPF presentation remain Tasks 6-7; production blueprints remain M9.

**Expected files:** `src/DevForge.Application/Publication/*`, minimal publication/lease/workspace contracts, `src/DevForge.Infrastructure/Publication/*`, Desktop DI registration, and focused Unit/Integration tests. Existing checkpoint codecs are reused unless a narrowly required invariant is missing.

**TDD matrix:** LocalReady eligibility and authoritative reload; shared activity and cross-process lease contention before mutation; safe-mode refusal; finalized target/reparse/tree-digest/secret drift refusal; Git-only and GitHub phase persistence; cancellation/failure normalized to durable `PublishPending`; retry without generation or duplicate commit; kill windows after intent, repository init, commit, branch creation, remote creation, origin creation, each push, receipt intent, receipt write, and receipt success; exact orphan receipt adoption; mismatch/non-canonical/non-overwrite refusal; terminal `Completed` invariants.

**Task 5 exit:** focused Application publication and Infrastructure lease/receipt tests, affected Git/GitHub/checkpoint/persistence regressions, format, locked restore, Release build, full solution tests, EF consistency, diff checks, and independent review pass before a scoped local commit. Tests do not create a real remote and no push is part of this task.

**Completion:** implemented and independently approved on 2026-08-14. The coordinator now holds the shared activity gate and guarded cross-process lease, persists every irreversible Git/GitHub/receipt phase with cancellation-independent writes, revalidates persisted success evidence before terminal completion, and adopts only byte-exact orphan receipts. The next bounded slice is Task 6, enabling reviewed Git intent in Create Project; WPF publication presentation remains Task 7.

## Task 6 execution boundary

**Scope:** Application creation contracts, workflow, preset codec, and planner/hash integration only. Add immutable reviewed Git initialization, branch policy, optional personal-account GitHub publication, repository identity, and visibility to the Create Project draft; remove the M7 Git-disabled invariant; and preserve the exact options through recipe, preview, plan hash, checkpoint, and backward-compatible presets. WPF controls, execution-to-publication composition, `PublishPending`/`Completed` presentation, production blueprints, and real remote behavior remain Tasks 7-9.

**Expected files:** `src/DevForge.Application/Contracts/CreationContracts.cs`, `src/DevForge.Application/Creation/{ProjectCreationWorkflow,ProjectCreationPresetCodec}.cs`, narrowly required planner/hash validation, matching Creation/Planning UnitTests, preset persistence compatibility tests, and milestone status documentation.

**TDD matrix:** Git-on/publish-off/private-on defaults; closed `main` versus `main + develop`; publish requires Git plus canonical personal account/repository; public visibility is an explicit reviewed publish choice; invalid combinations aggregate before target preflight/planner/workspace mutation; draft/recipe/preview Git equality; every reviewed Git option changes the plan hash; v2 preset deterministic round-trip; legacy v1 preset upgrade to safe defaults; unknown/invalid/sensitive fields fail closed; collection snapshots and plan invalidation remain immutable.

**Task 6 exit:** focused Creation/Planning/preset tests and persistence compatibility regressions, format, locked restore, Release build, full solution tests, EF consistency, diff checks, and a scoped static security/design review before a local commit. No WPF publication controls, Git/GitHub command execution, remote access, or push is part of this task.

**Completion:** implemented and statically reviewed on 2026-08-14. New drafts default to Git on, `main`, publish off, and private visibility; public visibility requires an explicit reviewed GitHub publication. Exact Git intent is immutable across draft, recipe, preview, hash, and execution snapshot. Preset schema v2 stores the intent deterministically, while v1 presets decode to the new safe defaults and still require plan review. The next bounded slice is Task 7, Desktop completion UX and publication composition.
