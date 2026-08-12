# M6 Desktop Shell, Settings, and Environment Doctor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Deliver the first production-quality native WPF shell for DevForge Studio with Generic Host composition, persistent settings and theme selection, a cached Environment Doctor, a useful Dashboard, safe startup recovery, and keyboard-accessible Light/Dark UI.

**Architecture:** Keep presentation state and Windows-only adapters in `DevForge.Desktop`, invoke existing Application ports for all persistence/process/filesystem work, and use `App` solely as the Generic Host composition root. Startup is an explicit state machine—migration, interrupted-run recovery, settings/theme, stale-only environment scan, then navigation—with a read-only safe-mode shell when migration cannot be trusted.

**Tech Stack:** C# 14, .NET 10, native WPF, CommunityToolkit.Mvvm 8.4.2, Microsoft.Extensions.Hosting 10.0.10, existing EF Core/SQLite infrastructure, xUnit, Microsoft.NET.Test.Sdk.

---

## Scope and exit gate

M6 implements Dashboard, Settings, Environment Doctor, navigation, notifications, theme resources, startup recovery, and selected WPF smoke coverage. Create Project, Projects, Blueprint Catalog, run execution, GitHub publishing, and catalog management remain M7+; their rail entries may be visible only as disabled, clearly labelled future destinations.

The milestone exits only when locked restore, format verification, Release build, all tests, focused Desktop/E2E tests, and architecture tests pass with zero warnings/errors. No Desktop view model may directly access `System.IO`, EF Core, `Process`, `cmd`, PowerShell, or a cloud/AI API.

## File map

- `src/DevForge.Desktop/Bootstrap/`: Generic Host registration, startup coordinator, safe-mode state.
- `src/DevForge.Desktop/Navigation/`: closed route catalog and navigation service.
- `src/DevForge.Desktop/Notifications/`: bounded in-memory user notifications.
- `src/DevForge.Desktop/Settings/`: typed settings snapshot, persistence coordinator, Settings view model/view.
- `src/DevForge.Desktop/Theming/`: theme preference, Windows system-theme source, resource swapping.
- `src/DevForge.Desktop/EnvironmentDoctor/`: stale-cache coordinator and view model/view.
- `src/DevForge.Desktop/Dashboard/`: dashboard aggregation and view model/view.
- `src/DevForge.Desktop/Shell/`: shell view model and navigation item model.
- `src/DevForge.Desktop/Resources/`: tokens, controls, Light and Dark dictionaries.
- `tests/DevForge.E2ETests/Desktop/`: presentation, startup, host composition, and WPF smoke tests.

### Task 1: Activate the Desktop test/runtime baseline

**Files:**
- Modify: `Directory.Packages.props`
- Modify: `src/DevForge.Desktop/DevForge.Desktop.csproj`
- Modify: `src/DevForge.Desktop/AssemblyInfo.cs`
- Modify: `tests/DevForge.E2ETests/DevForge.E2ETests.csproj`
- Create: `tests/DevForge.E2ETests/Desktop/DesktopAssemblyTests.cs`

- [ ] **Step 1: Write the failing assembly test**

```csharp
namespace DevForge.E2ETests.Desktop;

public sealed class DesktopAssemblyTests
{
    [Fact]
    public void DesktopTargetsWindowsWpfAndExposesApplicationRoot()
    {
        Assert.True(typeof(DevForge.Desktop.App).IsSubclassOf(typeof(System.Windows.Application)));
        Assert.Equal("DevForge.Desktop", typeof(DevForge.Desktop.App).Assembly.GetName().Name);
    }
}
```

- [ ] **Step 2: Run the test and capture RED**

Run:

```powershell
& $env:DOTNET_ROOT\dotnet.exe test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --filter FullyQualifiedName~DesktopAssemblyTests
```

Expected: test project fails to compile because it does not target `net10.0-windows` with WPF or reference Desktop.

- [ ] **Step 3: Pin and reference only the approved packages**

Add centrally pinned versions:

```xml
<PackageVersion Include="CommunityToolkit.Mvvm" Version="8.4.2" />
<PackageVersion Include="Microsoft.Extensions.Hosting" Version="10.0.10" />
```

Add both package references to Desktop. Change E2E to `net10.0-windows`, set `<UseWPF>true</UseWPF>`, and reference `DevForge.Desktop`. Add:

```csharp
using System.Runtime.CompilerServices;

[assembly: InternalsVisibleTo("DevForge.E2ETests")]
```

to Desktop `AssemblyInfo.cs`; do not grant Infrastructure or any dynamic proxy assembly friend access.

- [ ] **Step 4: Run focused GREEN and dependency checks**

Run the focused test, then:

```powershell
& $env:DOTNET_ROOT\dotnet.exe list src/DevForge.Desktop/DevForge.Desktop.csproj reference
& $env:DOTNET_ROOT\dotnet.exe list src/DevForge.Desktop/DevForge.Desktop.csproj package
```

Expected: test passes; project references are Application and Infrastructure only; package list contains only the exact centrally pinned versions.

- [ ] **Step 5: Commit**

```powershell
git add Directory.Packages.props src/DevForge.Desktop tests/DevForge.E2ETests
git commit -m "build(desktop): activate WPF host dependencies"
```

### Task 2: Add closed navigation and bounded notifications

**Files:**
- Create: `src/DevForge.Desktop/Navigation/DesktopRoute.cs`
- Create: `src/DevForge.Desktop/Navigation/NavigationService.cs`
- Create: `src/DevForge.Desktop/Notifications/NotificationService.cs`
- Create: `src/DevForge.Desktop/Shell/ShellViewModel.cs`
- Create: `tests/DevForge.E2ETests/Desktop/NavigationServiceTests.cs`
- Create: `tests/DevForge.E2ETests/Desktop/NotificationServiceTests.cs`

- [ ] **Step 1: Write RED tests for route closure and notification bounds**

```csharp
[Fact]
public void NavigationRejectsDisabledM7Destination()
{
    var sut = new NavigationService();
    Assert.False(sut.TryNavigate(DesktopRoute.CreateProject));
    Assert.Equal(DesktopRoute.Dashboard, sut.CurrentRoute);
}

[Fact]
public void NotificationsRetainNewestTwentyWithoutTechnicalDetails()
{
    var sut = new NotificationService();
    for (var index = 0; index < 21; index++)
        sut.Publish(UserNotification.Info($"Message {index}"));
    Assert.Equal(20, sut.Items.Count);
    Assert.Equal("Message 20", sut.Items[^1].Message);
}
```

- [ ] **Step 2: Run RED**

Expected: compile failure for missing route/navigation/notification types.

- [ ] **Step 3: Implement immutable route metadata and observable services**

Define `DesktopRoute` with exact values `Dashboard`, `CreateProject`, `Projects`, `BlueprintCatalog`, `EnvironmentDoctor`, `Settings`; expose `RouteDescriptor(Route, Label, Glyph, IsEnabled)`. Enable only Dashboard, EnvironmentDoctor, Settings in M6. `NavigationService` derives from `ObservableObject`, starts at Dashboard, exposes `TryNavigate`, and raises `CurrentRoute` changes only for enabled routes.

Define `NotificationSeverity` (`Information`, `Warning`, `Error`), immutable `UserNotification`, and `NotificationService` with an `ObservableCollection<UserNotification>` capped at 20. Messages are bounded to 256 characters and rejected when blank, control-character-containing, or credential-shaped through `RedactedText.FromTrustedRedaction`.

`ShellViewModel` exposes route descriptors, current content route, safe-mode banner state, and commands that call `TryNavigate`; it never creates views or resolves services from a locator.

- [ ] **Step 4: Run focused tests and format**

Expected: all Desktop navigation/notification tests pass and `dotnet format ... --verify-no-changes` exits 0.

- [ ] **Step 5: Commit**

```powershell
git add src/DevForge.Desktop/Navigation src/DevForge.Desktop/Notifications src/DevForge.Desktop/Shell tests/DevForge.E2ETests/Desktop
git commit -m "feat(desktop): add closed shell navigation"
```

### Task 3: Implement typed Settings and first-run checklist

**Files:**
- Create: `src/DevForge.Desktop/Settings/DesktopSettings.cs`
- Create: `src/DevForge.Desktop/Settings/DesktopSettingsService.cs`
- Create: `src/DevForge.Desktop/Settings/SettingsViewModel.cs`
- Create: `tests/DevForge.E2ETests/Desktop/DesktopSettingsServiceTests.cs`
- Create: `tests/DevForge.E2ETests/Desktop/SettingsViewModelTests.cs`

- [ ] **Step 1: Write RED persistence and validation tests**

