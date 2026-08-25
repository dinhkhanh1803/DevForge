# M9 Production Blueprints Implementation Plan

**Goal:** Ship exactly three deterministic, checksummed, team-ready built-in blueprints and prove their generated projects with real certified toolchains.

**Design:** `docs/superpowers/specs/2026-08-25-m9-production-blueprints-design.md`

**Excluded:** additional catalog entries, remote package acquisition, arbitrary executables or commands, automatic deployment, GitHub network mutation, installer/updater work, and M10/M11 behavior.

Every task starts with focused failing tests, implements the smallest production slice, runs its focused and affected regression gates, records only observed results, and ends in a scoped local commit. Package versions and compatibility ranges are verified against official primary documentation before being pinned. No push is part of this plan.

## Task 1: Built-in distribution, catalog composition, and production contract harness

**Scope:** make immutable build-output packages discoverable as `BuiltIn`, preserve the distinct trusted-local source, and establish release contracts before adding package content.

**Expected files:** `src/DevForge.Blueprints.BuiltIn/DevForge.Blueprints.BuiltIn.csproj`, a minimal built-in package location contract, `src/DevForge.Desktop/{DevForge.Desktop.csproj,Bootstrap/DesktopBlueprintSourceRegistry.cs}`, architecture tests, `tests/DevForge.BlueprintTests/Production/*`, Desktop host/source tests, `blueprints/README.md`, and `docs/decisions/0015-versioned-static-built-in-blueprints.md`.

**RED tests:** require build-output assets under `blueprints\built-in`; require exactly one built-in and one local source with correct provenance; reject missing/reparse/ambiguous roots; require exact MVP IDs and versioned directory shape; require all files to be checksum-declared; prove local manifests cannot self-assign built-in trust; keep Desktop free of direct unguarded file enumeration.

**GREEN implementation:** expose only the fixed application-relative built-in location, include canonical package assets in consuming output, open both roots through `IFileSystem`, and add reusable production-package contract fixtures that load packages through `BlueprintPackageLoader` rather than parsing them independently.

**Focused commands:** Blueprint production contract filter, Desktop source/host filters, architecture tests, format verification, Release build.

**Exit gate:** source composition is deterministic and fail-closed; contract tests fail because the three packages are not yet delivered only while individual package tasks are RED, never through skipped tests or placeholder success.

## Task 2: Production WPF tool blueprint

**Scope:** deliver `desktop.csharp-wpf-tool/1.0.0` first, using the established .NET process boundary and adding only the closed publish-smoke operation required by the matrix.

**Expected files:** `blueprints/desktop.csharp-wpf-tool/1.0.0/**`, WPF expected-tree snapshot/contracts, narrow `ProcessExecutionHandlers` and action-policy changes, process security/regression tests, and generated-project E2E fixture.

**RED tests:** package structure/checksum; exact ID/version/tool range; typed inputs and Windows compatibility; deterministic plan/hash/tree; required handoff headings; native WPF plus MVVM/Clean Architecture project graph; nullable/analyzers/DI/Host/logging/config; central pinned package versions and NuGet lockfiles; publish profile; no web/embedded-browser dependency; no secret-shaped file/content; closed `dotnet publish` arguments and refusal of arbitrary targets/options.

**GREEN implementation:** author the checksummed skeleton and actions, pin supported .NET 10 packages through the generated `Directory.Packages.props`, render reviewed names only through restricted templates, add locked restore/build/test/format/publish validators, and keep engine evidence separate from blueprint-authored files.

**Focused commands:** WPF package contracts, process-policy tests, composed generation test, then real generated `dotnet restore --locked-mode`, format verify, Release build, test, and publish smoke.

**Exit gate:** a fresh generated WPF solution passes the real matrix on Windows 11 and its expected tree/digests are stable across two runs.

## Task 3: Closed pnpm vocabulary and production React blueprint

**Scope:** add the minimum safe pnpm validation operations and deliver `web.react-vite-ts/1.0.0` without invoking an online scaffolder.

**Expected files:** `src/DevForge.Infrastructure/Execution/ProcessExecutionHandlers.cs`, action/process policy tests, `blueprints/web.react-vite-ts/1.0.0/**`, React expected-tree/contracts, and generated-project E2E fixture.

**RED tests:** Node `>=22 <25`, pnpm `>=10 <11`; exact package and lockfile dependency resolution; strict TypeScript, alias, lint/format, environment validation, API boundary, Vitest, production build; frozen install with lifecycle scripts disabled; only fixed `lint`, `typecheck`, `test`, and `build` scripts accepted; reject `exec`, `dlx`, inline evaluation, registry/config/credential flags, script substitution, user-supplied commands, and lifecycle-script enablement; deterministic plan/tree and complete handoff docs.

**GREEN implementation:** author the Vite-compatible static skeleton, exact `package.json` and `pnpm-lock.yaml`, checksummed overlay/templates, and extend the existing runner handler with a handler-specific immutable pnpm command grammar. No shell or scaffolder command is added.

