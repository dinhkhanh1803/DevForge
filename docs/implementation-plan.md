# Milestone M7 Create and Execute UX Implementation Plan

**Goal:** Expose trusted blueprint selection, schema-driven recipe authoring, immutable plan preview, recoverable execution, local-ready evidence, presets, catalog inspection, and run history through the native WPF shell.

**Status:** Approved M7 design and executable TDD plan; implementation is active.

**Architecture:** Application owns the reviewed-plan creation workflow; Infrastructure opens canonical guarded target/artifact workspaces; Desktop projects immutable snapshots into WPF. M4 planner/catalog and M5 orchestrator/recovery remain authoritative.

**Tech stack:** .NET SDK 10.0.302, C# 14, WPF/.NET 10, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.Hosting 10.0.10, EF Core SQLite, xUnit 2.9.3.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-13-m7-create-execute-ux-design.md`
- Task plan: `docs/superpowers/plans/2026-08-13-m7-create-execute-ux.md`
- Decision to create at closure: `docs/decisions/0012-reviewed-plan-driven-project-creation.md`

## Current scope and progress

- [x] Read the complete baseline and approve the M7 design.
- [x] Define files, RED/GREEN tests, commit boundaries, and exit gate before code.
- [ ] Implement immutable creation contracts and guarded target/workspace adapters.
- [ ] Implement privacy-safe preset codec and reviewed-plan workflow.
- [ ] Implement dynamic form, preview, execution center, catalog, history, and LocalReady views.
- [ ] Compose the real M4/M5 graph and safe-mode behavior.
- [ ] Prove real no-terminal generation with a temporary test-only blueprint.
- [ ] Close architecture/privacy/accessibility matrices and the full M7 gate.

## Exit gate

M7 exits only after a schema addition renders without Create Project XAML changes; invalid/untrusted/stale/non-empty cases fail before mutation; preview matches the immutable plan; the real test fixture reaches `LocalReady` without a terminal; cancellation/resume does not duplicate evidence; and all full/focused/EF/WPF/privacy gates pass. Git/GitHub remains M8 and production blueprints remain M9.