```csharp
[Fact]
public async Task LoadUsesSafeDefaultsWhenSettingsAreAbsent()
{
    var settings = await new DesktopSettingsService(new FakeSettingsStore(), TimeProvider.System)
        .LoadAsync(CancellationToken.None);
    Assert.Equal(ThemePreference.System, settings.Theme);
    Assert.Equal("en-US", settings.CultureName);
    Assert.False(settings.OnboardingCompleted);
}

[Fact]
public async Task SaveRejectsRelativeProjectRootWithoutWriting()
{
    var store = new FakeSettingsStore();
    var result = await new DesktopSettingsService(store, TimeProvider.System)
        .SaveAsync(new DesktopSettingsDraft("relative", "none", "none", "en-US", ThemePreference.System, false), default);
    Assert.False(result.IsValid);
    Assert.Empty(store.Writes);
}
```

- [ ] **Step 2: Run RED**

Expected: missing Settings types.

- [ ] **Step 3: Implement the exact six-key contract**

Use keys:

```csharp
internal static class DesktopSettingKeys
{
    public const string Theme = "ui.theme";
    public const string Culture = "ui.culture";
    public const string DefaultProjectRoot = "projects.default-root";
    public const string DefaultIdeId = "ide.default-id";
    public const string DefaultTeamProfileId = "team.default-profile-id";
    public const string OnboardingCompleted = "onboarding.completed";
}
```

`DesktopSettingsService.LoadAsync` performs one `ListAsync`, ignores unknown keys, accepts only the exact expected value kind for each known key, defaults invalid/corrupt values safely, and never logs values. `SaveAsync` validates the complete draft first, using `LocalPersistencePathPolicy.TryNormalize` for a non-empty root, bounded identifiers or literal `none` for IDE/team, the closed cultures `en-US`/`vi-VN`, and a defined `ThemePreference`; only then upserts all six settings with one shared UTC timestamp.

`SettingsViewModel` exposes editable properties, `SaveCommand`, `ResetCommand`, `IsBusy`, validation messages, and checklist flags for root, IDE, team standard, language, and completion. Commands are disabled in safe mode and while busy. It accepts `IDesktopSettingsService`, `IThemeService`, and `NotificationService` through its constructor.

- [ ] **Step 4: Run GREEN including write-nothing-on-invalid matrix**

Cover null/relative/UNC/reserved roots; unsupported culture/theme; secret-shaped identifiers; cancellation; store failures mapped to one safe user message; valid round-trip; and an onboarding completion save.

- [ ] **Step 5: Commit**

```powershell
git add src/DevForge.Desktop/Settings tests/DevForge.E2ETests/Desktop
git commit -m "feat(desktop): persist validated user settings"
```

### Task 4: Implement System/Light/Dark theme switching

**Files:**
- Create: `src/DevForge.Desktop/Theming/ThemePreference.cs`
- Create: `src/DevForge.Desktop/Theming/ISystemThemeSource.cs`
- Create: `src/DevForge.Desktop/Theming/WindowsSystemThemeSource.cs`
- Create: `src/DevForge.Desktop/Theming/ThemeService.cs`
- Create: `src/DevForge.Desktop/Resources/Colors.Light.xaml`
- Create: `src/DevForge.Desktop/Resources/Colors.Dark.xaml`
- Create: `tests/DevForge.E2ETests/Desktop/ThemeServiceTests.cs`

- [ ] **Step 1: Write RED tests with a fake system source**

Assert System follows source changes, Light/Dark ignore subsequent source changes, duplicate apply does not duplicate dictionaries, and disposal unsubscribes.

- [ ] **Step 2: Run RED**

Expected: missing theme contracts.

- [ ] **Step 3: Implement deterministic resource replacement**

`ThemePreference` exact values are `System = 1`, `Light = 2`, `Dark = 3`; `EffectiveTheme` is Light/Dark only. `ISystemThemeSource` exposes `EffectiveTheme Current` and `event EventHandler? Changed`.

`WindowsSystemThemeSource` reads only `HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize\AppsUseLightTheme` and observes `SystemEvents.UserPreferenceChanged`; missing/invalid registry data defaults Light. It never writes the registry.

`ThemeService` accepts `Application`, `ISystemThemeSource`, and a dictionary factory. It keeps exactly one color dictionary at merged-dictionary index zero, reapplies only when effective theme changes, and updates on the WPF dispatcher. High-contrast remains owned by WPF system brushes and is not overridden.

