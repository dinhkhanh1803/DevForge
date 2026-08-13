# M7 Create and Execute UX Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a Windows user select a trusted blueprint, complete its schema-driven form, review an immutable plan, execute or recover it through M5, and inspect local-ready evidence without using a terminal.

**Architecture:** Application owns the creation workflow and immutable snapshots; Infrastructure owns canonical target decomposition and guarded workspaces; Desktop owns WPF projection and commands only. Existing M4 planning and M5 execution remain authoritative.

**Tech Stack:** C# 14, .NET 10, WPF, CommunityToolkit.Mvvm 8.4.2, Generic Host 10.0.10, EF Core SQLite 10.0.10, xUnit 2.9.3.

---

## Scope and gate

Implement only M7: dynamic Create Project, catalog presentation, Plan Preview, Execution Center, LocalReady result, presets, and Run History. Git/GitHub remains M8; production blueprints M9; support bundles/log-file browsing/folder shell launch M10. Every task uses RED -> GREEN -> focused regression -> format -> scoped commit.

### Task 1: Immutable M7 creation contracts

**Files:**
- Create: `src/DevForge.Application/Contracts/CreationContracts.cs`
- Test: `tests/DevForge.UnitTests/Application/Creation/CreationContractTests.cs`

- [ ] **Step 1: Write failing contract tests**

Cover Text/Boolean/WholeNumber values, immutable snapshots, explicit enums, stable issue locations, target/plan identity, bounds, and sensitive-key refusal:

```csharp
[Fact]
public void DraftSnapshotsValuesAndRejectsSensitiveKeys()
{
    var values = new Dictionary<string, DynamicInputValue?>
    {
        ["include-tests"] = DynamicInputValue.Boolean(true).Value,
    };
    var draft = ProjectCreationDraft.Create(
        "Client Portal", @"D:\Projects", "client-portal",
        BlueprintReference.Create("sample.local", "1.0.0").Value,
        values, [], "none");
    values["include-tests"] = DynamicInputValue.Boolean(false).Value;

    Assert.True(draft.IsValid);
    Assert.True(draft.Value.Inputs["include-tests"].BooleanValue);
    Assert.False(ProjectCreationDraft.Create(
        "Client Portal", @"D:\Projects", "client-portal",
        BlueprintReference.Create("sample.local", "1.0.0").Value,
        new Dictionary<string, DynamicInputValue?>
        {
            ["githubToken"] = DynamicInputValue.Text("value").Value,
        }, [], "none").IsValid);
}
```

- [ ] **Step 2: Run RED**

`dotnet test tests/DevForge.UnitTests/DevForge.UnitTests.csproj -c Release --no-restore --filter FullyQualifiedName~CreationContractTests -m:1`

Expected: compile failure because M7 contract types do not exist.

- [ ] **Step 3: Implement guarded types**

Add `ProjectCreationStage`, `DynamicInputValueKind`, `DynamicInputValue`, `ProjectCreationDraft`, `ProjectTargetDescriptor`, `ProjectCreationPlanSnapshot`, `ProjectCreationExecutionSnapshot`, `ProjectCreationPresetDraft`, `IProjectTargetPreflight`, `IProjectExecutionWorkspaceFactory`, `IRunIdentityGenerator`, and `IProjectCreationWorkflow`. Factories aggregate issues and snapshot once; async ports end with `CancellationToken`.

- [ ] **Step 4: Run GREEN** for Creation and all Application contracts.

- [ ] **Step 5: Commit** as `feat(application): define guarded project creation contracts`.

### Task 2: Canonical target preflight and workspace factory

**Files:**
- Create: `src/DevForge.Infrastructure/Creation/WindowsProjectTargetService.cs`
- Create: `src/DevForge.Infrastructure/Creation/GuidRunIdentityGenerator.cs`
- Test: `tests/DevForge.IntegrationTests/Infrastructure/Creation/WindowsProjectTargetServiceTests.cs`

- [ ] **Step 1: Write RED tests**

Cover canonical root/folder decomposition, absent target, existing target refusal, file collision, reserved/UNC/device path, root/nested junction, cancellation, write-probe cleanup, artifact scoping, and bounded IDs.

