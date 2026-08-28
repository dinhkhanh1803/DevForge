# M11 Output Boundary Implementation Plan

> Execute inline with executing-plans, TDD and verification-before-completion.

**Goal:** Pass real WinForms generation/publication using production environment
composition without weakening complete-tree integrity.

**Architecture:** ADR-0025; optional canonical build-output membership written by
the existing evidence writer, consumed by the Git tree projection; .NET-only
environment declared by the trusted production process launcher.

**Tech Stack:** Existing C#/.NET 10 and pinned dependencies; no new packages.

## Task 1: Production environment

- [x] Add WindowsProcessRunnerTests for required runtime folders/SDK host, absent
  ambient injection and protected override refusal. Observe RED.
- [x] Add Processes/DotNetProcessEnvironment.cs; invoke after explicit environment
  setup in WindowsProcessRunner.CreateStartInfo only for ExecutableTool.DotNet.
  The method populates safe folders, trusted SDK PATH/host, and fixed opt-outs;
  throws scrubbed DF-PROC-001 on protected-key collision.
- [x] Remove environment reconstruction from M11 ReleaseDotnetRunner; forward
  `await _inner.RunAsync(command, progress, cancellationToken)` unchanged.
- [x] Run focused process tests and real M11 acceptance. Final acceptance after
  Task 2 passes 5/5 with no test-only environment injection.

## Task 2: Source versus output evidence

- [x] Add tree tests proving canonical output membership excludes only recorded
  paths from SourceFiles while the digest changes on output mutation. Add malformed,
  traversal, duplicate, unknown-property and missing-file rejection. Observe RED.
- [x] Add Execution/BuildOutputManifest.cs with bounded canonical codec, reviewed
  project/validator derivation, safe output-path predicate, all-source fallback,
  and no IO outside guarded workspace methods.
- [x] Reserve `.devforge/build-outputs.json` in ExecutionPortContracts policy.
- [x] Extend CanonicalProjectEvidenceWriter files/integrity inventory conditionally
  (keep its four-file receipt contract), using atomic no-overwrite/exact-byte retry.
- [x] Extend CanonicalProjectTreeSnapshot with AllFiles; hash AllFiles and project
  SourceFiles via the manifest. LocalGitService scans AllFiles, never just source.
- [x] Test source-only compatibility, ignored source rejection, output mutation
  rejection, interrupted/repeated publication, evidence exact-retry and tamper.
  Keep GitTreeVerifier and Git command vocabulary unchanged.

## Task 3: Acceptance and review

- [x] Run `dotnet test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release
  --no-restore --filter FullyQualifiedName~WinFormsCandidateE2ETests` with the local
  SDK and existing single-node/no-server build flags; require all five pass.
- [x] Run locked solution restore, scoped format/write and verify, Release build,
  and full solution test; record exact counts and OS in implementation-status.
- [x] Recheck security/recovery tests and request read-only review; correct findings
  with regressions. Preserve four unrelated Desktop dirty files, no push.
- [x] Update candidate README/checksum, ADR/status/plan/changelog only from observed
  evidence. Do not mark Windows 11/M9/M10 release gates complete.

Executable is `E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe`; worktree is
`E:\MyProjects\DevForge\.worktrees\m4-m11-completion` on codex/m4-m11-completion.
Flags: `--disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false`.

## Observed exit

2026-08-27, Windows 10 Pro 22H2 build 19045.6466; SDK 10.0.302/runtime 10.0.10.
Locked restore, final scoped format/write and verification, Release build all
exit 0. Build: 12 projects, 0 warnings/errors. Full test exits 0: Unit 651,
Integration 626, Blueprint 131, E2E 221; total 1,629 passed, none failed/skipped.
Focused tree/Git/evidence tests: 68/68; M11 E2E: 5/5. Candidate inventory: 39/39.
Review's second-read race and the large-manifest scanner-line failure both have
RED/GREEN regression evidence. Existing Desktop changes and worktree retained;
no commit, merge, push, remote operation, or additional blueprint.

Exact `--include` arguments used for both format commands:

```text
src/DevForge.Application/Contracts/ExecutionPortContracts.cs
src/DevForge.Infrastructure/Execution/BuildOutputManifest.cs
src/DevForge.Infrastructure/Execution/CanonicalProjectEvidenceWriter.cs
src/DevForge.Infrastructure/Git/CanonicalProjectTree.cs
src/DevForge.Infrastructure/Git/LocalGitService.cs
src/DevForge.Infrastructure/Processes/DotNetProcessEnvironment.cs
src/DevForge.Infrastructure/Processes/WindowsProcessRunner.cs
tests/DevForge.IntegrationTests/Infrastructure/Processes/WindowsProcessRunnerTests.cs
tests/DevForge.IntegrationTests/Infrastructure/Git/CanonicalProjectTreeTests.cs
tests/DevForge.IntegrationTests/Infrastructure/Git/LocalGitServiceTests.cs
tests/DevForge.IntegrationTests/Infrastructure/Execution/CanonicalGenerationReportWriterTests.cs
tests/DevForge.E2ETests/M11/WinFormsCandidateE2ETests.cs
tests/DevForge.E2ETests/M9/WpfBlueprintFixture.cs
tests/DevForge.BlueprintTests/Production/ProductionBlueprintCatalogFixture.cs
tests/DevForge.BlueprintTests/Production/WinFormsCandidateContractTests.cs
```