- [ ] **Step 4: Run GREEN on an STA test thread**

Expected: all theme tests pass without opening a window.

- [ ] **Step 5: Commit**

```powershell
git add src/DevForge.Desktop/Theming src/DevForge.Desktop/Resources tests/DevForge.E2ETests/Desktop
git commit -m "feat(desktop): add persistent system-aware themes"
```

### Task 5: Implement the stale-only Environment Doctor cache

**Files:**
- Create: `src/DevForge.Desktop/EnvironmentDoctor/EnvironmentHealthItem.cs`
- Create: `src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorService.cs`
- Create: `src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorViewModel.cs`
- Create: `tests/DevForge.E2ETests/Desktop/EnvironmentDoctorServiceTests.cs`
- Create: `tests/DevForge.E2ETests/Desktop/EnvironmentDoctorViewModelTests.cs`

- [ ] **Step 1: Write RED cache tests**

```csharp
[Theory]
[InlineData(14, false)]
[InlineData(15, true)]
public async Task StartupScansOnlyWhenCacheIsAtLeastFifteenMinutesOld(int ageMinutes, bool scans)
{
    // Seed all persisted tool records with the same scanned timestamp.
    // Assert doctor call count and returned Source (Cache/Fresh).
}
```

Also test empty cache, explicit Rescan, partial/mixed timestamps, cancellation, failed scan preserving the last cache, stable ordinal ordering, and no executable paths exposed in the presentation model.

- [ ] **Step 2: Run RED**

Expected: missing service/view-model types.

- [ ] **Step 3: Implement cache mapping and scan policy**

`EnvironmentDoctorService` depends on `IEnvironmentDoctor`, `IEnvironmentToolStore`, and `TimeProvider`. TTL is exactly `TimeSpan.FromMinutes(15)`. Startup uses cache only when non-empty and every record has `ExpiresAt > now`; otherwise it scans. Explicit rescan always scans.

Map fresh `EnvironmentTool(IsAvailable: true)` to `EnvironmentToolStatus.Installed`, false to `Missing`; preserve persisted `Compatible`, `Outdated`, `Conflicting`, and `Unknown` records when serving cache. Persist one `EnvironmentToolRecord` per result with a 15-minute expiry. Presentation exposes ID/name, version, status, scanned time, and source only—never executable path or environment properties.

`EnvironmentDoctorViewModel` exposes status groups, last scan time, stale indicator, `RescanCommand`, busy state, status icon plus text (never color alone), and a bounded copied-diagnostic summary containing product/version/status only.

- [ ] **Step 4: Run focused GREEN**

Expected: cache boundary at exactly 15 minutes, explicit rescan, cancellation, and privacy tests pass.

- [ ] **Step 5: Commit**

```powershell
git add src/DevForge.Desktop/EnvironmentDoctor tests/DevForge.E2ETests/Desktop
git commit -m "feat(desktop): add cached environment doctor"
```

### Task 6: Build Dashboard aggregation without direct filesystem access

**Files:**
- Create: `src/DevForge.Application/Contracts/ProjectLocationContracts.cs`
- Create: `src/DevForge.Infrastructure/FileSystem/GuardedProjectLocationProbe.cs`
- Create: `src/DevForge.Desktop/Dashboard/DashboardSnapshot.cs`
- Create: `src/DevForge.Desktop/Dashboard/DashboardService.cs`
- Create: `src/DevForge.Desktop/Dashboard/DashboardViewModel.cs`
- Create: `tests/DevForge.UnitTests/Application/ProjectLocationContractTests.cs`
- Create: `tests/DevForge.IntegrationTests/Infrastructure/FileSystem/GuardedProjectLocationProbeTests.cs`
- Create: `tests/DevForge.E2ETests/Desktop/DashboardServiceTests.cs`

- [ ] **Step 1: Write RED port and aggregation tests**

Define the intended port test:

```csharp
public interface IProjectLocationProbe
{
    Task<ProjectLocationStatus> InspectAsync(string canonicalRoot, CancellationToken cancellationToken);
}
```

Test that Dashboard lists recent projects in store order, marks missing/moved paths unavailable without throwing, filters checkpoints to Failed/ValidationFailed/PublishPending, exposes saved presets, and provides explicit empty states.

