# Node production verification — 2026-08-28

Worktree: `codex/m4-m11-completion`, base `cd44154`. Preserve pre-existing M11
and unrelated Desktop edits. No commit, push, remote creation or release promotion.
Host: Windows 10 Pro 22H2, build 19045.6466, x64. This is not Windows 11 evidence.
SDK: `.tools/dotnet/dotnet.exe` 10.0.302 under the parent checkout.

## Scope and boundary

ADR-0028: protected Node/pnpm runtime, source-verified sibling tooling snapshot,
bounded marker-owned cleanup, immutable React static-dist compatibility, and
raw-byte-hash-bound public-bundle false-positive review. After the successful
foundation gate, one test-only Next candidate was added; final local gates pass.

Portable Node 22.23.2 ZIP was fetched from nodejs.org and matches the official
SHA-256 `1177b4137ba5adaa56354ae40f1080c7450e8ae09cecb47da459d1c52ac99f97`.
`node.exe --version` returned `v22.23.2`; installed pnpm entry point returned
`10.24.0`. No machine installation or persistent environment change.

## Historical foundation checkpoint

Commands below use `E:/MyProjects/DevForge/.tools/dotnet/dotnet.exe` as `dotnet`
and run in `E:/MyProjects/DevForge/.worktrees/m4-m11-completion`.

| Command | Actual result |
| --- | --- |
| `dotnet restore DevForge.sln --locked-mode --disable-build-servers -m:1` | Exit 0, all projects up-to-date. |
| `dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj -c Release --no-build --no-restore` | Exit 0, 651 passed, 0 failed/skipped. |
| `dotnet test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-build --no-restore` | Exit 0, 722 passed, 0 failed/skipped. |
| `dotnet test tests/DevForge.BlueprintTests/DevForge.BlueprintTests.csproj -c Release --no-build --no-restore` | Exit 0, 141 passed, 0 failed/skipped. |
| `git diff --check` | Exit 0; checkout line-ending advisories only. |

Scoped format succeeded at this checkpoint; final gates are recorded below.
One full build overlapped the running E2E and failed with
MSB3026/MSB3021/MSB3027 (20 warnings, 4 errors): loaded E2E DLLs could not be
replaced. Serialized retry passed with 0 warnings/errors, exit 0 (1.42 seconds).
This was not a production-code or security waiver.

## Red/green evidence

- Runtime isolation/protected override, installed pnpm resolution, ancestor
  workspace isolation and allowed-nonzero source verification were reproduced
  before their fixes. Pre-existing Integration baseline was 653 passing.
- Source reserved names, case-insensitive config refusal, partial-copy mutation,
  payload/tooling double tamper, output evidence, partial static-dist recovery,
  junction refusal and large Node cleanup are covered by focused regressions.
- Real frozen React install/lint/typecheck/tests/build passed, then exposed
  omitted required dist and the scanner's long-line refusal. Retain the immutable
  dist contract; scan complete lines under the original 1 MiB/regex-timeout bounds.
- Delimiter-only boolean suppression was rejected after 7/12 new conservative
  marker cases failed. A mutated public fixture also reproduced 5/8 failures.
- `.mjs`/`.cjs` and binary-prefix JavaScript cases reproduced 5/15 failures.
- Raw-byte hash + exact occurrence suppression replaced that unsafe approach.
  Scanner suite passed 46/46; additional appended-category assertions are included
  in the later 722/722 Integration run. No matched secret values are logged.
- Independent review verified fixture length/hash/occurrence and approved the
  replacement on code inspection. This is not execution or release acceptance.

The first resumed real React test passed 1/1 in 3.9776 minutes but still used the
rejected delimiter implementation. It is diagnostic evidence only, not the final
foundation gate. The replacement run with fixed-hash scanning and direct JavaScript
space/NUL publication tampering passed 1/1, exit 0, in 4.4487 minutes. Exact command:

