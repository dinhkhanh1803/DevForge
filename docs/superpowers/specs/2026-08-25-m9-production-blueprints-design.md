# M9 Production Blueprints Design

## Scope

M9 ships exactly the three MVP production blueprints required by the baseline:

- `desktop.csharp-wpf-tool`
- `web.react-vite-ts`
- `tool.python-cli`

Each blueprint is a versioned, checksummed, deterministic package that generates a team-ready project, validates it with the supported local toolchain, and includes the required handoff documentation. M9 also closes only the engine and Desktop composition gaps required to discover and execute these built-in packages.

M9 does not expand the catalog, add remote blueprint acquisition, add automatic deployment, build an updater or installer, contact GitHub during tests, introduce arbitrary process execution, or implement M10 hardening and packaging.

## Selected approach

DevForge ships versioned static framework skeletons rather than invoking online scaffolders during generation. Templates and overlays are reviewed source assets, dependency graphs are represented by exact lockfiles, and every package is protected by `checksums.json`. Generation therefore depends only on the selected package, reviewed inputs, runtime facts, and the closed execution vocabulary.

This approach is preferred over runtime scaffolder commands because it avoids `latest` resolution, changing upstream templates, and a broader network/process boundary. It is preferred over C#-authored project generation because the documented package structure remains directly reviewable, releasable, and independently contract-testable.

## Built-in package distribution and discovery

The canonical authoring tree is:

```text
blueprints/<blueprint-id>/<semantic-version>/
├── manifest.yaml
├── inputs.schema.json
├── rules.yaml
├── templates/
├── overlays/
├── validators/
├── migrations/
├── README.md
└── checksums.json
```

`DevForge.Blueprints.BuiltIn` owns the three package assets. Build output places those immutable assets under a fixed `blueprints\built-in` directory. Desktop composition opens that directory through `IFileSystem` as a read-only catalog source with `BlueprintSourceProvenance.BuiltIn`; the existing local-data directory remains the distinct `Local` source. Source provenance, not manifest text, assigns trust.

The registry validates the application-relative root, refuses missing or ambiguous built-in content, and does not copy packages into a user-writable trusted location. Catalog and execution reopen the same source and reverify the package checksum before planning or execution. M10 will later own installer asset packaging; M9 proves normal build-output composition.

## Package contract

Every package uses semantic version `1.0.0`, declares a bounded engine range, declares only supported tools, and has a checksum declaration covering every package file except `checksums.json` itself. Empty structural directories are represented by bounded explanatory files so the release artifact retains the documented shape.

Inputs use the existing typed schema and restricted template language. Inputs cannot name secrets, cannot contain commands, and cannot select package versions. Compatibility rules are deterministic expressions over the Environment Doctor and team profile snapshots. Actions use only the closed handlers and guarded package/payload-relative paths.

Each generated project contains the required handoff documents:

- `README.md`
- `ARCHITECTURE.md`
- `CONTRIBUTING.md`
- `DEVELOPMENT.md`
- `TESTING.md`
- `DEPLOYMENT.md`
- `TEAM_START_HERE.md`
The execution run also persists `.devforge/project.recipe.yaml`, `devforge.lock.json`, `generation-report.json`, and `policy.snapshot.json` as engine-owned run evidence. A blueprint must not forge or overwrite that evidence. `.env.example` is included only where a runtime configuration example is useful, and generated `.gitignore` excludes `.env` and other local secret files.

## Closed process vocabulary

All generation-time and validation commands continue through `IProcessRunner` with an `ExecutableIdentity`, immutable `ArgumentList`, guarded workspace, bounded timeout/output, and minimal environment. No shell, `cmd /c`, PowerShell command string, administrator execution, or user-supplied executable is introduced.

The production vocabulary is extended narrowly:

- .NET actions: locked restore; format verification; Release build; unit test; publish smoke.
- pnpm actions: frozen install with lifecycle scripts disabled at installation; fixed package scripts `lint`, `typecheck`, `test`, and `build` from the checksummed generated `package.json`.
- uv/Python actions: frozen synchronization from `uv.lock`; fixed `ruff`, typecheck, `pytest`, packaging, and CLI-smoke entrypoints from the checksummed `pyproject.toml`.

The policy validates executable and argument shape together. It rejects free-form scripts, inline evaluation, arbitrary module names, changing registries/indexes, unpinned add/install operations, lifecycle-script opt-in, credential/config arguments, output redirection, and paths outside the staging workspace. Trusted executable resolution and Environment Doctor gain Python/uv support with the same absolute-path and version parsing rules used for existing tools.

## Blueprint: desktop.csharp-wpf-tool