**Focused commands:** React package/process security tests, composed generation, real `pnpm install --frozen-lockfile --ignore-scripts`, lint, typecheck, test, and build with the certified Node/pnpm pair.

**Exit gate:** the generated React project passes the Windows 11 matrix, records Windows 10 as best-effort evidence only when actually run, and remains deterministic and Git-clean.

## Task 4: Python/uv trusted tool boundary and production Python CLI blueprint

**Scope:** add closed Python/uv identities, trusted resolution and doctor probes, then deliver `tool.python-cli/1.0.0`.

**Expected files:** `src/DevForge.Application/Contracts/ProcessContracts.cs`, `src/DevForge.Infrastructure/{Processes/TrustedExecutableResolver.cs,Environment/EnvironmentProbeCatalog.cs,Execution/ProcessExecutionHandlers.cs}`, related architecture/security/doctor/process tests, `blueprints/tool.python-cli/1.0.0/**`, Python expected-tree/contracts, and generated-project E2E fixture.

**RED tests:** canonical Python/uv identities and bounded version parsing; absolute trusted resolver behavior; doctor availability/version compatibility; exact certified Python range; frozen `uv.lock`; `pyproject.toml`, src layout, config/logging, Ruff, typecheck, pytest, build and console entrypoint; only fixed `uv sync --frozen` and checked validation entrypoints accepted; reject arbitrary modules, scripts, indexes, credentials, dependency changes, inline code, external working directories, and shell syntax; deterministic plan/tree and complete handoff docs.

**GREEN implementation:** extend the closed tool enum/map without string escape hatches, resolve Python/uv through trusted absolute candidates, add doctor probes, author the exact locked skeleton, and add handler-specific uv validation grammar over `IProcessRunner`.

**Focused commands:** process contract/security, resolver/doctor, Python package contracts and composed generation, followed by real frozen sync, Ruff, typecheck, pytest, package build, and CLI `--help` smoke.

**Exit gate:** the generated Python CLI passes the certified Windows 11 Python/uv matrix with no skipped release test and no ambient index/config dependency beyond the declared locked installation boundary.

## Task 5: Shared handoff and engine-owned run evidence

**Scope:** enforce the cross-blueprint team handoff standard and persist the named recipe/lock/policy/report evidence without allowing packages to forge it.

**Expected files:** shared BlueprintTests contracts, canonical run-evidence Application/Infrastructure writer or narrow extensions to the existing report boundary, checkpoint/recovery tests if required, all three package documentation assets, and privacy/tamper tests.

**RED tests:** all seven handoff documents exist with required truthful sections and blueprint-specific commands; `.env.example` has no value and `.env` is ignored; run artifacts contain canonical `.devforge/project.recipe.yaml`, `devforge.lock.json`, `generation-report.json`, and `policy.snapshot.json`; evidence binds blueprint version/checksum, plan hash, selected features, exact dependency/tool policy, validation results and generated artifacts; retries are byte-identical; tamper/non-canonical/secret-shaped/overwrite attempts fail closed.

**GREEN implementation:** reuse canonical serializers and guarded atomic file APIs, add only missing bounded evidence writers, preserve the existing final-tree/publication digest boundary, and complete package-specific handoff documents from the shared reviewed standard.

**Focused commands:** handoff contracts, run-evidence/recovery/privacy tests, all production blueprint contracts, format and Release build.

**Exit gate:** every successful generated run has a self-contained project handoff plus integrity-bound engine-owned evidence; no placeholder document or forged blueprint report exists.

## Task 6: Cross-blueprint integration and M9 closure

**Scope:** run the complete production matrix, close documentation, and stop before M10.

**Expected files:** `tests/DevForge.E2ETests/M9/*`, consolidated expected-tree snapshots, `docs/implementation-plan.md`, `docs/implementation-status.md`, `README.md`, `CHANGELOG.md`, and any final scoped regression fix with its test.

**RED tests:** Desktop discovers exactly the three production blueprints; each can be reviewed, planned, generated, validated and finalized through production composition; same inputs produce the same plan hash/tree; differing reviewed inputs change the hash/output; final targets are non-overwritten and recoverable; optional M8 local Git leaves the exact generated tree clean; no remote is contacted; static scans reject shell/admin/latest/wildcard/token/secret/forbidden platform surfaces.

**GREEN implementation:** add the composed fixtures and only defects they expose, aggregate the certified toolchain evidence, update milestone documentation with exact results, and record any consciously deferred environment matrix item without calling it green.

**Closure commands:** locked restore, format verification, Release build, focused M9 tests, all four test projects, real WPF/React/Python generated-project commands, EF pending-model check, architecture/privacy/security scans, `git diff --check`, and clean status after a scoped local commit.

**Exit gate:** exactly three production blueprints pass contracts and the required E2E matrix; all DevForge gates are green with zero failed/skipped tests; M9 is marked complete and M10 is recommended. No push.