```csharp
[Fact]
public async Task NonEmptyTargetFailsBeforeCreatingRunArtifacts()
{
    await fixture.CreateFileAsync("existing-project\\README.md", "owned by user");
    var result = await fixture.Service.PreflightAsync(
        @"D:\guarded-root", "existing-project", CancellationToken.None);
    Assert.False(result.IsValid);
    Assert.Contains(result.Issues, issue => issue.Code == "project.target.not-empty");
    Assert.False(await fixture.ArtifactDirectoryExistsAsync());
}
```

- [ ] **Step 2: Run RED**, expecting missing Infrastructure services.

- [ ] **Step 3: Implement only through `IFileSystem` guards**

Validate `WorkspaceRoot`/`WorkspaceRelativePath`, reject any existing target, use an atomically claimed run-owned sibling for write probing, and open artifacts below `runs\<run-id>`. Errors never reveal resolved paths. IDs are `run-<32 lowercase hex>` and `recipe-<32 lowercase hex>`.

```csharp
public sealed class WindowsProjectTargetService(
    IFileSystem fileSystem,
    WorkspaceRoot localDataRoot) : IProjectTargetPreflight, IProjectExecutionWorkspaceFactory
{
    public Task<ValidationResult<ProjectTargetDescriptor>> PreflightAsync(
        string rootPath, string outputFolder, CancellationToken cancellationToken);

    public Task<ValidationResult<ProjectExecutionWorkspaces>> OpenAsync(
        ProjectTargetDescriptor target, string runId, CancellationToken cancellationToken);
}
```

- [ ] **Step 4: Run GREEN** plus `WindowsWorkspaceFileSystemTests`.

- [ ] **Step 5: Commit** as `feat(infrastructure): guard project creation targets`.

### Task 3: Privacy-safe preset codec

**Files:**
- Create: `src/DevForge.Application/Creation/ProjectCreationPresetCodec.cs`
- Test: `tests/DevForge.UnitTests/Application/Creation/ProjectCreationPresetCodecTests.cs`

- [ ] **Step 1: Write RED tests** for deterministic schema-version-1 JSON, every dynamic kind, sorted collections, round-trip, unknown/duplicate fields, malformed/oversized JSON, and PEM/Bearer/JWT/GitHub/OpenAI/AWS/connection-string/`.env` defenses.

```csharp
[Theory]
[InlineData("databasepassword")]
[InlineData("clientsecret")]
[InlineData("githubtoken")]
[InlineData("apitoken")]
public void DecodeRejectsSensitiveInputIdentifiers(string key)
{
    var json = PersistableJson.Create(
        $$"""{"schemaVersion":1,"inputs":{"{{key}}":"x"}}""").Value;
    Assert.False(new ProjectCreationPresetCodec().Decode(json).IsValid);
}
```

- [ ] **Step 2: Run RED**, expecting missing codec.

- [ ] **Step 3: Implement** with `Utf8JsonWriter`, exact `JsonDocument` allowlists, 64 KiB/128-item bounds, and existing secret-shape policy. Persist no plan, run, workspace, output, or error detail.

```csharp
public sealed class ProjectCreationPresetCodec
{
    public ValidationResult<PersistableJson> Encode(ProjectCreationPresetDraft? draft);
    public ValidationResult<ProjectCreationPresetDraft> Decode(PersistableJson? document);
}
```

- [ ] **Step 4: Run GREEN** for codec/privacy/persistence contracts.

- [ ] **Step 5: Commit** as `feat(application): persist safe project creation presets`.

### Task 4: Application creation workflow

**Files:**
- Create: `src/DevForge.Application/Creation/ProjectCreationWorkflow.cs`
- Test: `tests/DevForge.UnitTests/Application/Creation/ProjectCreationWorkflowTests.cs`

- [ ] **Step 1: Write planning RED tests** for executable trust, schema/default conversion, target preflight before planner, exact fingerprint, Git disabled, aggregated validation, cancellation, one planner call, immutable snapshot, and stale-plan refusal.

