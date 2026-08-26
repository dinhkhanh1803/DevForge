# M10 Security, Diagnostics, Packaging, and Release Hardening Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Harden the MVP against required hostile inputs, deliver privacy-safe local diagnostics and cleanup, prove accessible native Desktop behavior, and produce a verified self-contained `win-x64` release package.

**Architecture:** Application defines bounded typed policies and coordinates capabilities; Infrastructure performs guarded Windows file/archive/log/package operations; Desktop presents only safe immutable results. Every destructive path requires workspace containment plus ownership evidence, every diagnostic payload is redacted before persistence/export, and release completion is evidence-driven.

**Tech Stack:** C# 14, WPF on .NET 10, CommunityToolkit.Mvvm, Generic Host logging, EF Core SQLite, xUnit 2.9.3, MSBuild self-contained `win-x64` publish.

---

## Global execution rules

- Work only in `E:\MyProjects\DevForge\.worktrees\m4-m11-completion` on `codex/m4-m11-completion`.
- Use `E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe` and append `--disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false` to build/test commands where accepted.
- Begin every behavior change with a focused failing test and capture the RED reason.
- Product filesystem effects go through `IFileSystem`/`IWorkspaceFileSystem`; product commands go through `IProcessRunner` with typed executable identity and separate arguments.
- Never include a secret fixture value in production logs, reports, database rows, notifications, or committed documentation.
- Commit each task locally. Do not push or create/mutate a remote.
- Do not mark M9 or M10 complete until the required Windows 11 matrices have real evidence.

## File map

- `src/DevForge.Application/Contracts/SecurityHardeningContracts.cs`: immutable hardening audit inputs/results.
- `src/DevForge.Application/Contracts/DiagnosticsContracts.cs`: structured-event, retention, support-export contracts.
- `src/DevForge.Application/Diagnostics/SupportBundleCoordinator.cs`: policy orchestration without IO.
- `src/DevForge.Infrastructure/FileSystem/WindowsFileSystem.cs`: validated workspace provisioning in addition to opening.
- `src/DevForge.Infrastructure/Diagnostics/JsonLinesDiagnosticSink.cs`: bounded redacted daily/run JSONL persistence.
- `src/DevForge.Infrastructure/Diagnostics/DiagnosticRetentionService.cs`: age/size enforcement over owned local artefacts.
- `src/DevForge.Infrastructure/Diagnostics/SupportBundleWriter.cs`: allowlisted deterministic atomic ZIP export.
- `src/DevForge.Desktop/Execution/*` and `src/DevForge.Desktop/RunHistory/*`: capability-driven diagnostics actions.
- `src/DevForge.Desktop/Settings/*`: bounded retention settings.
- `src/DevForge.Desktop/DevForge.Desktop.csproj`: self-contained publish profile defaults and release metadata.
- `scripts/Test-ReleasePackage.ps1`: deterministic package audit/smoke driver; it invokes only fixed repository commands and never enters the product runtime.
- `docs/release-checklist.md` plus user/maintainer/blueprint/troubleshooting/privacy guides: release evidence and operator documentation.

### Task 1: Security closure and guarded local-data provisioning

**Files:**
- Modify: `src/DevForge.Application/Contracts/FileSystemContracts.cs`
- Modify: `src/DevForge.Infrastructure/FileSystem/WindowsFileSystem.cs`
- Modify: `src/DevForge.Infrastructure/Persistence/LocalDataRootProvisioner.cs`
- Modify: `src/DevForge.Desktop/Bootstrap/DesktopHostBuilder.cs`
- Create: `tests/DevForge.IntegrationTests/Infrastructure/Security/M10HostileInputMatrixTests.cs`
- Modify: `tests/DevForge.IntegrationTests/Persistence/LocalDataRootProvisionerTests.cs`
- Modify: `tests/DevForge.UnitTests/Architecture/InfrastructureBoundaryTests.cs`
- Modify: `docs/implementation-plan.md`, `docs/implementation-status.md`, `CHANGELOG.md`

- [x] **Step 1: Add RED tests for provisioning through the filesystem port**

Use a recording `IFileSystem` and require the provisioner to call the typed method once, propagate cancellation, and map containment/IO failures to `DF-FS-001` without exposing a path:

