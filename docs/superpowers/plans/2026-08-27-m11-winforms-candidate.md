# M11 WinForms Candidate Implementation Plan

> **For agentic workers:** Use superpowers:executing-plans inline for this approved slice. No subagents are requested. Steps use checkbox syntax for tracking.

**Goal:** Deliver an independently testable native WinForms candidate without altering the shipped MVP catalog or claiming missing release evidence.

**Architecture:** A versioned checksummed static package reuses the existing production loader/planner/guarded execution pipeline. Test-only candidate content is isolated from production BuiltIn distribution. DevForge remains WPF; generated Desktop uses WinForms with a ViewModel, Host, and DI.

**Tech Stack:** .NET SDK 10.0.302, C# 14, WinForms on net10.0-windows, MVVM Toolkit 8.4.2, Hosting 10.0.10, xUnit 2.9.3; existing pinned dependency graph.

---

## Commands and scope

Worktree: `E:\MyProjects\DevForge\.worktrees\m4-m11-completion`, branch `codex/m4-m11-completion`. Use `E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe`. Build/test commands use `--disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false`; preserve unrelated Desktop dirty files. No push or remote writes.

### Task 1: Candidate contract and distribution isolation

**Files:** `tests/DevForge.BlueprintTests/Production/WinFormsCandidateContractTests.cs`, `ProductionBlueprintCatalogFixture.cs`, `tests/DevForge.BlueprintTests/DevForge.BlueprintTests.csproj`, `tests/DevForge.E2ETests/DevForge.E2ETests.csproj`.

- [x] Add a fixture factory for the explicit `blueprints/candidates` output directory; retain the default BuiltIn factory unchanged.
- [x] Replace the BlueprintTests catch-all content glob with only the candidate test link (existing BuiltIn project references already distribute the three MVP packages). E2E uses the same candidate link with `None`, never WPF `Content`.
- [x] Write and run RED identity/shape tests before package creation:

```csharp
using var fixture = await ProductionBlueprintCatalogFixture.CreateCandidatesAsync();
await fixture.Catalog.RefreshAsync(CancellationToken.None);
var package = Assert.Single(await fixture.Catalog.ListAsync(CancellationToken.None));
Assert.Equal("desktop.csharp-winforms-tool", package.Manifest.Id);
Assert.Equal(["format", "build", "test", "publish-smoke"],
    package.Manifest.Validators.Select(item => item.Id));
```

Run the BlueprintTests project with filter `FullyQualifiedName~WinFormsCandidateContractTests`; expected RED is missing candidate root/package, not a compilation error. Require an independent default-catalog assertion containing only the three MVP identities. Inspect project graph, five lock files, handoff sections, exact tools, commands, dependencies, checksum rejection, and stable plan hash across target paths.

### Task 2: Versioned WinForms skeleton and native UI

**Files:** `blueprints/v1-candidates/desktop.csharp-winforms-tool/**` (manifest/schema/rules/checksums, templates, base five-project source/tests, validators/migrations guides).

- [x] Retain the reviewed WPF package's non-UI build/package locks, layer boundaries and exact process targets; author WinForms-specific metadata, docs and Desktop source, with no XAML copied.
- [x] Define the generated Desktop switch as:

```xml
<TargetFramework>net10.0-windows</TargetFramework>
<UseWindowsForms>true</UseWindowsForms>
```

- [x] Use a STA entry point with ApplicationConfiguration initialization, a bounded Host lifetime and DI-resolved MainForm. MainForm binds a read-only status label to MainViewModel and invokes its RefreshCommand from an accessible button; use auto-sized TableLayoutPanel, DPI scaling and keyboard ordering. No direct filesystem/process access is added to form or ViewModel.
- [ ] Add generated service/ViewModel behavior tests before implementation and native UI smoke coverage in E2E. Retain exact package versions through Directory.Packages.props, not inline PackageReference versions.
- [x] Calculate canonical LF checksums for every package file except checksums.json. Run candidate contracts GREEN, never modify MVP blueprint bytes.

### Task 3: Production pipeline, real toolchain and closure evidence

**Files:** `tests/DevForge.E2ETests/M9/WpfBlueprintFixture.cs` (test composition reuse only), `tests/DevForge.E2ETests/M11/WinFormsCandidateE2ETests.cs`, milestone docs/ADR/changelog.

- [x] Extend the shared test fixture with an explicit WinForms candidate factory, candidate source directory and an optional real IProcessRunner. Keep all M9 defaults unchanged.
- [ ] Test review -> execute -> evidence -> local Git using the real production workflow; deterministic test-double command paths are labeled composition coverage, not real toolchain certification. Add validation failure/target preservation and candidate-only identity tests.
- [ ] Run the real generated project through WindowsProcessRunner and guarded CommandSpec. Assert every mandatory gate succeeds and the published native app opens responsively, exposes its named Refresh action and exits cleanly. The smoke must not mutate user data or require elevation.
- [ ] Run root locked restore, scoped format write/verify, Release build, Unit, Integration, Blueprint, E2E suites; record exact pass/fail/skip counts and OS. Run release package contracts to preserve the MVP three-root boundary.
- [ ] Update implementation plan/status, CHANGELOG and candidate README with actual results; keep Windows 11 and release promotion Pending. Stage only scope-owned files and commit locally.

## Exit

The following red checkpoint is historical. The subsequent ADR-0025 repair and
`2026-08-27-m11-output-boundary.md` supersede its source/output and environment
blockers: real local acceptance now passes 5/5 and the full solution 1,629/1,629.
Candidate-specific checksum mutation coverage and Windows 11 release evidence
remain open. No commit or push was performed in the repair task.

2026-08-27 checkpoint: candidate contracts and real .NET/native smoke pass locally,
but the unskipped real publication acceptance fails. Finalization includes ignored
bin/obj/artifacts outputs; exact Git tree verification intentionally rejects their
absence from the commit. This shared-pipeline design gap stops closure under
executing-plans. No security bypass, forced inclusion, output deletion, skip, or
commit is used to mark this slice complete. Source-only composition does not
substitute for the real-toolchain publication gate. Candidate-specific tamper
mutation coverage is also still open; existing loader checksum coverage is retained.

The candidate is implemented only when its real local toolchain/native smoke and root regressions pass. It is not shipped or certified until Windows 11 and M9/M10 release evidence is complete. Other M11 blueprints remain separate future slices, not placeholder packages.
