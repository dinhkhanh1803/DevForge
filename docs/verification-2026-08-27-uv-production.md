# M11 uv production verification — 2026-08-27

Workspace: `E:\MyProjects\DevForge\.worktrees\m4-m11-completion`.
Branch: `codex/m4-m11-completion`, existing HEAD `cd44154`; no commit/push.
Host: Windows 10 Pro 22H2 build 19045.6466 (not Windows 11).
SDK: .NET 10.0.302, runtime 10.0.10. uv 0.12.1, Python 3.14.6.

## Exact final commands

`dotnet` below denotes `E:/MyProjects/DevForge/.tools/dotnet/dotnet.exe`.
The actual test-host PATH prepends these two existing tool directories only:

```powershell
$env:PATH = (Join-Path (Get-Location) '.tools/uv-0.12.1') + ';' + (Join-Path (Get-Location) '.tools/python/cpython-3.14.6-windows-x86_64-none') + ';' + $env:PATH
```

No wrapper injects environment into production CommandSpecs. The runner resolves
the installed tools and declares its own runtime environment.

| Command | Result |
| --- | --- |
| `dotnet restore DevForge.sln --locked-mode --disable-build-servers -m:1` | Exit 0, all projects up-to-date |
| `dotnet format DevForge.sln --no-restore --include <files below>` | Exit 0 |
| `dotnet format DevForge.sln --no-restore --verify-no-changes --include <files below>` | Exit 0 |
| `dotnet build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false` | Exit 0, 12 projects, 0 warnings, 0 errors |
| `dotnet test DevForge.sln -c Release --no-build --no-restore -m:1 --logger 'console;verbosity=minimal'` | Exit 0: Unit 651, Integration 653, Blueprint 141, E2E 228; total 1,673 passed, 0 failed/skipped |
| `git diff --check` | Exit 0; only Git LF/CRLF notices |

Formatting includes exactly these 15 files; unrelated pre-existing edits are not formatted:

```text
src/DevForge.Application/Contracts/ExecutionPortContracts.cs
src/DevForge.Infrastructure/Processes/WindowsProcessRunner.cs
src/DevForge.Infrastructure/Processes/UvProcessEnvironment.cs
src/DevForge.Infrastructure/Execution/UvExecutionEnvironment.cs
src/DevForge.Infrastructure/Execution/OwnedStagingWorkspaceManager.cs
src/DevForge.Infrastructure/Execution/ProcessExecutionHandlers.cs
src/DevForge.Infrastructure/Execution/BuildOutputManifest.cs
tests/DevForge.IntegrationTests/Infrastructure/Execution/CanonicalGenerationReportWriterTests.cs
tests/DevForge.IntegrationTests/Infrastructure/Execution/OwnedStagingWorkspaceManagerTests.cs
tests/DevForge.IntegrationTests/Infrastructure/Execution/ProcessExecutionHandlerTests.cs
tests/DevForge.IntegrationTests/Infrastructure/Processes/WindowsProcessRunnerTests.cs
tests/DevForge.IntegrationTests/Infrastructure/Git/CanonicalProjectTreeTests.cs
tests/DevForge.E2ETests/M11/PythonDesktopCandidateE2ETests.cs
tests/DevForge.E2ETests/M9/WpfBlueprintFixture.cs
tests/DevForge.E2ETests/M9/ProductionBlueprintReleaseMatrixE2ETests.cs
```

## Red/green and diagnostic record

- Baseline production acceptance: exit 1. Sandbox initially refused access to the
  shared uv cache; outside sandbox reproduced uv frozen-sync DNS 11003.
- Runtime regression: 9/9 failed with absent environment or accepted protected
  override, then 9/9 passed after the production policy.
- Frozen-sync handler regression: missing tooling variables failed, then the
  process-handler suite passed 45/45.
- Actual uv now downloaded locked dependencies, exposing a long temporary path
  during build isolation. OS temp replaced staging TEMP; frozen sync and all seven
  validators then passed. No Administrator/global Windows policy change.
- Review found wrong bounded-enumeration argument (exclusion instead of root),
  finalized cleanup rejecting tooling, and exact-bound overhead mismatch. Nested
  junction, 4097-file rejection and 4096-file cleanup were observed red then green.
- Python dist evidence regression failed before implementation, then passed with
  atomic retry, reviewed-source retention and full-tree inclusion. Additional
  corruption and unrecorded cache/dist membership cases pass.
- Focused integration set passed 157/157 before the final probe/bound additions.
- Python Desktop focused acceptance passed 6/6 before adding the shared CLI case:
  real frozen install, Ruff, mypy, 11 generated pytest tests, sdist/wheel, CLI/native
  smoke, local publication, repeat verification, wheel-tamper rejection and exact
  restoration. Final-target environment recreation uses production runner too.
- First full rerun: Unit 651, Integration 653, Blueprint 141 passed; E2E 227 passed,
  1 failed because the older matrix expected empty environment for uv. Replaced
  that assertion with the exact four safe tooling names; other tools still require
  an empty explicit environment. Real CLI and Desktop acceptance both passed.
- Intermediate compile failures were corrected (test access to intentionally
  internal values, analyzer array placement, and an optional-parameter method-group
  mismatch). A malformed test marker due to static field initialization order was
  fixed; canonical serialization remains strict. Format caught three LF-only lines
  after a patch; formatter corrected them and final verification passed.
- Sandbox Roslyn named-pipe denial required format outside sandbox. An initial
  multi-worker test build stalled; subsequent builds use disabled servers and one
  worker. These are tooling-environment failures, not reported as passing gates.

## Remaining boundaries

The local uv/publication hold is closed by the final full rerun. This adds 28
managed cases over the preceding baseline, in addition to strengthened existing
contracts. Both real Python CLI and Desktop workflows reached clean local
publication, repeated verification and wheel-tamper rejection/restoration.

No shared persistent uv cache means dependency acquisition requires network and
can be slower. Interpreter installation remains an explicit prerequisite. Deep
target paths may fail safely at third-party Windows path limits. The existing
text-candidate secret scanner does not unpack binary archives; every archive byte
is still included in the publication digest. Windows 11, remote CI and real GitHub
release evidence remain pending. BuiltIn stays exactly three MVP blueprints; no
new candidate, package upgrade, release promotion, commit or push in this repair.