```csharp
public interface IFileSystem
{
    Task EnsureWorkspaceExistsAsync(WorkspaceRoot allowedRoot, CancellationToken cancellationToken);
    Task<IWorkspaceFileSystem> OpenWorkspaceAsync(WorkspaceRoot allowedRoot, CancellationToken cancellationToken);
}

[Fact]
public async Task ProvisionerDelegatesValidatedRootToFileSystemPort()
{
    var fileSystem = new RecordingFileSystem();
    var location = DatabaseLocation.Create(_root, "devforge.db").Value;

    await new LocalDataRootProvisioner(fileSystem).EnsureExistsAsync(location, CancellationToken.None);

    Assert.Equal(WorkspaceRoot.Create(_root).Value, Assert.Single(fileSystem.EnsuredRoots));
}
```

- [x] **Step 2: Run the focused test and confirm RED**

Run:

```powershell
& E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe test tests\DevForge.IntegrationTests\DevForge.IntegrationTests.csproj -c Release --filter "FullyQualifiedName~LocalDataRootProvisionerTests" --disable-build-servers -m:1 -nodeReuse:false -p:UseSharedCompilation=false
```

Expected: compile failure because `IFileSystem.EnsureWorkspaceExistsAsync` and the injected provisioner constructor do not exist.

- [x] **Step 3: Implement the minimal guarded bootstrap operation**

Add the method above. In `WindowsFileSystem`, validate the typed root, create only that canonical directory, immediately open `WorkspacePathGuard`, and map expected failures to `InfrastructureOperationException`. Change `LocalDataRootProvisioner` to depend on `IFileSystem`; remove its direct `Directory.CreateDirectory`. Register it through DI without a factory escape.

```csharp
public sealed class LocalDataRootProvisioner(IFileSystem fileSystem) : ILocalDataRootProvisioner
{
    private readonly IFileSystem _fileSystem = fileSystem ?? throw new ArgumentNullException(nameof(fileSystem));

    public Task EnsureExistsAsync(DatabaseLocation location, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(location);
        var root = WorkspaceRoot.Create(location.LocalDataRoot);
        if (!root.IsValid)
        {
            throw new InfrastructureOperationException("DF-FS-001", "The local application data root is invalid.");
        }

        return _fileSystem.EnsureWorkspaceExistsAsync(root.Value, cancellationToken);
    }
}
```

- [x] **Step 4: Add the hostile-input release matrix RED tests**

Create theory cases that load/validate a malicious package and assert failure occurs before a recording process runner or mutating workspace method is called. Include traversal, rooted/device/UNC path, `.env`, reserved evidence, checksum mismatch, untrusted/quarantined trust, arbitrary handler, PowerShell/cmd/bash identity, registry/firewall/service/admin/download intent, nested secret-shaped map key, malformed/duplicate/oversized controls, log control characters, junction escape, and secret corpus. Assertions compare error codes and safe summaries, never echo the hostile value.

```csharp
[Theory]
[MemberData(nameof(HostileActions))]
public void HostileActionsFailClosed(BlueprintActionDefinition action)
{
    var issues = BlueprintActionPolicy.Validate(action, BlueprintTrust.BuiltIn);
    Assert.NotEmpty(issues);
    Assert.All(issues, issue => Assert.StartsWith("DF-", issue.Code, StringComparison.Ordinal));
}
```

- [x] **Step 5: Run RED and implement only exposed gaps**

Run filters `M10HostileInputMatrixTests|BlueprintActionPolicyTests|BlueprintPackageLoaderTests|BlueprintControlReaderTests|WorkspaceSecretScannerTests|InfrastructureBoundaryTests`. If a case already passes, retain it as release coverage. For a failure, harden the narrow parser/policy/guard and add the exact regression; do not add a general script or executable denylist as a substitute for the existing allowlist.

- [x] **Step 6: Run Task 1 GREEN and regressions**

Expected: all focused tests pass, no unsafe-effect recorder call occurs, and source scan finds no direct filesystem mutation outside Infrastructure.

- [x] **Step 7: Update docs and commit**

Record exact test counts and remaining Windows 11 debt, then:

```powershell
git add src tests docs/implementation-plan.md docs/implementation-status.md CHANGELOG.md
git commit -m "security(m10): close hostile input boundaries"
```

### Task 2: Structured JSONL diagnostics and bounded retention

**Files:**
- Create: `src/DevForge.Application/Contracts/DiagnosticsContracts.cs`
- Create: `src/DevForge.Infrastructure/Diagnostics/DiagnosticEventNormalizer.cs`
- Create: `src/DevForge.Infrastructure/Diagnostics/JsonLinesDiagnosticSink.cs`
- Create: `src/DevForge.Infrastructure/Diagnostics/DiagnosticRetentionService.cs`
- Modify: `src/DevForge.Desktop/Bootstrap/DesktopHostBuilder.cs`
- Modify: `src/DevForge.Desktop/Settings/DesktopSettings.cs`
- Modify: `src/DevForge.Desktop/Settings/DesktopSettingsService.cs`
- Test: `tests/DevForge.UnitTests/Application/Diagnostics/*`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/Diagnostics/*`

- [ ] **Step 1: Define RED contract tests**

Require `DiagnosticEvent.Create` to validate UTC timestamp, level, event ID, optional bounded run/step IDs, attempt, source, `RedactedText`, duration, and error code. Require `DiagnosticRetentionPolicy.Create` to accept 1-365 days and 16 MiB-2 GiB with defaults 30 days/256 MiB.

```csharp
public sealed record DiagnosticEvent(
    DateTimeOffset TimestampUtc,
    DiagnosticLevel Level,
    string EventId,
    string? RunId,
    string? StepId,
    int? Attempt,
    string Source,
    RedactedText Message,
    long? DurationMs,
    string? ErrorCode);
```

- [ ] **Step 2: Implement immutable validation and canonical serialization**

Serialize one canonical UTF-8 JSON object per line with fixed property order. Normalize CR/LF/tab/C0 controls in identifiers and source; message is already redacted but is scanned again before persistence. Cap an event at 32 KiB and replace oversized message text with `[DIAGNOSTIC TRUNCATED]`.

- [ ] **Step 3: Add RED integration tests for daily/run sinks**

Prove simultaneous writes remain parseable, daily and run-specific paths are deterministic, no partial line is observed after cancellation/fault injection, required fields exist, and secret/control-character fixtures never survive.

- [ ] **Step 4: Implement sinks through guarded workspaces**

Use `IAtomicFileWorkspaceFileSystem` for bounded snapshot publication or a dedicated guarded append capability that keeps the OS handle inside Infrastructure. Do not pass absolute log paths across the port.

- [ ] **Step 5: Add RED retention tests and implement deterministic cleanup**

Sort eligible owned artefacts by timestamp then relative path. Delete expired files first, then oldest remaining files until under the byte ceiling. Never delete the active day's file, database, blueprints, finalized projects, unowned folders, reparse points, or support bundles currently under lease.

- [ ] **Step 6: Compose settings/host, run GREEN, update docs, commit**

Run Unit diagnostics, Integration diagnostics/file guards, Desktop settings/host tests, then full affected suites. Commit as `feat(m10): add structured diagnostics retention`.

### Task 3: Privacy-safe support bundle and owned cleanup

**Files:**
- Create: `src/DevForge.Application/Diagnostics/SupportBundleCoordinator.cs`
- Create: `src/DevForge.Infrastructure/Diagnostics/SupportBundleWriter.cs`
- Modify: `src/DevForge.Application/Contracts/DiagnosticsContracts.cs`
- Modify: `src/DevForge.Desktop/Bootstrap/DesktopHostBuilder.cs`
- Test: `tests/DevForge.UnitTests/Application/Diagnostics/SupportBundleCoordinatorTests.cs`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/Diagnostics/SupportBundleWriterTests.cs`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/Diagnostics/SupportBundleSecurityTests.cs`

- [ ] **Step 1: Write RED canonical request/result tests**

```csharp
public sealed record SupportBundleRequest(string RunId, bool IncludeEnvironmentSnapshot);
public sealed record SupportBundleReceipt(
    string BundleId,
    WorkspaceRelativePath RelativePath,
    string Sha256,
    long Length,
    DateTimeOffset CreatedAtUtc);
```

Reject missing/noncanonical IDs and safe-mode requests that would need mutation outside `support-bundles`.

- [ ] **Step 2: Write RED allowlist/privacy/archive tests**

Require only scrubbed recipe, plan, journal/checkpoint, step results, manifest/checksum, generation report, tool status, redacted logs, error-catalog excerpt, and `inventory.json`. Assert ZIP names are canonical forward-slash relative names with no `..`, drive, UNC, duplicate, NTFS ADS, `.env`, database, source-tree, credential, or executable entry.

- [ ] **Step 3: Implement canonical inventory and deterministic ZIP**

Use fixed entry ordering, normalized UTF-8, SHA-256 per entry, bounded entry/aggregate limits, and a generated schema/version. Write to a run-owned temporary directory, scan every byte, then atomically move one completed archive into `support-bundles` without overwrite.

- [ ] **Step 4: Add kill-window and cleanup tests**

Inject failure before/after every entry and before atomic publish. Retry must produce the same final bytes; no partial archive is claimed. Cleanup must verify bundle/staging markers and remain idempotent.

- [ ] **Step 5: Run GREEN, security regressions, docs, commit**

Commit as `feat(m10): export privacy-safe support bundles`.

### Task 4: Desktop diagnostics, keyboard accessibility, and scaling

**Files:**
- Modify: `src/DevForge.Desktop/Execution/ExecutionCenterViewModel.cs`
- Modify: `src/DevForge.Desktop/Execution/ExecutionCenterView.xaml`
- Modify: `src/DevForge.Desktop/RunHistory/RunHistoryActionCoordinator.cs`
- Modify: `src/DevForge.Desktop/RunHistory/RunHistoryViewModel.cs`
- Modify: `src/DevForge.Desktop/RunHistory/RunHistoryView.xaml`
- Modify: `src/DevForge.Desktop/Settings/SettingsViewModel.cs`
- Modify: `src/DevForge.Desktop/Settings/SettingsView.xaml`
- Test: `tests/DevForge.E2ETests/Desktop/*Diagnostics*Tests.cs`
- Test: `tests/DevForge.E2ETests/Desktop/DesktopAccessibilityTests.cs`

- [ ] **Step 1: Add RED ViewModel capability tests**

Assert support export/open/copy actions are enabled only from authoritative snapshots, disabled while busy, refuse mutation in safe mode, return redacted notifications, and never expose a raw staging path as cleanup authority.

- [ ] **Step 2: Implement commands through coordinators**

Use `IAsyncRelayCommand`, cancellation, single-flight guards, and typed receipts. Interactive folder/IDE launch remains behind the existing launcher boundary; ViewModels do not use `Process`, `Directory`, `File`, or shell APIs.

- [ ] **Step 3: Add RED XAML accessibility/scaling contracts**

Parse all Desktop XAML and require automation names for actionable controls, icon plus text for statuses, no fixed content heights that clip at 150%, virtualization for logs, logical tab order, visible focus, text wrapping, and explicit one-way binding for read-only indicators.

- [ ] **Step 4: Implement minimal XAML/resource changes and failure focus**

Use shared resource tokens and grid/star/auto sizing. Selection/focus moves to the first failed step through ViewModel state; code-behind remains presentation-only and contains no IO/process logic.

- [ ] **Step 5: Run Desktop E2E, manual 100/125/150 smoke, docs, commit**

Record the exact host/display evidence separately from automated contracts. Commit as `feat(desktop): enable hardened diagnostics ux`.

### Task 5: Self-contained win-x64 package and upgrade smoke

**Files:**
- Create: `src/DevForge.Desktop/Properties/PublishProfiles/ReleaseWinX64.pubxml`
- Modify: `src/DevForge.Desktop/DevForge.Desktop.csproj`
- Create: `scripts/Test-ReleasePackage.ps1`
- Create: `tests/DevForge.E2ETests/Release/ReleasePackageContractTests.cs`
- Create: `tests/DevForge.E2ETests/Release/ReleaseUpgradeTests.cs`
- Modify: `.github/workflows/ci.yml`

- [ ] **Step 1: Add RED package contract tests**

Assert fixed RID `win-x64`, self-contained true, single-file false, ReadyToRun choice explicit, deterministic version metadata, no wildcard package/content declaration, and exactly three built-in blueprint roots in publish output.

- [ ] **Step 2: Add the fixed publish profile**

```xml
<PropertyGroup>
  <RuntimeIdentifier>win-x64</RuntimeIdentifier>
  <SelfContained>true</SelfContained>
  <PublishSingleFile>false</PublishSingleFile>
  <PublishReadyToRun>false</PublishReadyToRun>
  <DebugType>embedded</DebugType>
</PropertyGroup>
```

- [ ] **Step 3: Publish and audit from an isolated directory**

Run locked restore/build/tests first, then `dotnet publish` with the fixed profile and explicit output under `artifacts/release/win-x64`. The audit rejects missing EXE/runtime/blueprints/config/docs, unexpected `.env`/database/private-key/script payload, and files outside the output root.

- [ ] **Step 4: Add fresh/upgrade/migration-failure E2E**

Start the packaged EXE with an isolated local-data root test hook already owned by Desktop composition, wait for a responsive main window, and exit cleanly. Repeat with a prior-schema fixture and injected migration failure; verify data preservation, backup, and read-only safe mode. Tests must not require Administrator or a repo-local installed runtime.

- [ ] **Step 5: Add pinned CI release job, run GREEN, docs, commit**

Upload only the audited package after all tests. Pin actions by commit SHA and keep permissions `contents: read`. Commit as `build(m10): verify self-contained release package`.

### Task 6: Documentation, release checklist, and M10 closure

**Files:**
- Create: `docs/user-guide.md`
- Create: `docs/maintainer-guide.md`
- Create: `docs/blueprint-author-guide.md`
- Create: `docs/troubleshooting.md`
- Create: `docs/privacy-and-support-bundles.md`
- Create: `docs/release-checklist.md`
- Create: `docs/decisions/0020-private-local-diagnostics-and-retention.md`
- Create: `docs/decisions/0021-self-contained-win-x64-release.md`
- Modify: `README.md`, `CHANGELOG.md`, `docs/implementation-plan.md`, `docs/implementation-status.md`

- [ ] **Step 1: Add RED documentation/checklist contract**

Require every MVP Must gate—Build, Recovery, Security, Blueprints, UX, Data, Documentation, Packaging—to have a status, exact command/evidence link, host, timestamp, and no unfinished marker or manual-green field. Require guides to cover install, first run, create/recovery/publish, blueprint trust/authoring, diagnostics/privacy, and remediation codes.

- [ ] **Step 2: Write guides and ADRs from observed behavior only**

Do not document unimplemented installer/updater/telemetry/catalog features. Mark Windows 11 items Pending until executed.

- [ ] **Step 3: Run authoritative local release gate**

Run locked restore, format write and verify, Release build, all four test projects, EF pending-model check, self-contained publish/audit/startup/upgrade smoke, blueprint real-tool matrices available on the host, security/privacy/static scans, `git diff --check`, and clean status after commit. Record exact counts and outputs.

- [ ] **Step 4: Run required Windows 11 release matrix**

On a real supported Windows 11 host, repeat WPF/React/Python generated-project matrices, Desktop keyboard/scaling smokes, and packaged fresh/upgrade smokes. No proxy result from Windows 10 or Windows Server may be recorded as Windows 11 evidence.

- [ ] **Step 5: Close or truthfully hold M10**

If every Must item is green, mark M9/M10 complete and recommend M11. Otherwise keep the milestone open, list exact pending gates, and do not use “release ready”. Commit verified documentation as `docs(m10): record release gate evidence`.

## Final acceptance

- Hostile input, secret leakage, path escape, archive slip, arbitrary deletion, shell/admin, and malicious blueprint tests pass.
- Structured logs and support bundles are bounded, redacted, allowlisted, guarded, and recoverable.
- Retention never deletes finalized customer projects or unowned paths.
- Native WPF diagnostics UX works without a terminal and has keyboard/scaling evidence.
- The isolated self-contained `win-x64` package starts and upgrades safely on the supported matrix.
- All MVP release-checklist Must rows have exact evidence; no failed or silently skipped test exists.
- M11 remains blocked until the complete M10 exit gate is green.