- [ ] **Step 2: Run RED**, expecting missing workflow.

- [ ] **Step 3: Implement planning** by injecting catalog, planner, target preflight/factory, ID generator, orchestrator, recovery, preset store/codec, and time provider. Snapshot, resolve exact blueprint, validate schema, create recipe, preflight, plan once, and bind a draft fingerprint to plan hash.

```csharp
public async Task<ValidationResult<ProjectCreationPlanSnapshot>> CreatePlanAsync(
    ProjectCreationDraft draft,
    CancellationToken cancellationToken)
{
    var target = await _targetPreflight.PreflightAsync(
        draft.RootPath, draft.OutputFolder, cancellationToken).ConfigureAwait(false);
    if (!target.IsValid) return ValidationResult.Failure<ProjectCreationPlanSnapshot>(target.Issues);
    var recipe = CreateRecipe(draft);
    if (!recipe.IsValid) return ValidationResult.Failure<ProjectCreationPlanSnapshot>(recipe.Issues);
    var planned = await _planner.CreatePlanAsync(recipe.Value, cancellationToken).ConfigureAwait(false);
    return planned.IsValid
        ? ProjectCreationPlanSnapshot.Create(draft, target.Value, planned.Value, _ids, _timeProvider)
        : ValidationResult.Failure<ProjectCreationPlanSnapshot>(planned.Issues);
}
```

- [ ] **Step 4: Write execution RED tests** for fresh Draft run, explicit workspace creation, progress forwarding, durable status preservation, no fake Completed, cancellation, resume/retry/cleanup delegation.

- [ ] **Step 5: Implement execution** through `ExecutionRequest.Create`, `IExecutionOrchestrator`, and `IRunRecoveryService`; do not duplicate M5 eligibility.

```csharp
var workspaces = await _workspaceFactory.OpenAsync(
    snapshot.Target, snapshot.RunId, cancellationToken).ConfigureAwait(false);
var run = ProjectRun.Create(snapshot.RunId, snapshot.RecipeId);
var request = ExecutionRequest.Create(
    snapshot.PlannedProject, run.Value,
    workspaces.Value.TargetParent, workspaces.Value.TargetDirectory,
    workspaces.Value.RunArtifacts, ExecutionMode.Fresh);
var checkpoint = await _orchestrator.ExecuteAsync(
    request.Value, progress, cancellationToken).ConfigureAwait(false);
```

- [ ] **Step 6: Run GREEN** for workflow, M4 planner, and M5 orchestrator suites.

- [ ] **Step 7: Commit** as `feat(application): coordinate reviewed project creation`.

### Task 5: Dynamic Configure stage

**Files:**
- Create: `src/DevForge.Desktop/CreateProject/DynamicInputViewModel.cs`
- Create: `src/DevForge.Desktop/CreateProject/CreateProjectViewModel.cs`
- Create: `src/DevForge.Desktop/CreateProject/CreateProjectView.xaml`
- Create: `src/DevForge.Desktop/CreateProject/CreateProjectView.xaml.cs`
- Test: `tests/DevForge.E2ETests/Desktop/CreateProjectViewModelTests.cs`
- Test: `tests/DevForge.E2ETests/Desktop/DynamicInputTemplateTests.cs`

- [ ] **Step 1: Write RED tests** for Text/Choice/Boolean/WholeNumber projection, one-time defaults, exact choices/constraints, blueprint reset, inline issues, slug derivation until manual edit, first-invalid focus, no planning while typing, safe mode, preset load, and plan invalidation.

- [ ] **Step 2: Run RED**, expecting missing types.

- [ ] **Step 3: Implement closed ViewModels** with CommunityToolkit observables and one dynamic-input collection. Expose Review Plan, Save Preset, and Reset only; no execute command and no IO/EF/process/Infrastructure type.

```csharp
public sealed partial class CreateProjectViewModel : ObservableObject
{
    public ReadOnlyObservableCollection<DynamicInputViewModel> Inputs { get; }
    public IAsyncRelayCommand ReviewPlanCommand { get; }
    public IAsyncRelayCommand SavePresetCommand { get; }
    public IRelayCommand ResetCommand { get; }
}
```

