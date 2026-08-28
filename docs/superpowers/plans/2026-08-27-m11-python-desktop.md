# M11 Python Desktop Implementation Plan

> Execute inline with executing-plans, test-driven-development and verification-before-completion.

**Goal:** Close WinForms checksum mutation coverage and implement one independently
verifiable native Python Desktop candidate without expanding the shipped catalog.

**Architecture:** ADR-0026. Guarded private package copies; existing Python
pipeline and pinned dependencies; pure state model + Tk view + CLI composition.

**Tech Stack:** C#/.NET 10 tests and engine; Python 3.14, Tk/ttk, uv 0.12,
Ruff 0.16.3, mypy 2.3.1, pytest 9.1.1, build 1.5.0, hatchling 1.32.0.

## Task 1: WinForms checksum contract

Files: `tests/DevForge.BlueprintTests/Production/CandidateTamperTests.cs` and
`ProductionBlueprintCatalogFixture.cs` only.

- [x] Create a private test-owned package copy through guarded workspace ports.
  Copy original inventory unchanged; load and resolve pristine candidate first.
- [x] Mutate manifest, `MainForm.cs`, `templates/Directory.Packages.props`, and
  `checksums.json` separately. Assert catalog has no executable matching ID,
  inspect result is Quarantined and resolve is invalid. Assert sibling candidates
  and shared original remain unchanged. Mutation assertion:
  `Assert.DoesNotContain(snapshot.ExecutableBlueprints, item => item.Manifest.Id == id);`
- [x] Run focused WinForms contracts and tamper tests before adding Python payload.

## Task 2: Python candidate contract and tests first

Files: `tests/DevForge.BlueprintTests/Production/PythonDesktopCandidateContractTests.cs`,
both Blueprint/E2E csproj content lists, `tests/DevForge.E2ETests/M9/WpfBlueprintFixture.cs`,
`tests/DevForge.E2ETests/M11/PythonDesktopCandidateE2ETests.cs`.

- [x] RED: select `tool.python-desktop` from candidate catalog (not Single catalog),
  assert seven validators including `desktop-smoke`, pure model/view and pinned
  locks; expect missing package before authoring. Keep BuiltIn exactly three.
- [x] RED generated behavior: `model = StatusModel(); model.refresh();
  assert model.text == "Ready - refresh 1"`; test repeat and independent instances.
  Native smoke must invoke a real ttk button, observe updated StringVar, focus and
  orderly close; tests exercise failures without suppressing unexpected exceptions.

## Task 3: Candidate payload and closed command

Files: `blueprints/v1-candidates/tool.python-desktop/**`,
`src/DevForge.Infrastructure/Execution/ProcessExecutionHandlers.cs`,
`tests/DevForge.IntegrationTests/Infrastructure/Execution/ProcessExecutionHandlerTests.cs`.

- [x] Reuse reviewed base Python configuration/lock as input without modifying MVP.
  Add `model.py`, `desktop.py` and `desktop_cli.py`; retain CLI help smoke.
  `team-desktop = "team_tool.desktop_cli:main"` is a console entrypoint so bounded
  smoke retains a process handle and meaningful exit status on Windows.
- [x] Add exact accepted vector only:
  `["run", "--frozen", "--no-sync", "--no-config", "team-desktop", "--smoke-test"]`.
  Test accepted vector RED first; extra args, normal GUI launch, arbitrary module,
  unfrozen and --with remain rejected before runner calls.
- [x] Update manifest, explicit artifacts, seven handoff guides and canonical LF
  checksums for every payload. Do not change package versions or default shipping.

## Task 4: Verification and truthful closure

- [x] Run deterministic workflow, mandatory validator failure, target preservation,
  tamper and source-only local Git tests using the existing guarded fixture.
- [x] Run real uv frozen sync, Ruff format/check, mypy, pytest, package build and
  native smoke. Probe production runner unchanged; record any uv environment or
  output-boundary failure instead of representing simulated composition as E2E.
- [x] Review new code/security boundaries and correct findings with regressions.
- [x] Run locked restore, scoped format/write+verify, Release build and full tests.
  Update implementation-status/plan/ADR/changelog and retain unrelated dirty files.

All dotnet commands use `E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe` from
`E:\MyProjects\DevForge\.worktrees\m4-m11-completion`, flags
`-c Release --no-restore --disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false`.
Test filters: `FullyQualifiedName~Candidate`; final command: `dotnet test DevForge.sln`
with those flags and `--logger "console;verbosity=minimal"`.
Keep branch/worktree; no commit or push in this task.

## Observed exit and remaining gate

Local restore and scoped format/write+verify exit 0. Release build: 12 projects,
0 warnings/errors. Full test exit 1: Unit 651, Integration 626, Blueprint 141,
E2E 226 passed / 1 failed; total 1,644 passed, 1 failed, 0 skipped. The failed
production uv workflow is retained and unskipped. It reports DNS os error 11003
fetching a locked dependency; standalone declared-host toolchain passes all eight
commands and 11 generated tests. Host DNS lookup resolves the same endpoint.

- [ ] Combined production uv generation -> finalize -> clean Git -> durable retry
  must pass without test-only environment injection. Candidate is not closed.
- [ ] Windows 11/release-host certification and M9/M10 external release evidence.

Review corrections: callback failure latch with observed behavioral RED/GREEN;
four Python checksum mutations; deterministic comparison retains uv.lock.
Generated tests initially had missing-module RED before model/view/entrypoint
implementation. No prior MVP blueprint file, unrelated Desktop change or remote
was modified.

Exact format include paths:

```text
src/DevForge.Infrastructure/Execution/ProcessExecutionHandlers.cs
tests/DevForge.IntegrationTests/Infrastructure/Execution/ProcessExecutionHandlerTests.cs
tests/DevForge.BlueprintTests/Production/ProductionBlueprintCatalogFixture.cs
tests/DevForge.BlueprintTests/Production/WinFormsCandidateContractTests.cs
tests/DevForge.BlueprintTests/Production/CandidateTamperTests.cs
tests/DevForge.BlueprintTests/Production/PythonDesktopCandidateContractTests.cs
tests/DevForge.E2ETests/M9/WpfBlueprintFixture.cs
tests/DevForge.E2ETests/M11/PythonDesktopCandidateE2ETests.cs
```
