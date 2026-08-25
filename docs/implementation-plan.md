# Milestone M9 Production Blueprints Implementation Plan

**Goal:** Ship exactly three deterministic, checksummed production blueprints and validate generated projects with their certified Windows toolchains.

**Status:** M9 Task 1 complete and locally verified; Task 2 is the current implementation slice.

**Architecture:** Versioned static packages are shipped as immutable built-in content. Desktop composes a `BuiltIn` source and the existing `Local` source through guarded workspaces. Blueprint actions remain declarative and all external tools pass through a closed `IProcessRunner` vocabulary.

**Tech stack:** .NET SDK 10.0.302, C# 14, WPF/.NET 10, Node/pnpm and Python/uv versions pinned after official-source verification, EF Core SQLite, xUnit 2.9.3.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-25-m9-production-blueprints-design.md`
- Task plan: `docs/superpowers/plans/2026-08-25-m9-production-blueprints.md`
- Decision: `docs/decisions/0015-versioned-static-built-in-blueprints.md` (Task 1)

## Current scope and progress

- [x] Read the complete baseline and isolate M9 from M10-M11.
- [x] Approve the production-blueprint design.
- [x] Task 1: built-in distribution, catalog composition, and production contract harness.
- [ ] Task 2: WPF production blueprint (active).
- [ ] Task 3: closed pnpm vocabulary and React production blueprint.
- [ ] Task 4: Python/uv boundary and Python CLI production blueprint.
- [ ] Task 5: shared handoff and engine-owned run evidence.
- [ ] Task 6: cross-blueprint integration and closure.

## Current Task 2 boundary

**Scope:** deliver only `desktop.csharp-wpf-tool` manifest version `1.0.0`, using the established .NET boundary and adding only a closed publish-smoke operation. React, Python, shared run-evidence expansion, and M10 remain out of scope.

**Expected files:** `blueprints/desktop.csharp-wpf-tool/**`, WPF production package/expected-tree tests, narrowly scoped process action/handler tests and production changes for `dotnet publish`, plus a generated-project E2E fixture.

**Tests:** exact package shape/checksum/identity; deterministic plan/hash/tree; typed inputs and Windows/.NET compatibility; required handoff docs; native WPF MVVM/Clean Architecture graph; nullable/analyzers/Host/DI/logging/config; central pinned packages and locks; publish profile; forbidden web/browser/secret surfaces; exact closed publish arguments and arbitrary option refusal; real restore/format/build/test/publish smoke.

**Task 2 exit:** WPF package/process/composed tests and the real generated toolchain matrix pass; expected tree/digests match across two runs; locked restore, format, Release build, affected/full regressions, EF consistency, diff/security checks, and review pass before a scoped local commit.

## M9 exit gate

M9 exits only when Desktop discovers exactly the three MVP packages, package/schema/rule/action/checksum/handoff contracts pass, planning and expected trees are deterministic, every generated project passes its real certified toolchain matrix, Git cleanliness is proven, and locked restore/format/build/full tests/EF/security/privacy gates pass. M10 remains untouched.