```powershell
$env:PATH = 'E:/MyProjects/DevForge/.worktrees/m4-m11-completion/.tools/node-v22.23.2-win-x64;' + $env:PATH
& E:/MyProjects/DevForge/.tools/dotnet/dotnet.exe test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~RealReactProductionRunner' --logger 'console;verbosity=detailed'
```

All nine production commands exited 0 (install once; lint/typecheck/test/build
twice for postcondition revalidation). Generated tests passed 2/2 both times.
Both builds produced the documented exact public bundle hash. Full static tree,
no dependency/tooling roots, owned staging cleanup, local publication, repeated
verification, source/HTML/JS tamper refusal and exact restored-byte retry passed.
Network slow-download warnings did not fail frozen installation; no command or
scanner gate was skipped. Foundation acceptance is locally satisfied; full final
solution verification will also cover the subsequent candidate.

## Next candidate diagnostic and regression evidence

- Missing-candidate contracts were observed RED (2/2) before package creation.
  Closed `format:check`/`smoke` handler cases were also observed RED (2/2).
- Candidate contract/checksum-tamper filter: exit 0, 14/14; simulated Next workflow
  filter excluding RealNext: exit 0, 4/4. Subsequent runtime matrix is additional
  coverage pending the final managed run.
- First native Next run: frozen install passed (174 packages); format failed on
  seven copied overlays. Repository CRLF formatting had been used instead of the
  candidate LF config. Explicit candidate-config Prettier reproduced then fixed
  those files. No runtime source mutation or skipped formatter is permitted.
- Direct pinned ESLint reproduced `no-control-regex` in environment validation.
  Replace the regex with a character-code predicate, not a disabled lint rule.
  The seven real Node environment tests passed after the change.
- Negative smoke lifecycle uses the unchanged script in subprocesses with only
  the `next` module replaced by a test fixture. Assertion failure, hung close
  reaching the real 30-second deadline, and cancellation all exit unsuccessfully;
  every previously observed loopback port refuses connections afterward.
  Two runs passed 3/3; the latest takes 30.465 seconds and awaits process `close`
  (including output drain), following independent review feedback.
- Reviewer found no further blocking candidate issues after these tests; static
  review and mocked Next lifecycle do not substitute for real Next HTTP smoke.

Commands for the standalone regressions (portable Node as `node`):

```text
node --experimental-strip-types --test blueprints/v1-candidates/web.next-ts/overlays/base/tests/environment.test.mjs
node --test blueprints/v1-candidates/web.next-ts/overlays/base/tests/smoke-lifecycle.test.mjs
node ./.tools/next-lock/node_modules/prettier/bin/prettier.cjs --check --config blueprints/v1-candidates/web.next-ts/templates/.prettierrc.json "blueprints/v1-candidates/web.next-ts/overlays/base/**/*" blueprints/v1-candidates/web.next-ts/templates/package.json blueprints/v1-candidates/web.next-ts/templates/TESTING.md
```

All three commands exited 0 in their latest invocation.

Real Next acceptance subsequently passed 1/1, exit 0, 8.2876 minutes. It observed
Node 22.23.2/pnpm 10.24.0 through the production runner, installed 174 packages,
and executed all 13 commands successfully (frozen install once, six required
validators twice including postcondition revalidation). Both generated test runs
passed 10/10; both optimized Next builds and real HTTP smoke passed. Finalized
source contains no node_modules/.next/.devforge-node/dist/tooling; owned staging
was removed. Real local Git publication, repeated verification, four source
tamper cases and restored-byte retry passed, with zero remote calls.

```powershell
$env:PATH = 'E:/MyProjects/DevForge/.worktrees/m4-m11-completion/.tools/node-v22.23.2-win-x64;' + $env:PATH
& E:/MyProjects/DevForge/.tools/dotnet/dotnet.exe test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false --filter 'FullyQualifiedName~RealNextProduction' --logger 'console;verbosity=detailed'
```