- [ ] **Step 4: Add XAML DataTemplates** for labeled TextBox, non-editable ComboBox, CheckBox, and invariant integer TextBox. Bind accessible names, required markers, constraints, and nearby issues. No reflection or code-behind control generation.

```xml
<DataTemplate DataType="{x:Type local:DynamicInputViewModel}">
    <ContentControl Content="{Binding}">
        <ContentControl.Style>
            <Style TargetType="ContentControl">
                <Style.Triggers>
                    <DataTrigger Binding="{Binding EditorKind}" Value="Choice">
                        <Setter Property="ContentTemplate" Value="{StaticResource ChoiceInputTemplate}" />
                    </DataTrigger>
                </Style.Triggers>
            </Style>
        </ContentControl.Style>
    </ContentControl>
</DataTemplate>
```

- [ ] **Step 5: Run GREEN** plus STA WPF smoke.

- [ ] **Step 6: Commit** as `feat(desktop): render blueprint-driven project forms`.

### Task 6: Exact Plan Preview

**Files:**
- Create: `src/DevForge.Desktop/CreateProject/PlanPreviewViewModel.cs`
- Create: `src/DevForge.Desktop/CreateProject/PlanPreviewView.xaml`
- Create: `src/DevForge.Desktop/CreateProject/PlanPreviewView.xaml.cs`
- Test: `tests/DevForge.E2ETests/Desktop/PlanPreviewViewModelTests.cs`

- [ ] **Step 1: Write RED tests** asserting exact ordered hash, blueprint/trust, artifacts, dependencies, tools, steps, validators, inputs, features, warnings, redacted previews, Git-disabled evidence, and plan clearing on edit.

- [ ] **Step 2: Run RED**.

- [ ] **Step 3: Implement immutable projection only**. It accepts `ProjectCreationPlanSnapshot`, never calls the planner, and passes the same reviewed snapshot to Create & Validate.

```csharp
public sealed class PlanPreviewViewModel
{
    public ProjectCreationPlanSnapshot Snapshot { get; }
    public string PlanHash => Snapshot.PlannedProject.Preview.PlanHash;
    public IAsyncRelayCommand CreateAndValidateCommand { get; }
    public IRelayCommand BackToConfigureCommand { get; }
}
```

- [ ] **Step 4: Add accessible virtualized preview XAML** without invented duration/disk estimates or unredacted values.

- [ ] **Step 5: Run GREEN** and commit as `feat(desktop): preview immutable creation plans`.

### Task 7: Execution Center and recovery

**Files:**
- Create: `src/DevForge.Desktop/Execution/ExecutionSessionCoordinator.cs`
- Create: `src/DevForge.Desktop/Execution/ExecutionCenterViewModel.cs`
- Create: `src/DevForge.Desktop/Execution/ExecutionStepViewModel.cs`
- Create: `src/DevForge.Desktop/Execution/ExecutionCenterView.xaml`
- Create: `src/DevForge.Desktop/Execution/ExecutionCenterView.xaml.cs`
- Test: `tests/DevForge.E2ETests/Desktop/ExecutionSessionCoordinatorTests.cs`
- Test: `tests/DevForge.E2ETests/Desktop/ExecutionCenterViewModelTests.cs`

- [ ] **Step 1: Write RED tests** for one active session, second-start refusal, bounded 500-line/64-KiB redacted progress, observer isolation, durable cancellation, shutdown, non-reentrancy, failed-step focus, exact states, and recovery delegation.

- [ ] **Step 2: Run RED**.

- [ ] **Step 3: Implement coordinator/ViewModels** with one CTS per active operation, `finally` disposal, throttled dispatcher publication that never drops the final checkpoint, and plan/checkpoint-derived rows.

```csharp
public async Task<ProjectCreationExecutionSnapshot> ExecuteAsync(
    ProjectCreationPlanSnapshot plan,
    CancellationToken shutdownToken)
{
    if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        throw new InvalidOperationException("A creation session is already active.");
    using var linked = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
    _activeCancellation = linked;
    try { return await _workflow.ExecuteAsync(plan, _progress, linked.Token); }
    finally { _activeCancellation = null; Volatile.Write(ref _active, 0); }
}
```

