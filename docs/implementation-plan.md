# Milestone M9 Production Blueprints Implementation Plan

**Goal:** Ship exactly three deterministic, checksummed production blueprints and validate generated projects with their certified Windows toolchains.

**Status:** M9 Tasks 1-6 are implemented and locally verified. The required Windows 11 release certification remains open because the available host is Windows 10 build 19045; M10 must not start until that matrix is run and green.

**Architecture:** Versioned static packages are shipped as immutable built-in content. Desktop composes a `BuiltIn` source and the existing `Local` source through guarded workspaces. Blueprint actions remain declarative and all external tools pass through a closed `IProcessRunner` vocabulary.

**Tech stack:** .NET SDK 10.0.302, C# 14, WPF/.NET 10, Node/pnpm and Python/uv versions pinned after official-source verification, EF Core SQLite, xUnit 2.9.3.

## Source and detailed plan

- Design: `docs/superpowers/specs/2026-08-25-m9-production-blueprints-design.md`
- Task plan: `docs/superpowers/plans/2026-08-25-m9-production-blueprints.md`
- Decision: `docs/decisions/0015-versioned-static-built-in-blueprints.md` (Task 1)
- Decision: `docs/decisions/0016-closed-production-blueprint-validation.md` (Task 2)
- Decision: `docs/decisions/0017-static-react-blueprint-and-closed-pnpm.md` (Task 3)
- Decision: `docs/decisions/0018-static-python-cli-blueprint-and-closed-uv.md` (Task 4)
- Decision: `docs/decisions/0019-engine-owned-canonical-project-evidence.md` (Task 5)

## Current scope and progress

- [x] Read the complete baseline and isolate M9 from M10-M11.
- [x] Approve the production-blueprint design.
- [x] Task 1: built-in distribution, catalog composition, and production contract harness.
- [x] Task 2: WPF production blueprint.
- [x] Task 3: closed pnpm vocabulary and React production blueprint.
- [x] Task 4: Python/uv boundary and Python CLI production blueprint.
- [x] Task 5: shared handoff and engine-owned run evidence.
- [x] Task 6: cross-blueprint integration and local closure.

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

## Completed Task 5 boundary

**Scope:** enforce one truthful seven-document handoff contract across the three production blueprints and add the smallest engine-owned writer that persists canonical `.devforge/project.recipe.yaml`, `devforge.lock.json`, `generation-report.json`, and `policy.snapshot.json`. Blueprint packages cannot author or overwrite this namespace. M10, additional blueprints, deployment automation, and publication-boundary changes remain out of scope.

**Expected files:** shared BlueprintTests handoff contracts; narrow Application/Infrastructure run-evidence contracts, serializers, and composition changes; completion/recovery/privacy/tamper tests; the three packages' documentation assets only where the shared contract exposes a real gap; one ADR for the evidence ownership and digest boundary.

**Tests:** all seven handoff documents have required truthful sections and blueprint-specific commands; `.env.example` carries names only and `.env` remains ignored; the four evidence files bind blueprint identity/version/checksum, plan hash, selected features, exact dependency/tool policy, validations, and generated artifacts; repeated writes are byte-identical; non-canonical paths, package-forged evidence, overwrite attempts, secret-shaped content, partial-write recovery, and tampering fail closed; the existing final-tree/publication digest stays stable.

**Task 5 exit:** every successful generated run contains the complete handoff set plus integrity-bound engine evidence; recovery produces the same bytes without accepting forged state; focused and full format/restore/build/test gates pass before a scoped local commit.

Task 5 exit is satisfied locally. All three packages meet the shared truthful handoff contract. The completion boundary persists the four canonical engine-owned files from the authoritative persisted preview, uses bounded reviewed generated-file evidence, records explicit engine/project/team legacy provenance, and preserves byte-identical recovery across each atomic evidence write and finalization-intent kill window. Spec and quality/security review report no remaining findings; locked restore, format, Release build, all 1,445 tests, EF consistency, and scoped integrity/security checks pass.

## Completed Task 6 boundary

**Scope:** consolidate the production release matrix for exactly `desktop.csharp-wpf-tool`, `web.react-vite-ts`, and `tool.python-cli`; prove each package through the real production catalog, review/planning, guarded execution, validation, finalization, deterministic output, failure recovery, and optional M8 local-Git composition; close M9 documentation only from observed gate evidence. M10 behavior, additional blueprints, remote publication, cloud/AI/browser surfaces, and expanded command vocabularies remain out of scope.

**Expected files:** a cohesive `tests/DevForge.E2ETests/M9/ProductionBlueprintReleaseMatrixE2ETests.cs`, narrowly reusable M9 fixture/snapshot helpers, consolidated exact expected-path/digest data for all three packages, and only regression production changes directly exposed by RED. `docs/implementation-status.md`, `README.md`, and `CHANGELOG.md` are updated only after the final closure controller supplies exact full-gate and real-toolchain evidence.

**RED tests:** Desktop/build-output discovery returns exactly the three production IDs; every blueprint completes production review, deterministic planning, guarded generation, validation, and finalization; identical reviewed inputs produce identical plan hashes and exact trees; changed reviewed inputs change the hash and rendered output; occupied final targets remain untouched and failed execution remains recoverable through its owned cleanup boundary; optional local Git creates one exact clean repository without a remote; repository-bound scans reject shell/admin, unbounded latest/wildcard dependency, token/secret, and forbidden platform surfaces.

**Task 6 exit:** focused M9 E2E passes with zero failed or skipped tests, then the controller runs locked restore, format verification, Release build, all four test projects, the real WPF/React/Python generated-project matrices, EF pending-model verification, architecture/privacy/security scans, `git diff --check`, and clean status after the scoped local commit. Only exact observed results may close M9 and recommend M10; any unexecuted environment matrix item is recorded as deferred rather than green.

Task 6 implementation and the available-host matrix are satisfied locally. The consolidated production test discovers exactly three built-in packages and proves deterministic review, planning, execution, evidence, recovery, exact command vocabulary, no-overwrite finalization, and production local-Git verification without a remote. Fresh generated WPF, React, and Python projects pass their real pinned toolchains on Windows 10 build 19045. The React matrix also proves its integrity-bound `dist` output is byte-stable across a second real build and leaves Git clean. The final Windows 11 WPF/React/Python certification required by the approved M9 design has not been executed on this host, so the milestone release gate remains open and M10 is not yet authorized.

## Post-Task 6 Desktop launch regression

**Scope:** correct only the WPF build-item classification that makes a shipped blueprint `App.xaml` collide with DevForge Desktop's own application resource. Blueprint bytes and output layout, catalog behavior, M9 packages, and M10 remain unchanged.

**Expected files:** `src/DevForge.Desktop/DevForge.Desktop.csproj`, one Desktop runtime-packaging regression test, this plan, and implementation status.

**Test and exit gate:** first prove the built Desktop assembly incorrectly advertises root `app.xaml`/`app.xaml.cs` content; then require blueprint payload to copy as non-WPF `None` items, rebuild, run the focused regression and full suite, and launch the Release executable long enough to confirm the WPF process remains alive without an unhandled startup exception.

## M9 exit gate

M9 exits only when Desktop discovers exactly the three MVP packages, package/schema/rule/action/checksum/handoff contracts pass, planning and expected trees are deterministic, every generated project passes its real certified toolchain matrix, Git cleanliness is proven, and locked restore/format/build/full tests/EF/security/privacy gates pass. M10 remains untouched.
