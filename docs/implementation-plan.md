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
