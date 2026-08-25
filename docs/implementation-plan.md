# Milestone M9 Production Blueprints Implementation Plan

**Goal:** Ship exactly three deterministic, checksummed production blueprints and validate generated projects with their certified Windows toolchains.

**Status:** M9 design approved; Task 1 is the current implementation slice.

**Architecture:** Versioned static packages are shipped as immutable built-in content. Desktop composes a `BuiltIn` source and the existing `Local` source through guarded workspaces. Blueprint actions remain declarative and all external tools pass through a closed `IProcessRunner` vocabulary.

**Tech stack:** .NET SDK 10.0.302, C# 14, WPF/.NET 10, Node/pnpm and Python/uv versions pinned after official-source verification, EF Core SQLite, xUnit 2.9.3.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-25-m9-production-blueprints-design.md`
- Task plan: `docs/superpowers/plans/2026-08-25-m9-production-blueprints.md`
- Decision: `docs/decisions/0015-versioned-static-built-in-blueprints.md` (Task 1)

## Current scope and progress

- [x] Read the complete baseline and isolate M9 from M10-M11.
- [x] Approve the production-blueprint design.
- [ ] Task 1: built-in distribution, catalog composition, and production contract harness.
- [ ] Task 2: WPF production blueprint.
- [ ] Task 3: closed pnpm vocabulary and React production blueprint.
- [ ] Task 4: Python/uv boundary and Python CLI production blueprint.
- [ ] Task 5: shared handoff and engine-owned run evidence.
- [ ] Task 6: cross-blueprint integration and closure.

## Current Task 1 boundary

**Scope:** establish immutable build-output package distribution, register built-in and trusted-local sources with exact provenance, and add a reusable real-loader contract harness. No production blueprint content, new process tool, or large UI change is included yet.

**Expected files:** BuiltIn/Desktop project composition, `DesktopBlueprintSourceRegistry`, Blueprint production tests, Desktop host/source tests, architecture tests, `blueprints/README.md`, ADR-0015, and milestone status.

**Tests:** exact output location; two-source provenance; missing/reparse/ambiguous root refusal; exact MVP ID/version directory contract; complete checksum declaration; local source cannot self-assign built-in trust; no direct Desktop filesystem enumeration.

**Task 1 exit:** focused Blueprint/Desktop/architecture tests, format, locked restore, Release build, affected full tests, diff checks, and review pass before a scoped local commit.

## M9 exit gate

M9 exits only when Desktop discovers exactly the three MVP packages, package/schema/rule/action/checksum/handoff contracts pass, planning and expected trees are deterministic, every generated project passes its real certified toolchain matrix, Git cleanliness is proven, and locked restore/format/build/full tests/EF/security/privacy gates pass. M10 remains untouched.