The final full-suite rerun also includes the later output-drain reliability
adjustment and five reviewed-runtime planning cases.

## Final managed gate command scope

Locked restore was rerun after candidate changes: exit 0, all projects up-to-date.
Both commands below exited 0; `<files>` is the exact 19-file scope that follows.
Unrelated Desktop converter/resource files were not formatted.

```text
dotnet format DevForge.sln --no-restore --include <files>
dotnet format DevForge.sln --no-restore --verify-no-changes --include <files>
```

- `src/DevForge.Infrastructure/Execution/NodeExecutionWorkspace.cs`
- `src/DevForge.Infrastructure/Execution/OwnedStagingWorkspaceManager.cs`
- `src/DevForge.Infrastructure/Execution/ProcessExecutionHandlers.cs`
- `src/DevForge.Infrastructure/Processes/NodeProcessEnvironment.cs`
- `src/DevForge.Infrastructure/Processes/TrustedExecutableResolver.cs`
- `src/DevForge.Infrastructure/Processes/WindowsProcessRunner.cs`
- `src/DevForge.Infrastructure/Security/SecretPatternCatalog.cs`
- `src/DevForge.Infrastructure/Security/WorkspaceSecretScanner.cs`
- `tests/DevForge.IntegrationTests/Infrastructure/Execution/NodeExecutionWorkspaceTests.cs`
- `tests/DevForge.IntegrationTests/Infrastructure/Execution/OwnedStagingWorkspaceManagerTests.cs`
- `tests/DevForge.IntegrationTests/Infrastructure/Execution/ProcessExecutionHandlerTests.cs`
- `tests/DevForge.IntegrationTests/Infrastructure/Processes/WindowsProcessRunnerTests.cs`
- `tests/DevForge.IntegrationTests/Infrastructure/Security/WorkspaceSecretScannerTests.cs`
- `tests/DevForge.ProcessTestHelper/Program.cs`
- `tests/DevForge.E2ETests/M9/WpfBlueprintFixture.cs`
- `tests/DevForge.E2ETests/M11/NodeProductionBoundaryE2ETests.cs`
- `tests/DevForge.E2ETests/M11/NextCandidateE2ETests.cs`
- `tests/DevForge.BlueprintTests/Production/CandidateTamperTests.cs`
- `tests/DevForge.BlueprintTests/Production/NextCandidateContractTests.cs`

Final serialized Release build exited 0, 0 warnings/errors, 14.33 seconds:

```text
dotnet build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 -p:UseSharedCompilation=false
```

Full-solution test command exited 0: **1,762/1,762 passed**, zero failed/skipped.
Unit 651 (593 ms), Integration 724 (31 s), Blueprint 152 (2 s), E2E 235
(14 min 37 s). This includes the final output-drain fix and runtime matrix,
and reruns real Node/Next/React and existing .NET/Python acceptance:

```powershell
$env:PATH = 'E:/MyProjects/DevForge/.worktrees/m4-m11-completion/.tools/node-v22.23.2-win-x64;E:/MyProjects/DevForge/.worktrees/m4-m11-completion/.tools/uv-0.12.1;E:/MyProjects/DevForge/.worktrees/m4-m11-completion/.tools/python/cpython-3.14.6-windows-x86_64-none;' + $env:PATH
& E:/MyProjects/DevForge/.tools/dotnet/dotnet.exe test DevForge.sln -c Release --no-build --no-restore -m:1
```

Final candidate inventory check passed: 35 raw-SHA256 entries, 35 payload files,
zero mismatches. `git diff --check` exited 0 after documentation closure, with
checkout line-ending advisories only. Existing Desktop edits are preserved.

## Release holds

Windows 11, native UX/DPI, packaging and remote CI evidence remain open exactly as
recorded in the release checklist. No candidate is promoted or remotely published.
