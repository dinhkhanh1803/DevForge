# Milestone M8 Git and GitHub Completion Implementation Plan

**Goal:** Convert reviewed `LocalReady` projects into recoverable, evidence-backed Git/GitHub completion through trusted CLI boundaries.

**Status:** M8 design independently approved; Tasks 1-2 complete and Task 3 Git CLI service is next.

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
- [ ] Implement closed Git and GitHub CLI services through `IProcessRunner`.
- [ ] Implement recoverable post-finalization publication orchestration.
- [ ] Enable reviewed Git intent and Desktop completion UX.
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