- [ ] **Step 4: Add WPF view** with virtualized icon+text steps, attempt/duration, redacted details/remediation, and exact Cancel/Resume/Retry/Cleanup enablement. Open Staging and Support Bundle stay disabled for M10.

- [ ] **Step 5: Run GREEN** plus focused M5 cancellation/resume tests.

- [ ] **Step 6: Commit** as `feat(desktop): add recoverable execution center`.

### Task 8: Catalog, Run History, and LocalReady

**Files:**
- Create: `src/DevForge.Desktop/BlueprintCatalog/BlueprintCatalogViewModel.cs`
- Create: `src/DevForge.Desktop/BlueprintCatalog/BlueprintCatalogView.xaml`
- Create: `src/DevForge.Desktop/BlueprintCatalog/BlueprintCatalogView.xaml.cs`
- Create: `src/DevForge.Desktop/RunHistory/RunHistoryViewModel.cs`
- Create: `src/DevForge.Desktop/RunHistory/RunHistoryView.xaml`
- Create: `src/DevForge.Desktop/RunHistory/RunHistoryView.xaml.cs`
- Create: `src/DevForge.Desktop/Execution/LocalReadyViewModel.cs`
- Create: `src/DevForge.Desktop/Execution/LocalReadyView.xaml`
- Create: `src/DevForge.Desktop/Execution/LocalReadyView.xaml.cs`
- Test: `tests/DevForge.E2ETests/Desktop/BlueprintCatalogViewModelTests.cs`
- Test: `tests/DevForge.E2ETests/Desktop/RunHistoryViewModelTests.cs`
- Test: `tests/DevForge.E2ETests/Desktop/LocalReadyViewModelTests.cs`

- [ ] **Step 1: Write RED tests** for executable/inspect-only catalog, deterministic order, exact history status/actions, no manual success, LocalReady label/evidence/warnings, absence of Completed text, and IDE failure without checkpoint mutation.

- [ ] **Step 2: Run RED**, then implement bounded immutable projections.

```csharp
public sealed record BlueprintCatalogItemViewModel(
    string Id, string Version, string TrustLabel, bool CanCreate, string? Issue);

public sealed record RunHistoryItemViewModel(
    string RunId, string StatusLabel, string? CurrentStep, bool CanResume,
    bool CanRetry, bool CanCleanup, string? ErrorCode);

public sealed class LocalReadyViewModel
{
    public string StatusLabel { get; } = "LOCAL PROJECT READY";
    public bool IsDomainCompleted { get; } = false;
}
```

- [ ] **Step 3: Add accessible virtualized WPF views** with existing resources.

- [ ] **Step 4: Run GREEN** and commit as `feat(desktop): expose catalog history and local-ready evidence`.

### Task 9: Compose M4/M5 and activate M7 routes

**Files:**
- Modify: `src/DevForge.Desktop/Bootstrap/DesktopHostBuilder.cs`
- Modify: `src/DevForge.Desktop/Navigation/DesktopRoute.cs`
- Modify: `src/DevForge.Desktop/Navigation/NavigationService.cs`
- Modify: `src/DevForge.Desktop/Shell/ShellViewModel.cs`
- Modify: `src/DevForge.Desktop/MainWindow.xaml`
- Modify: `src/DevForge.Desktop/App.xaml.cs`
- Modify: `src/DevForge.Infrastructure/Execution/ClosedExecutionHandlerRegistryProvider.cs`
- Test: `tests/DevForge.E2ETests/Desktop/DesktopHostBuilderTests.cs`
- Test: `tests/DevForge.E2ETests/Desktop/DesktopBehaviorMatrixTests.cs`
- Test: `tests/DevForge.E2ETests/Desktop/WpfResourceSmokeTests.cs`

- [ ] **Step 1: Write RED composition tests** requiring real M4 planner/catalog, closed M5 handlers/finalizer/recovery, M7 workflow/pages, exact routes, and safe-mode write/scan/execute/recovery refusal.