- [ ] **Step 2: Run RED**

Expected: missing project-location port and Dashboard types.

- [ ] **Step 3: Implement guarded existence probing and aggregation**

`ProjectLocationStatus` exact values: `Available`, `Unavailable`, `Invalid`. Infrastructure validates with `WorkspaceRoot.Create`, then uses the existing guarded `IFileSystem.OpenWorkspaceAsync`; expected missing/containment failures map to Unavailable/Invalid and cancellation propagates. No raw path is included in exception messages.

`DashboardService` depends on `IRecentProjectStore`, `IPresetStore`, `IRunCheckpointStore`, `IProjectLocationProbe`, and `IEnvironmentDoctorService`. It uses bounded snapshots (20 recents, 20 actionable runs, 20 presets), stable ordering, and immutable result records. `DashboardViewModel` exposes Refresh and navigation commands; actions unavailable in M6 stay disabled and labelled.

- [ ] **Step 4: Run GREEN and architecture scan**

Add a reflection/source architecture test that fails if a type under `DevForge.Desktop.*ViewModel` references `System.IO`, `System.Diagnostics.Process`, `Microsoft.EntityFrameworkCore`, or Infrastructure concrete types.

- [ ] **Step 5: Commit**

```powershell
git add src/DevForge.Application/Contracts/ProjectLocationContracts.cs src/DevForge.Infrastructure/FileSystem src/DevForge.Desktop/Dashboard tests
git commit -m "feat(desktop): aggregate safe dashboard state"
```

### Task 7: Compose startup, recovery, and migration safe mode

**Files:**
- Create: `src/DevForge.Desktop/Bootstrap/DesktopStartupState.cs`
- Create: `src/DevForge.Desktop/Bootstrap/DesktopStartupCoordinator.cs`
- Create: `src/DevForge.Desktop/Bootstrap/DesktopHostBuilder.cs`
- Modify: `src/DevForge.Desktop/App.xaml`
- Modify: `src/DevForge.Desktop/App.xaml.cs`
- Create: `tests/DevForge.E2ETests/Desktop/DesktopStartupCoordinatorTests.cs`
- Create: `tests/DevForge.E2ETests/Desktop/DesktopHostBuilderTests.cs`

- [ ] **Step 1: Write RED startup-order tests**

Record calls and assert exact order:

```text
migrate -> recover-interrupted -> load-settings -> apply-theme -> load-or-scan-environment -> load-dashboard
```

Test migration Failure/RecoveryFailed opens safe mode, skips recovery and all writes/scans, permits Settings/Doctor cached read-only navigation, and surfaces one user-safe error. Test cancellation before window creation stops the host without showing a partial shell.

- [ ] **Step 2: Run RED**

Expected: missing bootstrap types.

- [ ] **Step 3: Implement Generic Host composition**

Remove `StartupUri`. `App.OnStartup` builds/starts `IHost`, resolves one `MainWindow`, awaits `IDesktopStartupCoordinator.InitializeAsync`, assigns its view model, and shows only after initialization. `OnExit` stops and disposes the host with a five-second cancellation budget.

`DesktopHostBuilder.Create(string localAppDataRoot)` validates `DatabaseLocation.Create(Path.Combine(localAppDataRoot, "DevForge"), "devforge.db")`, registers existing DbContext factory/repositories/migration/recovery/environment/filesystem/process services, Desktop singletons, views, view models, `TimeProvider.System`, and one shared cancellation-safe startup coordinator. No service locator is exposed outside `App`/builder.

`DesktopStartupState` carries `Mode` (`Normal`, `SafeReadOnly`), initial route, user-safe banner, settings snapshot, environment snapshot, and dashboard snapshot. It never carries an exception, connection string, executable path, environment property value, or process output.

- [ ] **Step 4: Run GREEN including registration graph validation**

Build a host in tests with a temporary canonical local-data root and replaced fakes; resolve every Desktop view model and required port; assert singleton/transient lifetimes deliberately and dispose cleanly.

- [ ] **Step 5: Commit**

```powershell
git add src/DevForge.Desktop/Bootstrap src/DevForge.Desktop/App.xaml src/DevForge.Desktop/App.xaml.cs tests/DevForge.E2ETests/Desktop
git commit -m "feat(desktop): compose safe Generic Host startup"
```

### Task 8: Build the native WPF shell and functional views

