# Milestone M9 Production Blueprints Implementation Plan

**Goal:** Ship exactly three deterministic, checksummed production blueprints and validate generated projects with their certified Windows toolchains.

**Status:** M9 Tasks 1-4 complete and locally verified; Task 5 is the next implementation slice.

**Architecture:** Versioned static packages are shipped as immutable built-in content. Desktop composes a `BuiltIn` source and the existing `Local` source through guarded workspaces. Blueprint actions remain declarative and all external tools pass through a closed `IProcessRunner` vocabulary.

**Tech stack:** .NET SDK 10.0.302, C# 14, WPF/.NET 10, Node/pnpm and Python/uv versions pinned after official-source verification, EF Core SQLite, xUnit 2.9.3.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-25-m9-production-blueprints-design.md`
- Task plan: `docs/superpowers/plans/2026-08-25-m9-production-blueprints.md`
- Decision: `docs/decisions/0015-versioned-static-built-in-blueprints.md` (Task 1)
- Decision: `docs/decisions/0016-closed-production-blueprint-validation.md` (Task 2)
- Decision: `docs/decisions/0017-static-react-blueprint-and-closed-pnpm.md` (Task 3)
- Decision: `docs/decisions/0018-static-python-cli-blueprint-and-closed-uv.md` (Task 4)

## Current scope and progress

- [x] Read the complete baseline and isolate M9 from M10-M11.
- [x] Approve the production-blueprint design.
- [x] Task 1: built-in distribution, catalog composition, and production contract harness.
- [x] Task 2: WPF production blueprint.
- [x] Task 3: closed pnpm vocabulary and React production blueprint.
- [x] Task 4: Python/uv boundary and Python CLI production blueprint.
- [ ] Task 5: shared handoff and engine-owned run evidence.
- [ ] Task 6: cross-blueprint integration and closure.

## Completed Task 2 boundary

**Scope:** deliver only `desktop.csharp-wpf-tool` manifest version `1.0.0`, using the established .NET boundary and adding only a closed publish-smoke operation. React, Python, shared run-evidence expansion, and M10 remain out of scope.

**Expected files:** `blueprints/desktop.csharp-wpf-tool/**`, WPF production package/expected-tree tests, narrowly scoped process action/handler tests and production changes for `dotnet publish`, plus a generated-project E2E fixture.

**Tests:** exact package shape/checksum/identity; deterministic plan/hash/tree; typed inputs and Windows/.NET compatibility; required handoff docs; native WPF MVVM/Clean Architecture graph; nullable/analyzers/Host/DI/logging/config; central pinned packages and locks; publish profile; forbidden web/browser/secret surfaces; exact closed publish arguments and arbitrary option refusal; real restore/format/build/test/publish smoke.

**Task 2 exit:** satisfied locally. The checksummed package loads through the production catalog; planning is deterministic; two composed executions produce the same tree digest; the closed publish validator rejects target/profile mutations and handler-boundary bypasses; an independently located generated solution passes locked restore, format, Release build, unit test, and publish smoke.

## Current Task 3 boundary

**Scope:** add only the closed pnpm operations needed by `web.react-vite-ts` manifest version `1.0.0`, then deliver its checksummed static skeleton and real Windows Node/pnpm matrix. Python/uv, shared run-evidence expansion, M10, online scaffolders, arbitrary scripts, registries, credentials, and lifecycle-script enablement remain out of scope.

**Expected files:** narrow process/tool policy changes and regression tests, `blueprints/web.react-vite-ts/**`, React production contracts, and a composed generated-project E2E fixture.

**Tests:** certified Node/pnpm compatibility; exact pinned `package.json` and `pnpm-lock.yaml`; strict TypeScript, alias, lint/format, environment boundary, Vitest and production build; deterministic plan/tree; complete handoff documents; frozen install with scripts disabled; exact immutable lint/typecheck/test/build vocabulary; rejection of exec/dlx/evaluation/config/registry/credential/lifecycle escape surfaces.

**Task 3 exit:** composed generation and deterministic tree gates pass, then a fresh standalone generated project passes frozen pnpm install, lint, typecheck, test, and build with the certified toolchain before affected/full DevForge gates and a scoped local commit.

Task 3 exit is satisfied locally. The package loads through the production checksummed catalog, planning is deterministic across target roots, two composed executions have identical plan hashes and tree digests, and the independently located generated project passes frozen script-disabled install, Prettier verification, lint, strict typecheck, two Vitest tests, and Vite production build on Node 22.21.1/pnpm 10.24.0. The full DevForge test matrix passes after serializing process-wide execution E2E fixtures through a non-parallel test collection.

## Completed Task 4 boundary

**Scope:** add only the closed uv operations required by `tool.python-cli` manifest version `1.0.0`, then deliver its checksummed static skeleton and real Windows Python/uv matrix. Shared engine-owned run evidence, M10, online project generators, arbitrary Python/module execution, custom indexes, credentials, activation scripts, and install hooks remain out of scope.

**Expected files:** narrow process/tool policy changes and regression tests, `blueprints/tool.python-cli/**`, Python production contracts, and a composed generated-project E2E fixture.

**Tests:** certified Python/uv compatibility; exact pinned `pyproject.toml` and `uv.lock`; `src` layout, Ruff format/lint, mypy strict checking, pytest coverage, deterministic plan/tree, complete handoff documents, frozen install; exact immutable validation vocabulary; rejection of module/eval/index/config/credential/hook escape surfaces.

**Task 4 exit:** composed generation and deterministic tree gates pass, then a fresh standalone generated project passes frozen uv sync, format check, lint, typecheck, test, and build/package smoke with the certified toolchain before affected/full DevForge gates and a scoped local commit.

Task 4 exit is satisfied locally. Python/uv have typed identities and fixed environment probes; Python raw evaluation/module modes fail closed; package installation and all six uv validators use exact reviewed argument lists. The static package carries exact build/quality pins, the frozen lock, checksums, typed configuration/logging, tests, and handoff documents. Two composed runs prove identical plan hashes and tree digests, while an independent rendered tree passes the certified CPython 3.14.6/uv 0.12.1 matrix. Locked restore, format, Release build, all 1,395 DevForge tests, EF consistency, checksum, security, and diff gates pass.

## M9 exit gate

M9 exits only when Desktop discovers exactly the three MVP packages, package/schema/rule/action/checksum/handoff contracts pass, planning and expected trees are deterministic, every generated project passes its real certified toolchain matrix, Git cleanliness is proven, and locked restore/format/build/full tests/EF/security/privacy gates pass. M10 remains untouched.