- [ ] **Step 2: Run RED**.

- [ ] **Step 3: Register the real graph**: blueprint metadata/sources/catalog, M4 schema/rule/runtime/planner, closed M5 handlers/completion, M7 target/workflow, and ViewModels. Roots are explicit validated inputs; ViewModels never construct Infrastructure.

```csharp
services.AddSingleton<IBlueprintCatalog, BlueprintCatalog>();
services.AddSingleton<IProjectPlanner, ProjectPlanner>();
services.AddSingleton<IStagingWorkspaceManager, OwnedStagingWorkspaceManager>();
services.AddSingleton<IRunCompletionCoordinator, ValidatedRunCompletionCoordinator>();
services.AddSingleton<IExecutionOrchestrator, CheckpointedExecutionOrchestrator>();
services.AddSingleton<IRunRecoveryService, RunRecoveryService>();
services.AddSingleton<IProjectCreationWorkflow, ProjectCreationWorkflow>();
services.AddSingleton<ExecutionSessionCoordinator>();
```

- [ ] **Step 4: Activate routes** with Dashboard=1, CreateProject=2, RunHistory=3, BlueprintCatalog=4, EnvironmentDoctor=5, Settings=6. Execution/LocalReady are internal workflow pages. Update DataTemplates and safe-mode routing.

- [ ] **Step 5: Run GREEN** for host, architecture, route matrix, and WPF smoke at 960x640, 1200x800, and 1440x960.

- [ ] **Step 6: Commit** as `feat(desktop): compose M7 creation workflow`.

### Task 10: Real no-terminal E2E fixture

**Files:**
- Create: `tests/DevForge.E2ETests/M7/M7BlueprintFixture.cs`
- Create: `tests/DevForge.E2ETests/M7/ProjectCreationWorkflowE2ETests.cs`

- [ ] **Step 1: Build a temporary TrustedLocal package** containing manifest, all four input kinds, safe create/render actions, file/content validators, README template, and complete checksums. It never enters production `blueprints/`.

- [ ] **Step 2: Write E2E RED** using real M4 catalog/planner, M7 workflow/target, M5 orchestrator/handlers/scanner/report/finalizer, and SQLite stores. Assert absent target before execute, then `RunStatus.LocalReady`, exact files/report, and no Git/remote side effect.

```csharp
var plan = await fixture.Workflow.CreatePlanAsync(fixture.ValidDraft, default);
Assert.True(plan.IsValid);
Assert.False(Directory.Exists(fixture.TargetPath));

var result = await fixture.Workflow.ExecuteAsync(plan.Value, null, default);

Assert.Equal(RunStatus.LocalReady, result.Checkpoint.Run.Status);
Assert.True(File.Exists(Path.Combine(fixture.TargetPath, "README.md")));
Assert.False(Directory.Exists(Path.Combine(fixture.TargetPath, ".git")));
```

- [ ] **Step 3: Run RED** and record the first missing behavior.

- [ ] **Step 4: Apply only the minimal product fix**, rerun to GREEN.

- [ ] **Step 5: Add cancellation/resume and non-empty-target cases**. Cancellation is durable; resume does not duplicate passed evidence; existing bytes are unchanged.

- [ ] **Step 6: Commit** as `test(e2e): prove M7 no-terminal creation flow`.

### Task 11: Architecture, privacy, behavior, and accessibility matrices

**Files:**
- Modify: `tests/DevForge.E2ETests/Desktop/DesktopArchitectureTests.cs`
- Modify: `tests/DevForge.E2ETests/Desktop/DesktopBehaviorMatrixTests.cs`
- Create: `tests/DevForge.E2ETests/Desktop/M7PrivacyTests.cs`
- Modify: `tests/DevForge.UnitTests/Architecture/ProjectDependencyTests.cs` only if the approved graph changes

- [ ] **Step 1: Add RED architecture assertions**: ViewModels cannot reference IO, Process, EF, Infrastructure concretes, WPF controls, or workspace handles; code-behind stays parameterless; Application has no Desktop/WPF dependency.

