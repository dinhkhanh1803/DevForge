# M3 Core Infrastructure Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver production Windows implementations for guarded workspace I/O, safe process execution, secret scanning, environment inspection, and trusted IDE launch.

**Architecture:** Application retains the immutable security contracts created in M1. Infrastructure implements those contracts with Windows/BCL APIs, fails closed when executable trust or path containment cannot be proven, and returns only bounded redacted results. Integration tests exercise real processes and test-owned filesystem roots.

**Tech Stack:** .NET SDK 10.0.302, C# 14, WPF-compatible Windows APIs, `System.Diagnostics.Process`, `System.IO`, xUnit 2.9.3; no new runtime package unless a RED test proves the BCL insufficient.

---

## File map

- `src/DevForge.Infrastructure/FileSystem/WindowsFileSystem.cs`: opens a canonical non-reparse workspace root.
- `src/DevForge.Infrastructure/FileSystem/WindowsWorkspaceFileSystem.cs`: guarded workspace operations.
- `src/DevForge.Infrastructure/FileSystem/WorkspacePathGuard.cs`: containment and reparse-component checks.
- `src/DevForge.Infrastructure/InfrastructureOperationException.cs`: stable scrubbed OS-boundary failure with no raw inner exception.
- `src/DevForge.Infrastructure/Processes/TrustedExecutableResolver.cs`: typed executable discovery and validation.
- `src/DevForge.Infrastructure/Processes/ProcessOutputRedactor.cs`: streaming bounded line redaction.
- `src/DevForge.Infrastructure/Processes/WindowsProcessRunner.cs`: process start, pumps, timeout/cancellation, and tree termination.
- `src/DevForge.Infrastructure/Security/WorkspaceSecretScanner.cs`: bounded text scanning with redacted findings.
- `src/DevForge.Infrastructure/Environment/WindowsEnvironmentDoctor.cs`: typed fixed version probes.
- `src/DevForge.Infrastructure/Ide/WindowsIdeLauncher.cs`: trusted non-elevated IDE handoff.
- `tests/DevForge.UnitTests/Architecture/InfrastructureBoundaryTests.cs`: dependency and forbidden-API enforcement.
- `tests/DevForge.IntegrationTests/Infrastructure/**/*.cs`: focused policy tests and real Windows process/filesystem/scanner tests; Infrastructure implementation tests stay out of UnitTests to preserve its approved project-reference boundary.
- `tests/DevForge.ProcessTestHelper/Program.cs`: deterministic child process used only by integration tests.

## Task 1: Protect the Infrastructure boundary

**Files:**
- Create: `tests/DevForge.UnitTests/Architecture/InfrastructureBoundaryTests.cs`
- Create: `docs/decisions/0005-guarded-windows-infrastructure-boundaries.md`
- Modify: `DevForge.sln` only if the process helper is added in Task 4

- [x] **Step 1: Write the failing architecture test**

Add repository-source checks that reject `Process.Start`, `ProcessStartInfo`, direct workspace-mutating `File`/`Directory` calls, `cmd /c`, PowerShell execution, and `UseShellExecute=true` outside Infrastructure and test fixtures. Drive the analyzer with a synthetic Desktop source containing `Process.Start` so the analyzer itself has a deterministic RED without requiring placeholder production owners.

```csharp
[Fact]
public void AnalyzerReportsForbiddenProcessStartOutsideInfrastructure()
{
    var sources = new Dictionary<string, string>
    {
        ["src/DevForge.Desktop/UnsafeLauncher.cs"] =
            "using System.Diagnostics; class UnsafeLauncher { void Run() => Process.Start(\"git\"); }",
    };
    var violations = InfrastructureBoundary.FindViolationsFromSources(sources);

    Assert.Single(violations);
}
```

The helper must enumerate production `.cs` files deterministically, allow OS effects only inside Infrastructure, and report `relative/path.cs: forbidden API` for each violation.

- [x] **Step 2: Run RED**

Run:

```powershell
.\.tools\dotnet\dotnet.exe test tests\DevForge.UnitTests\DevForge.UnitTests.csproj --configuration Release --filter FullyQualifiedName~InfrastructureBoundaryTests
```