The WPF blueprint targets `net10.0-windows`, enables nullable and analyzers, and generates a Clean Architecture/MVVM solution with Application, Domain, Infrastructure, native WPF Desktop, and unit-test projects. It uses Generic Host dependency injection, typed configuration, structured logging, deterministic central package versions, NuGet lockfiles, and a checked-in Windows publish profile.

The generated solution has no web host or embedded browser. Validation runs locked restore, formatting verification, Release build, unit tests, and a non-publishing filesystem smoke of the Windows publish profile.

## Blueprint: web.react-vite-ts

The React blueprint generates a Vite-compatible React/TypeScript skeleton with strict TypeScript, path aliases, ESLint, formatter configuration, runtime environment validation, an API boundary, Vitest unit coverage, and a production build. Node is constrained to `>=22 <25` and pnpm to `>=10 <11` as specified. `package.json` and `pnpm-lock.yaml` carry exact reviewed dependency resolution; generation never invokes an unversioned create command.

Validation performs frozen installation, lint, typecheck, unit tests, and production build through the closed pnpm script vocabulary. The generated project contains no production secret or environment value.

## Blueprint: tool.python-cli

The Python blueprint uses a `pyproject.toml` project, `src` layout, typed configuration boundary, standard logging, packaged console entrypoint, Ruff, static type checking, pytest, and an exact `uv.lock`. The compatibility range is bounded to the Python versions explicitly certified by the M9 release matrix; it is not inferred from whichever interpreter is ambient.

Validation performs frozen uv synchronization, Ruff checks, type checking, pytest, package build, and a deterministic CLI `--help` smoke. The closed uv vocabulary prevents arbitrary dependency changes or index selection.

## Shared team handoff standard

The three packages share a reviewed documentation structure and policy vocabulary but do not depend on mutable external template files at runtime. Each package contains its own checksummed copy so a version is self-contained. Contract tests enforce required headings, truthful commands, repository layout descriptions, local setup, testing, contribution, deployment guidance, and a concise first-day checklist.

`DEPLOYMENT.md` documents safe release preparation only. M9 does not create credentials, cloud resources, or automatic deployment workflows.

## Determinism and release integrity

Contract tests load production packages through the real `BlueprintPackageLoader`, verify built-in provenance, checksum every byte, validate schemas/rules/actions, plan twice with canonical inputs, and require identical plan hashes and expected trees. A package mutation, unlisted file, stale checksum, undeclared artifact, unpinned dependency, forbidden path, secret-shaped content, or unsupported command fails the build.

Expected-tree snapshots cover generated paths and stable file digests. Generated engine evidence is validated separately from blueprint-authored files. Line endings and UTF-8 encoding are canonicalized in source assets so Windows checkout settings cannot change package checksums.

## Test and release matrix

The always-on suite contains package contracts, source composition tests, action-policy tests, deterministic plan/snapshot tests, and composed generation tests using the production file/process boundaries.

Release E2E runs the generated projects with real installed toolchains:

- WPF on Windows 11 with .NET 10: restore, build, test, publish smoke.
- React on Windows 11 and Windows 10 best effort with supported Node/pnpm: install, lint, typecheck, test, build.
- Python on Windows 11 with the certified Python/uv pair: sync/install, Ruff, typecheck, pytest, package and CLI smoke.

Tests do not silently pass when a required release tool is missing. The release gate reports the missing certified toolchain as a blocker. After optional M8 Git initialization, a composed fixture verifies the generated repository is clean without contacting a remote.

## Delivery sequence

1. Add built-in source distribution/composition and production package contract harness.
2. Deliver the WPF blueprint using the existing .NET boundary, extending it only for publish smoke.
3. Add the closed pnpm validation vocabulary and deliver the React blueprint.
4. Add Python/uv identities, resolver, doctor probes, and closed validation vocabulary; deliver the Python blueprint.
5. Add cross-blueprint expected-tree/E2E release matrix, shared handoff contract, documentation, ADR, and M9 closure gates.

Each slice begins with failing tests, has a scoped exit gate, and is committed independently. A later blueprint does not weaken the execution policy established by an earlier slice.

## Failure behavior

A missing built-in package, checksum failure, incompatible tool, invalid input, failed install/restore, failed validator, timeout, or cancellation retains the existing recoverable execution semantics. The final target is not published until every required validator and finalization check succeeds. Diagnostics contain stable redacted codes and remediation; package manager, compiler, and test output remain bounded and redacted.

## Exit gate

M9 exits only when exactly three built-in production blueprints are discoverable in Desktop; all packages satisfy structure/checksum/schema/rule/action/handoff contracts; plans and expected trees are deterministic; each generated project passes its real certified toolchain matrix; Git cleanliness is proven; locked DevForge restore, format, Release build, all tests, architecture/privacy/security scans, and EF migration consistency pass; and implementation status records exact observed results. No additional blueprint or M10 packaging behavior is included.