- [ ] **Step 2: Add exact behavior matrices** for input kinds, routes/safe mode, plan invalidation, run actions, recovery, trust, target states, presets, LocalReady, and disabled M8/M10 actions.

- [ ] **Step 3: Add privacy adversaries**: credentials, `.env`, connection strings, PEM/JWT/Bearer, raw exceptions, long output, and source-like content cannot enter presets, notifications, exposed progress, or diagnostics.

- [ ] **Step 4: Run focused GREEN** for Desktop, Creation, Infrastructure Creation, M7 E2E, Architecture, and Privacy.

- [ ] **Step 5: Commit** as `test(m7): close UX architecture and privacy gates`.

### Task 12: Decisions, status, and exit gate

**Files:**
- Create: `docs/decisions/0012-reviewed-plan-driven-project-creation.md`
- Modify: `docs/implementation-plan.md`
- Modify: `docs/implementation-status.md`
- Modify: `README.md`
- Modify: `CHANGELOG.md`

- [ ] **Step 1: Record ADR-0012** for explicit Review Plan, Application facade, guarded target factory, schema control mapping, invalidation, LocalReady-not-Completed, test-only fixture, and M8/M9/M10 deferrals.

- [ ] **Step 2: Run the fresh serialized gate**

```powershell
& $env:DOTNET_ROOT\dotnet.exe --version
& $env:DOTNET_ROOT\dotnet.exe restore DevForge.sln --locked-mode --verbosity minimal
& $env:DOTNET_ROOT\dotnet.exe format DevForge.sln --verify-no-changes --no-restore
& $env:DOTNET_ROOT\dotnet.exe build DevForge.sln -c Release --no-restore --verbosity minimal -m:1
& $env:DOTNET_ROOT\dotnet.exe test DevForge.sln -c Release --no-build --no-restore --verbosity minimal -m:1
& $env:DOTNET_ROOT\dotnet.exe test tests/DevForge.UnitTests/DevForge.UnitTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Creation|FullyQualifiedName~Architecture|FullyQualifiedName~Privacy" -m:1
& $env:DOTNET_ROOT\dotnet.exe test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Creation|FullyQualifiedName~Execution|FullyQualifiedName~Blueprints" -m:1
& $env:DOTNET_ROOT\dotnet.exe test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-build --no-restore --filter "FullyQualifiedName~Desktop|FullyQualifiedName~M7" -m:1
& 'E:\MyProjects\DevForge\.tools\dotnet-tools\dotnet-ef.exe' migrations has-pending-model-changes --project src/DevForge.Infrastructure --startup-project src/DevForge.Infrastructure --context DevForgeDbContext --configuration Release --no-build
git diff --check
git status --short
```

Expected: SDK 10.0.302; every command exit 0; build 0 warnings/errors; all tests pass with zero skipped M7 tests; EF has no pending model changes; diff check clean.

- [ ] **Step 3: Update documents with exact observed counts only** and recommend M8 only after every gate succeeds.

- [ ] **Step 4: Commit closure**

```powershell
git add docs README.md CHANGELOG.md
git commit -m "docs: complete M7 create and execute UX milestone"
git show --stat --oneline HEAD
git status --short
```

Do not push unless the user explicitly requests it in the current task.

## Self-review record

- **Coverage:** FR-020–024 map to Tasks 1–6/10; catalog presentation to Tasks 5/8 without expansion; FR-050–056 UX to Tasks 4/7/10; FR-060–063 evidence to Tasks 6–8/10; no-terminal exit to Task 10; scaling/accessibility to Tasks 5–9/11.
- **Boundaries:** Desktop never receives workspace handles or constructs Infrastructure; Application uses injected ports; all effects remain guarded.
- **Lifecycle:** M7 presents `LocalReady`; it does not create `Completed`, Git/GitHub, PublishPending, support bundles, or production blueprints.
- **Type consistency:** contracts precede workflow; workflow precedes ViewModels; async ports end with CancellationToken; enums are explicit/nonzero.
- **Placeholder scan:** no TBD/TODO/fake completion step remains; every task names files, RED/GREEN, regressions, and commit boundary.
