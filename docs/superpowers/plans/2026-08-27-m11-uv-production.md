# M11 uv production repair implementation plan

> For agentic workers: execute inline with executing-plans, TDD and verification-before-completion. Preserve the existing worktree and unrelated edits.

**Goal:** Prove Python production generation and local publication without test-only environment injection.

**Architecture:** uv gets a declared Windows environment; owned staging contains a sibling tooling workspace. Only payload dist output membership enters full-tree evidence. WPF/Clean Architecture and all security bounds remain unchanged.

**Tech stack:** C#/.NET 10, guarded filesystem ports, uv 0.12.1/Python 3.14.6 acceptance tools.

## Tasks and files

- [x] Read specification, inspect worktree and reproduce production failure outside sandbox (uv DNS 11003).
- [x] Add runner regressions in `tests/DevForge.IntegrationTests/Infrastructure/Processes/WindowsProcessRunnerTests.cs`: empty environment receives SystemRoot and trusted Python; protected overrides fail; ambient values are absent. Run the filtered test before implementation.
- [x] Add `src/DevForge.Infrastructure/Processes/UvProcessEnvironment.cs` and wire `WindowsProcessRunner.cs`. Apply only uv; preserve Git/.NET semantics. Version probes need no interpreter.
- [x] Extend `StagingWorkspace` in `ExecutionPortContracts.cs` with a validated optional container port and populate it in all three lease paths of `OwnedStagingWorkspaceManager.cs`. Add `UvExecutionEnvironment.cs`, wire preparation in `ProcessExecutionHandlers.cs`. Validate sibling relationship and guarded paths before running; never choose paths from manifest strings.
- [x] Add tests in `ProcessExecutionHandlerTests.cs` for tooling environment and containment. Confirm red then green; maintain existing negative command tests.
- [x] Extend `BuildOutputManifest.cs` for canonical Python dist membership, root pyproject/lock and exact mandatory build validator. Add contract/integration regressions for source retention, corruption and tamper.
- [x] Strengthen `PythonDesktopCandidateE2ETests.cs`: real production runner, no environment injection, no .venv/cache in finalized target, dist present, repeated local publication, mutation rejection, final-path frozen sync and smoke. Retain negative execution/cleanup tests.
- [x] Update implementation plan/status/changelog with exact results and remaining Windows 11 debt.

## Commands and exit gate

Use `E:/MyProjects/DevForge/.tools/dotnet/dotnet.exe`. Run filtered tests after each red/green step, then `restore DevForge.sln --locked-mode`, scoped `format DevForge.sln --no-restore` and `--verify-no-changes`, `build DevForge.sln -c Release --no-restore`, `test DevForge.sln -c Release --no-build --no-restore`. Acceptance host PATH includes the pinned uv/Python directories; no test wrapper injects process environment. Require all test suites green and actual local publication/recovery evidence before removing the catalog hold. Windows 11/remote release evidence remains separate. No commit or push requested.