Expected and observed: compile FAIL `CS0103` because `InfrastructureBoundary` does not exist.

- [x] **Step 3: Record ADR-0005 and the exact allowed boundary**

Record these accepted decisions: typed executable identities, `ArgumentList`, no shell/elevation, opaque workspace roots, component-by-component reparse rejection, no-overwrite finalization, redaction before observation, and BCL-first implementation. Reject raw command strings, path-prefix-only checks, link following, and Desktop-owned OS calls.

- [x] **Step 4: Run focused GREEN**

Run the focused architecture test after adding its explicit Infrastructure allowlist. Observed: 3/3 PASS with no warnings while all existing production files remain compliant.

- [ ] **Step 5: Commit**

```powershell
git add tests/DevForge.UnitTests/Architecture/InfrastructureBoundaryTests.cs docs/decisions/0005-guarded-windows-infrastructure-boundaries.md
git commit -m "test(architecture): protect M3 infrastructure boundaries"
```

## Task 2: Implement the guarded Windows workspace

**Files:**
- Create: `src/DevForge.Infrastructure/InfrastructureOperationException.cs`
- Create: `src/DevForge.Infrastructure/FileSystem/WorkspacePathGuard.cs`
- Create: `src/DevForge.Infrastructure/FileSystem/WindowsFileSystem.cs`
- Create: `src/DevForge.Infrastructure/FileSystem/WindowsWorkspaceFileSystem.cs`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/FileSystem/WindowsWorkspaceFileSystemTests.cs`

- [x] **Step 1: Write path-guard and real I/O tests**

Cover opening a local directory, non-reparse root enforcement, canonical containment, case-insensitive comparison, create/read/write/enumerate, no-overwrite writes, safe overwrite of regular files, cancellation, root deletion rejection, recursive run-owned deletion, and atomic no-overwrite directory finalization.

```csharp
[Fact]
public async Task MoveDirectoryAsync_DoesNotOverwriteExistingDestination()
{
    await using var fixture = await WorkspaceFixture.CreateAsync();
    await fixture.CreateDirectoryAsync("staging");
    await fixture.CreateDirectoryAsync("target");

    await Assert.ThrowsAsync<IOException>(() => fixture.Workspace.MoveDirectoryAsync(
        PathOf("staging"),
        PathOf("target"),
        WorkspaceMoveIntent.AtomicNoOverwriteFinalize,
        CancellationToken.None));
}
```

- [x] **Step 2: Run RED**

Observed: compile failure `CS0234` because `DevForge.Infrastructure.FileSystem` did not exist.

- [x] **Step 3: Implement canonical containment and component checks**

`WorkspacePathGuard.ResolveContainedPath` must combine the private root and guarded relative path, normalize with `Path.GetFullPath`, require either exact root or `root + separator` prefix using `OrdinalIgnoreCase`, walk each existing component, and reject `FileAttributes.ReparsePoint`.

```csharp
internal string ResolveContainedPath(WorkspaceRelativePath relativePath)
{
    var candidate = Path.GetFullPath(Path.Combine(_root, relativePath.RevealForFileSystem()));
    var prefix = _root + Path.DirectorySeparatorChar;
    if (!candidate.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
    {
        throw new IOException("The workspace path is outside the allowed root.");
    }

    RejectExistingReparseComponents(candidate);
    return candidate;
}
```

Add `InfrastructureOperationException` with a stable code and fixed safe message, no public raw path/command/output property, and no retained inner exception. Map expected `IOException`, `UnauthorizedAccessException`, `SecurityException`, and supported Win32 failures only at the outer Infrastructure operation boundary.

- [x] **Step 4: Implement each workspace operation minimally**

Use `FileStreamOptions` with explicit `FileMode`/`FileAccess`/`FileShare`, async mode, and `FileOptions.Asynchronous`. Enumeration converts every result back to `WorkspaceRelativePath`, skips no error silently, and never follows reparse directories. Move uses `Directory.Move` only after source/destination validation and explicit missing-destination proof. Recursive delete rejects the root and validates every discovered entry before deletion.

- [x] **Step 5: Add link/junction escape tests**

Create a test-owned outside directory and a link/junction inside the workspace. Assert open-root, read, enumerate, and recursive delete all reject it and the outside sentinel remains unchanged. The host could not create symbolic links without privilege, so the test fixture creates a mount-point junction directly with `FSCTL_SET_REPARSE_POINT`; no shell, elevation, or skipped test is used.

- [x] **Step 6: Run GREEN and commit**

Run focused architecture UnitTests and filesystem IntegrationTests twice. Observed before final coverage additions: architecture 3/3 and filesystem 7/7 PASS repeatedly with zero skipped tests on the supported Windows development host.

```powershell
git add src/DevForge.Infrastructure/InfrastructureOperationException.cs src/DevForge.Infrastructure/FileSystem tests/DevForge.UnitTests/Infrastructure/FileSystem tests/DevForge.IntegrationTests/Infrastructure/FileSystem
git commit -m "feat(infrastructure): add guarded workspace filesystem"
```

## Task 3: Redact and bound process output

**Files:**
- Create: `src/DevForge.Infrastructure/Processes/ProcessOutputRedactor.cs`
- Create: `src/DevForge.Infrastructure/Processes/BoundedProcessOutput.cs`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/Processes/ProcessOutputRedactorTests.cs`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/Processes/BoundedProcessOutputTests.cs`

- [x] **Step 1: Write failing redaction/limit tests**

Cover explicit sensitive needles, overlapping needles, private keys, bearer/JWT, GitHub/OpenAI/AWS shapes, connection-string assignments, `.env` content, harmless `Foreign key`/`.env file` text, 4,096-character lines, 200 retained lines, 65,536 retained characters, and immutable output.

```csharp
[Fact]
public void Observe_RedactsBeforeProgressAndRetention()
{
    var sensitive = SensitiveProcessValue.Create("ghp_012345678901234567890123456789012345").Value;
    var output = new BoundedProcessOutput([sensitive], progress: null);

    output.Observe(ProcessOutputChannel.StandardError, "token=ghp_012345678901234567890123456789012345");

    Assert.DoesNotContain("ghp_", output.CreateResultLines().Single().Text.Value, StringComparison.Ordinal);
}
```

- [x] **Step 2: Run RED**

Observed: compile failure `CS0234` for the missing `DevForge.Infrastructure.Processes` namespace. A later runtime RED proved `ProcessResult` could not preserve an upstream truncation signal, and a concurrency RED reproduced `IndexOutOfRangeException` from simultaneous stdout/stderr observation.

- [x] **Step 3: Implement redaction before observation**

`ProcessOutputRedactor.TryRedact` replaces exact needles without converting them to loggable strings and applies shared credential-shape defenses. `BoundedProcessOutput.Observe` redacts before truncating a physical line, serializes concurrent stream observation, reports only the redacted `ProcessOutputLine`, and continues draining discarded input without retaining it. `ProcessResult.Create` now accepts the proven explicit upstream truncation signal.

- [x] **Step 4: Run GREEN and commit**

```powershell
.\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~ProcessOutputRedactionTests
git add src/DevForge.Application/Contracts/ProcessContracts.cs src/DevForge.Infrastructure/Processes src/DevForge.Infrastructure/Properties tests/DevForge.UnitTests/Application/ProcessContractTests.cs tests/DevForge.IntegrationTests/Infrastructure/Processes
git commit -m "feat(infrastructure): bound and redact process output"
```

Observed: Application truncation regression 1/1 and Infrastructure redaction/bounds/concurrency tests 11/11 PASS; no returned/progress line contains a credential fixture.

## Task 4: Implement safe process execution

**Files:**
- Create: `src/DevForge.Infrastructure/Processes/ITrustedExecutableResolver.cs`
- Create: `src/DevForge.Infrastructure/Processes/TrustedExecutableResolver.cs`
- Create: `src/DevForge.Infrastructure/Processes/WindowsProcessRunner.cs`
- Create: `tests/DevForge.ProcessTestHelper/DevForge.ProcessTestHelper.csproj`
- Create: `tests/DevForge.ProcessTestHelper/Program.cs`
- Modify: `DevForge.sln`
- Modify: `tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/Processes/WindowsProcessRunnerTests.cs`

- [ ] **Step 1: Create a deterministic test-helper contract and write RED tests**

The helper supports fixed verbs only: `echo-args`, `write-streams`, `large-output`, `sleep`, and `spawn-child-and-wait`. It never evaluates input. Integration tests pass spaces, quotes, `&`, `|`, `>`, `$()`, and semicolons and assert they arrive as inert individual arguments.

```csharp
[Fact]
public async Task RunAsync_PreservesInjectionShapedArgumentsAsData()
{
    var command = CommandFactory.Helper("echo-args", "a & whoami", "$(hostname)", "x|y");
    var result = await _runner.RunAsync(command, null, CancellationToken.None);

    Assert.Equal(ProcessTerminationReason.Exited, result.TerminationReason);
    Assert.Equal(0, result.ExitCode);
    Assert.Contains(result.RetainedLines, line => line.Text.Value == "ARG[1]=a & whoami");
}
```

- [ ] **Step 2: Run RED**

Expected: compile failure because the resolver, runner, and helper do not exist.

- [ ] **Step 3: Implement trusted executable resolution**

Map only `ExecutableTool` values. Prefer explicit test resolver injection; production resolution uses trusted Windows discovery candidates and `PATH` entries without invoking a shell. Require a regular local executable file and reject reparse candidates. Do not expose the resolved path in public results.

Missing/unstartable executables are mapped to `InfrastructureOperationException` code `DF-PROC-001` with a fixed scrubbed message; raw candidate paths and caught exception messages are not retained.

- [ ] **Step 4: Implement process start and asynchronous pumps**

Construct `ProcessStartInfo` with `UseShellExecute=false`, `CreateNoWindow=true`, redirects enabled, guarded working directory, and one `ArgumentList.Add` per argument. Copy validated environment entries only at the final boundary. Start both line pumps immediately and keep draining after retention truncates.

```csharp
var startInfo = new ProcessStartInfo(resolvedExecutable)
{
    UseShellExecute = false,
    CreateNoWindow = true,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    WorkingDirectory = resolvedWorkingDirectory,
};
foreach (var argument in command.ArgumentList)
{
    startInfo.ArgumentList.Add(argument);
}
```

- [ ] **Step 5: Implement one completion race**

Race `WaitForExitAsync` against the command timeout and caller cancellation. On timeout/cancellation, call `Kill(entireProcessTree: true)`, await exit and both pumps, and return the matching `ProcessTerminationReason` without an exit code. Confirmed normal exit wins over later cancellation.

- [ ] **Step 6: Run process security GREEN**

Cover exit codes, allowed/disallowed exit-code transport, stdout/stderr, progress, long/large output, redaction, timeout, pre/mid-flight cancellation, child-tree termination, missing executable, and working-directory revalidation. Verify the child PID is no longer alive before the test completes.

- [ ] **Step 7: Commit**

```powershell
git add DevForge.sln src/DevForge.Infrastructure/Processes tests/DevForge.ProcessTestHelper tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj tests/DevForge.IntegrationTests/Infrastructure/Processes tests/DevForge.UnitTests/Infrastructure/Processes
git commit -m "feat(infrastructure): execute trusted processes safely"
```

## Task 5: Implement bounded workspace secret scanning

**Files:**
- Create: `src/DevForge.Infrastructure/Security/SecretPatternCatalog.cs`
- Create: `src/DevForge.Infrastructure/Security/WorkspaceSecretScanner.cs`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/Security/WorkspaceSecretScannerTests.cs`

- [ ] **Step 1: Write detection, false-positive, and completeness tests**

Cover whole-workspace and explicit scopes; PEM, bearer/JWT, GitHub/OpenAI/AWS, secret assignments, connection strings, `.env` structures; binary files; oversized files/lines; unreadable files; cancellation; missing explicit files; and duplicate paths. Assert finding objects, exception text, and captured test output do not contain the fixture value.

```csharp
[Fact]
public async Task ScanAsync_ReturnsCategoryWithoutMatchedSecret()
{
    await _workspace.WriteTextAsync("config.txt", "Authorization: Bearer aaa.bbb.ccc");
    var result = await _scanner.ScanAsync(WholeWorkspaceRequest(), CancellationToken.None);

    var finding = Assert.Single(result.Findings);
    Assert.Equal("config.txt", finding.Path.Value);
    Assert.DoesNotContain("aaa.bbb.ccc", finding.Description.Value, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run RED**

Expected: compile failure for the scanner implementation.

- [ ] **Step 3: Implement bounded scanning through `IWorkspaceFileSystem` only**

Enumerate guarded files, read a bounded prefix to classify binary text, enforce exact per-file and per-line byte/character limits, and scan streaming lines. Missing, unreadable, binary, or oversized content must not be silently declared clean: throw `InfrastructureOperationException` code `DF-SCAN-001` with a fixed scrubbed message when scan completeness cannot be guaranteed. Construct findings with category-only `RedactedText`.

- [ ] **Step 4: Run GREEN and commit**

```powershell
git add src/DevForge.Infrastructure/Security tests/DevForge.UnitTests/Infrastructure/Security tests/DevForge.IntegrationTests/Infrastructure/Security
git commit -m "feat(infrastructure): scan workspaces for secrets"
```

Expected: all scanner tests PASS and a repository search finds no committed credential fixture.

## Task 6: Implement environment inspection and trusted IDE handoff

**Files:**
- Create: `src/DevForge.Infrastructure/Environment/EnvironmentProbeCatalog.cs`
- Create: `src/DevForge.Infrastructure/Environment/WindowsEnvironmentDoctor.cs`
- Create: `src/DevForge.Infrastructure/Ide/IdeCatalog.cs`
- Create: `src/DevForge.Infrastructure/Ide/WindowsIdeLauncher.cs`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/Environment/WindowsEnvironmentDoctorTests.cs`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/Ide/WindowsIdeLauncherTests.cs`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/Environment/EnvironmentDoctorTests.cs`

- [ ] **Step 1: Write fixed-probe environment RED tests**

Use a recording `IProcessRunner` to assert each supported tool uses a typed executable, fixed version arguments, a short positive timeout, no sensitive environment, and cancellation. Assert missing tools become normalized unavailable snapshots without raw paths/output.

- [ ] **Step 2: Implement the fixed probe catalog and doctor**

Each catalog entry contains only an `ExecutableTool`, immutable fixed arguments, and a version parser operating on already-redacted bounded output. The doctor probes the current M3 set deterministically and creates `EnvironmentSnapshot` only through its guarded factory.

- [ ] **Step 3: Write IDE lifecycle RED tests**

Prove only closed `IdeId` values map to VS Code or Visual Studio, workspace root is passed as one inert argument, `UseShellExecute=false`, no elevation verb exists, and successful handoff does not wait for the interactive IDE to exit. Automated tests use a recording launch adapter/test helper and never open the user's IDE.

- [ ] **Step 4: Implement the minimum dedicated handoff path**

If the RED test confirms `IProcessRunner` cannot represent detached interactive launch, introduce an internal `IInteractiveProcessLauncher` in Infrastructure, not a public raw-command Application port. It accepts only an `ExecutableIdentity` plus a previously opened `IWorkspaceFileSystem`; its implementation reuses the trusted resolver and guarded root, adds the root as one argument, starts non-elevated, and immediately disposes the `Process` handle.

- [ ] **Step 5: Run GREEN and commit**

```powershell
git add src/DevForge.Infrastructure/Environment src/DevForge.Infrastructure/Ide tests/DevForge.UnitTests/Infrastructure/Environment tests/DevForge.UnitTests/Infrastructure/Ide tests/DevForge.IntegrationTests/Infrastructure/Environment
git commit -m "feat(infrastructure): inspect tools and launch trusted IDEs"
```

Expected: focused UnitTests and IntegrationTests PASS; no GUI process is opened.

## Task 7: Security and regression hardening

**Files:**
- Create: `tests/DevForge.IntegrationTests/Infrastructure/InfrastructureSecurityTests.cs`
- Modify only scoped M3 files whose tests first fail.

- [ ] **Step 1: Add adversarial cross-component tests**

Exercise argument metacharacters, environment-variable secret redaction, working-directory link replacement, link insertion during enumeration, locked files, destination creation races, output progress callbacks that throw, cancellation during output, and process-start failures. Assert outside sentinels remain unchanged and fixture credentials never appear in returned values or captured logs.

- [ ] **Step 2: Capture each RED before fixing**

Run the exact focused test filter and retain the failed assertion/compiler error in the work log. Do not modify production code for a hypothetical issue that a deterministic test cannot reproduce.

- [ ] **Step 3: Apply minimal regression fixes**

Change only the guard, runner, redactor, scanner, doctor, or launcher responsible for the failing invariant. Add a stable test name describing the recovered defect and keep the test green permanently.

- [ ] **Step 4: Run M3 suites twice and commit**

```powershell
.\.tools\dotnet\dotnet.exe test tests\DevForge.UnitTests\DevForge.UnitTests.csproj --configuration Release --filter "FullyQualifiedName~Infrastructure|FullyQualifiedName~Architecture"
.\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj --configuration Release --filter FullyQualifiedName~Infrastructure
git add src/DevForge.Infrastructure tests/DevForge.UnitTests/Architecture/InfrastructureBoundaryTests.cs tests/DevForge.UnitTests/Infrastructure tests/DevForge.IntegrationTests/Infrastructure
git commit -m "test(infrastructure): harden Windows security boundaries"
```

Expected: both repeated runs PASS with zero failed and zero skipped M3 tests.

## Task 8: Run the exit gate and record exact evidence

**Files:**
- Modify: `docs/implementation-plan.md`
- Modify: `docs/implementation-status.md`
- Modify: `docs/superpowers/plans/2026-08-10-m3-core-infrastructure.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Format and inspect scope**

Run `dotnet format` without claiming success until verification passes. Inspect `git diff --check`, every changed path, project references, and package locks. Confirm the two pre-existing CRLF-only Application test marks remain unstaged and no secret/build/temp fixture is tracked.

- [ ] **Step 2: Run the full fresh exit gate**

```powershell
.\.tools\dotnet\dotnet.exe restore DevForge.sln --locked-mode --verbosity minimal
.\.tools\dotnet\dotnet.exe format DevForge.sln --verify-no-changes --no-restore --verbosity minimal
.\.tools\dotnet\dotnet.exe build DevForge.sln --configuration Release --no-restore --verbosity minimal
.\.tools\dotnet\dotnet.exe test DevForge.sln --configuration Release --no-build --no-restore
.\.tools\dotnet\dotnet.exe test tests\DevForge.UnitTests\DevForge.UnitTests.csproj --configuration Release --no-build --no-restore --filter "FullyQualifiedName~Infrastructure|FullyQualifiedName~Architecture"
.\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj --configuration Release --no-build --no-restore --filter FullyQualifiedName~Infrastructure
```

Expected: every command exits 0; Release build reports zero warnings/errors; every M3 test passes with zero skipped; the full solution retains all M0-M2 passes.

- [ ] **Step 3: Update exact status evidence**

Mark M3 Complete only after copying the actual SDK version, commands, exit codes, test counts, warnings, errors, and skipped counts. Record any host prerequisite that was genuinely exercised; do not convert an unexecuted security test into a passed gate.

- [ ] **Step 4: Commit milestone documentation**

```powershell
git add docs/decisions/0005-guarded-windows-infrastructure-boundaries.md docs/implementation-plan.md docs/implementation-status.md docs/superpowers/plans/2026-08-10-m3-core-infrastructure.md CHANGELOG.md
git commit -m "docs: complete M3 core infrastructure milestone"
```

## Exit gate

M3 passes only when all five Infrastructure ports are production-backed; process execution proves separated arguments, no shell/elevation, bounded redacted output, timeout/cancellation, and descendant termination; workspace tests prove canonical/reparse containment and no-overwrite semantics; scanner findings contain no secret values; environment/IDE behavior is typed and guarded; and locked restore, format, Release build, full tests, and focused security suites are freshly green with exact evidence.

## Explicitly deferred

- M4 catalog loading, compatibility evaluation, planner rules, and plan hashing.
- M5 orchestration, retry execution, staging ownership, resume, and finalization workflow.
- M6 Desktop composition and UI.
- M8 Git and M9 GitHub automation.
- M10 packaging, retention execution, and support bundles.