**Files:**
- Modify: `src/DevForge.Desktop/MainWindow.xaml`
- Modify: `src/DevForge.Desktop/MainWindow.xaml.cs`
- Create: `src/DevForge.Desktop/Resources/Tokens.xaml`
- Create: `src/DevForge.Desktop/Resources/Controls.xaml`
- Create: `src/DevForge.Desktop/Dashboard/DashboardView.xaml`
- Create: `src/DevForge.Desktop/Dashboard/DashboardView.xaml.cs`
- Create: `src/DevForge.Desktop/Settings/SettingsView.xaml`
- Create: `src/DevForge.Desktop/Settings/SettingsView.xaml.cs`
- Create: `src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorView.xaml`
- Create: `src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorView.xaml.cs`
- Modify: `src/DevForge.Desktop/App.xaml`
- Create: `tests/DevForge.E2ETests/Desktop/WpfResourceSmokeTests.cs`

- [ ] **Step 1: Write RED XAML/resource smoke tests**

On an STA dispatcher, instantiate `App`, load all dictionaries, instantiate MainWindow and all three functional views, bind minimal view models, call `ApplyTemplate`, and assert no resource/binding exception. Assert every clickable primary action has non-empty `AutomationProperties.Name`, disabled future routes have help text, and logical tab navigation reaches rail then page actions.

- [ ] **Step 2: Run RED**

Expected: missing views/resources.

- [ ] **Step 3: Implement the approved persistent-left-rail layout**

Use a two-column window: 224px rail and flexible content; minimum 960x640, default 1280x800. Rail contains product header, enabled/disabled route buttons, spacer, Environment Doctor, Settings. Main region contains page header, safe-mode banner, content presenter, and bottom-right notification region.

Use 4px-based tokens, card padding 16/20/24, body 10–11pt equivalent, headings 14–20pt, 44px minimum interactive targets, visible focus styles, text trimming with tooltips, virtualization for lists, and no fixed page height. Status visuals always combine icon, label, and theme color.

Dashboard contains Create Project CTA (disabled with “Available in M7”), Recent Projects, Action Needed, Saved Presets, Environment Health, and explicit empty states. Settings contains the onboarding checklist and editable root/IDE/team/language/theme. Doctor contains summary cards, tool table, stale timestamp, Rescan, Copy Diagnostics.

Code-behind contains only `InitializeComponent`; no business logic or service resolution.

- [ ] **Step 4: Run WPF smoke GREEN at 100%, 125%, 150% DPI contexts**

Use `VisualTreeHelper`/measure-arrange with 960x640, 1200x800, and 1440x960 logical constraints; assert desired sizes remain within constraints and scroll viewers appear instead of clipping.

- [ ] **Step 5: Commit**

```powershell
git add src/DevForge.Desktop tests/DevForge.E2ETests/Desktop
git commit -m "feat(desktop): build accessible native WPF shell"
```

### Task 9: Close Desktop architecture and behavioral matrices

**Files:**
- Create: `tests/DevForge.E2ETests/Desktop/DesktopArchitectureTests.cs`
- Create: `tests/DevForge.E2ETests/Desktop/DesktopBehaviorMatrixTests.cs`
- Modify: `tests/DevForge.UnitTests/Architecture/ProjectDependencyTests.cs` (or the existing dependency-rule test file discovered by `rg`)

- [ ] **Step 1: Add failing architecture assertions**

Assert Desktop references only Domain transitively through Application, Application, Infrastructure, WPF/BCL, CommunityToolkit, and Hosting; Application does not reference Desktop; Infrastructure does not reference Desktop; no type named ViewModel contains fields/properties of an Infrastructure concrete type; code-behind constructors do not accept repositories/ports.

- [ ] **Step 2: Add the exact behavior matrix**

Cover normal first run; returning user with fresh cache; returning user with stale cache; explicit rescan; settings invalid/valid/cancelled/store failure; System/Light/Dark switching; migration created/upgraded/up-to-date/restored/failure/recovery-failed; interrupted-run recovery success/failure; missing recent project; empty Dashboard; safe-mode navigation; and app shutdown during initialization.

- [ ] **Step 3: Run RED and fix only real contract violations**

No production feature is added here. Refactor registrations/dependencies only when a matrix/architecture test demonstrates a violation; add the regression name to the commit body.

- [ ] **Step 4: Run focused Desktop gate**

```powershell
& $env:DOTNET_ROOT\dotnet.exe test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --filter FullyQualifiedName~Desktop -m:1
& $env:DOTNET_ROOT\dotnet.exe test tests/DevForge.UnitTests/DevForge.UnitTests.csproj -c Release --filter FullyQualifiedName~Architecture -m:1
```

Expected: 0 failed, 0 skipped, no warning.

- [ ] **Step 5: Commit**

```powershell
git add src/DevForge.Desktop tests
git commit -m "test(desktop): close M6 behavior and architecture gates"
```

### Task 10: Document decisions and run the M6 exit gate

**Files:**
- Create: `docs/decisions/0010-native-wpf-host-and-theme.md`
- Create: `docs/decisions/0011-desktop-startup-safe-mode.md`
- Modify: `docs/implementation-plan.md`
- Modify: `docs/implementation-status.md`
- Modify: `README.md`

- [ ] **Step 1: Record the approved decisions**

ADR 0010 records native WPF/Generic Host, persistent rail, Settings-based onboarding, System default plus Light/Dark override, resource dictionary replacement, and no embedded browser. ADR 0011 records migration-first startup, recovery-before-editing, 15-minute doctor TTL, safe read-only migration failure, and no writes/scans in safe mode.

- [ ] **Step 2: Update milestone documents truthfully**

Mark only implemented M6 acceptance items complete. Record exact Desktop routes, disabled M7 entries, cache policy, test counts, commands, and any genuine debt. Recommend M7—Dynamic Form Engine and Project Creation Wizard—only after every command below succeeds.

- [ ] **Step 3: Run the fresh serialized gate**

```powershell
& $env:DOTNET_ROOT\dotnet.exe --version
& $env:DOTNET_ROOT\dotnet.exe restore DevForge.sln --locked-mode
& $env:DOTNET_ROOT\dotnet.exe format DevForge.sln --verify-no-changes --no-restore
& $env:DOTNET_ROOT\dotnet.exe build DevForge.sln -c Release --no-restore -m:1
& $env:DOTNET_ROOT\dotnet.exe test DevForge.sln -c Release --no-build --no-restore -m:1
& $env:DOTNET_ROOT\dotnet.exe test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-build --filter FullyQualifiedName~Desktop -m:1
& $env:DOTNET_ROOT\dotnet.exe ef migrations has-pending-model-changes --project src/DevForge.Infrastructure --startup-project src/DevForge.Infrastructure --context DevForgeDbContext --no-build
git diff --check
git status --short
```

Expected: SDK 10.0.302; every command exit 0; all tests pass with zero failed/skipped; EF reports no pending model changes; diff check clean; status contains only intended M6 documentation before the final commit.

- [ ] **Step 4: Commit milestone closure**

```powershell
git add docs README.md
git commit -m "docs: complete M6 desktop shell milestone"
```

- [ ] **Step 5: Verify the final commit and clean tree**

```powershell
git show --stat --oneline HEAD
git status --short
```

Expected: M6 documentation commit shown and empty working tree. Do not push unless the user explicitly requests it in the current task.

## Self-review record

- **Specification coverage:** FR-001–004 are Tasks 3–4; FR-010–012 are Tasks 6 and 8; FR-030–033 are Task 5 with safe diagnostics and explicit rescan; native WPF/MVVM/Generic Host and lifecycle are Tasks 1, 7, 8; keyboard/focus/scaling/themes are Tasks 4, 8, 9; migration/recovery/safe-mode behavior is Tasks 7 and 9.
- **Deliberate deferrals:** project creation, dynamic forms, catalog browsing, run execution UI, IDE launching, and arbitrary official-install URL opening remain M7+. M6 does not introduce an unsafe generic URL/process launcher to simulate FR-033.
- **Type consistency:** `ThemePreference`, `EffectiveTheme`, `DesktopRoute`, `DesktopSettings`, `EnvironmentHealthItem`, `DesktopStartupState`, and service interfaces are defined before their consuming tasks. All async ports end with `CancellationToken`.
- **Security check:** settings reject secret-shaped keys/values; Desktop view models have no filesystem/process/EF dependency; diagnostics omit paths/environment values; migration failure is read-only; external commands still flow only through `IProcessRunner`.
- **Completeness scan:** every implementation step names its concrete contract, validation rule, command, expected result, and commit boundary; no unfinished marker or fake completion claim remains.
