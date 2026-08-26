# DevForge Studio UI/UX Redesign Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the functional WPF presentation baseline with a polished, accessible, adaptive Developer Studio design across every existing DevForge Desktop workflow without changing application behavior or security boundaries.

**Architecture:** Keep the current ViewModel, command, stage, route, and service contracts authoritative. Build the redesign as semantic resource dictionaries, reusable WPF styles/templates, two presentation-only converters, and view-level XAML composition; no Desktop presentation code may acquire process, file-system, persistence, generation, or publication responsibilities.

**Tech Stack:** .NET 10, C# 14, WPF XAML, CommunityToolkit.Mvvm 8.4.2, xUnit 2.9.3, Segoe UI Variable, Segoe Fluent Icons, Cascadia Mono/Consolas

---

## Working directory and safety

Execute every command from:

```text
E:\MyProjects\DevForge\.worktrees\m4-m11-completion
```

The worktree also contains concurrent M10 changes. Never use `git add .`, `git add -A`, reset, checkout, clean, stash, or a broad formatter that rewrites unrelated files. Every commit command in this plan lists only UI-owned paths.

Use the pinned SDK for every .NET command:

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' --version
```

Expected: `10.0.302`.

Before Task 1, capture the baseline without mutating it:

```powershell
git status --short
git diff -- src/DevForge.Desktop tests/DevForge.E2ETests/Desktop
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~DevForge.E2ETests.Desktop'
```

Expected: existing concurrent non-Desktop paths may be dirty; Desktop diff is empty; focused Desktop tests pass.

## Planned file map

### Create

- `src/DevForge.Desktop/Resources/Typography.xaml` — typography and evidence text styles.
- `src/DevForge.Desktop/Resources/Icons.xaml` — named Segoe Fluent glyphs.
- `src/DevForge.Desktop/Resources/Components.xaml` — card, badge, callout, empty-state, action-bar, stepper, timeline, toast, and console styles.
- `src/DevForge.Desktop/Presentation/DoubleLessThanConverter.cs` — presentation-only width breakpoint comparison.
- `src/DevForge.Desktop/Presentation/EqualityMultiConverter.cs` — presentation-only route equality comparison.
- `tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs` — source-level design-system and view composition contracts.
- `tests/DevForge.E2ETests/Desktop/PresentationValueConverterTests.cs` — exact converter behavior.
- `tests/DevForge.E2ETests/Desktop/DesktopXamlSmokeTests.cs` — STA resource/view loading and minimum-size smoke coverage.

### Modify

- `src/DevForge.Desktop/App.xaml` — merged resource composition.
- `src/DevForge.Desktop/Resources/Tokens.xaml` — semantic spacing, geometry, sizing, and duration tokens.
- `src/DevForge.Desktop/Resources/Colors.Light.xaml` — complete semantic light palette.
- `src/DevForge.Desktop/Resources/Colors.Dark.xaml` — complete semantic dark palette.
- `src/DevForge.Desktop/Resources/Controls.xaml` — implicit controls and primary/secondary/ghost/danger/navigation variants.
- `src/DevForge.Desktop/MainWindow.xaml` — adaptive shell, navigation, safe-mode callout, content frame, and toasts.
- `src/DevForge.Desktop/Dashboard/DashboardView.xaml` — responsive dashboard hierarchy and empty states.
- `src/DevForge.Desktop/BlueprintCatalog/BlueprintCatalogView.xaml` — blueprint cards and empty state.
- `src/DevForge.Desktop/RunHistory/RunHistoryView.xaml` — recovery-aware run cards and empty state.
- `src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorView.xaml` — health callouts and readable tool table.
- `src/DevForge.Desktop/Settings/SettingsView.xaml` — onboarding, defaults, appearance, validation, and sticky actions.
- `src/DevForge.Desktop/CreateProject/CreateProjectView.xaml` — Configure and Review workflow composition.
- `src/DevForge.Desktop/Execution/ExecutionCenterView.xaml` — execution status, timeline, output, and recovery actions.
- `src/DevForge.Desktop/Execution/LocalReadyView.xaml` — Local Ready, Completed, and Publish Pending hierarchy.

No ViewModel, service, domain, application, infrastructure, persistence, or code-behind change is planned. If a visual requirement appears to need one, first prove that the existing binding surface cannot express it and amend the design before expanding scope.

## Acceptance traceability

| Design acceptance criterion | Plan evidence |
| --- | --- |
| Every existing Desktop view adopts the system | Tasks 3-7 modify MainWindow and all eight routed/workflow views; Task 8 loads each view |
| Light, Dark, and System remain coherent | Tasks 1-2 create matched semantic palettes; Tasks 8-9 load both explicit themes and run existing ThemeService tests |
| 960 x 640 minimum and 1280 x 800 polish | Task 3 adds the adaptive shell; Tasks 8-9 measure and visually inspect both sizes |
| Configure, Review, Execute, Local Ready, Completed, Publish Pending remain distinct | Tasks 6-7 preserve stage bindings and render distinct workflow/state composition |
| Empty, validation, disabled, busy, stale, failure, success, and recovery states | Tasks 2 and 4-7 define the semantic components and bind every state exposed by current models |
| Keyboard, screen reader, focus, and automation | Tasks 2, 3, and 8 protect interaction templates, HelpText, names, focus visuals, and source/runtime contracts |
| No unsupported UI actions | Tasks 4-7 list only existing commands; Task 8 rejects event-handler behavior and retains future actions as disabled |
| Desktop security boundaries remain intact | Every task forbids presentation-side effects; Tasks 6-9 rerun architecture and static boundary checks |
| Automated and visual evidence | Task 8 adds compiled-XAML smoke coverage; Task 9 runs Release gates and screen-by-screen visual QA |
| Concurrent M10 work remains untouched | Every commit stages an explicit UI-owned path set and final status/diff checks audit the commit range |

### Task 1: Lock the semantic resource contract

**Files:**

- Create: `tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs`
- Create: `src/DevForge.Desktop/Resources/Typography.xaml`
- Create: `src/DevForge.Desktop/Resources/Icons.xaml`
- Create: `src/DevForge.Desktop/Resources/Components.xaml`
- Modify: `src/DevForge.Desktop/App.xaml`
- Modify: `src/DevForge.Desktop/Resources/Tokens.xaml`
- Modify: `src/DevForge.Desktop/Resources/Colors.Light.xaml`
- Modify: `src/DevForge.Desktop/Resources/Colors.Dark.xaml`

- [ ] **Step 1: Write the failing semantic resource tests**

Create `DesktopPresentationContractTests.cs` with repository-root discovery, XAML key extraction, and the exact shared palette/app-merge contracts:

```csharp
using System.Text.RegularExpressions;

namespace DevForge.E2ETests.Desktop;

public sealed partial class DesktopPresentationContractTests
{
    private static readonly string[] RequiredSemanticPaletteKeys =
    [
        "Brush.AppBackground", "Brush.Navigation", "Brush.Surface",
        "Brush.SurfaceRaised", "Brush.SurfaceHover", "Brush.Border",
        "Brush.BorderStrong", "Brush.TextPrimary", "Brush.TextSecondary",
        "Brush.TextTertiary", "Brush.Accent", "Brush.AccentHover",
        "Brush.AccentSubtle", "Brush.Info", "Brush.InfoSurface",
        "Brush.Success", "Brush.SuccessSurface", "Brush.Warning",
        "Brush.WarningSurface", "Brush.Error", "Brush.ErrorSurface",
        "Brush.Focus", "Brush.DisabledSurface", "Brush.DisabledText",
    ];

    [Fact]
    public void LightAndDarkThemesExposeTheSameSemanticPalette()
    {
        var light = ResourceKeys("src/DevForge.Desktop/Resources/Colors.Light.xaml");
        var dark = ResourceKeys("src/DevForge.Desktop/Resources/Colors.Dark.xaml");

        Assert.Equal(light.Order(StringComparer.Ordinal), dark.Order(StringComparer.Ordinal));
        Assert.All(RequiredSemanticPaletteKeys, key =>
        {
            Assert.Contains(key, light);
            Assert.Contains(key, dark);
        });
    }

    [Fact]
    public void ApplicationMergesTheCompletePresentationSystemInDependencyOrder()
    {
        var xaml = Read("src/DevForge.Desktop/App.xaml");
        var resources = new[]
        {
            "Resources/Tokens.xaml",
            "Resources/Typography.xaml",
            "Resources/Icons.xaml",
            "Resources/Controls.xaml",
            "Resources/Components.xaml",
        };

        var previous = -1;
        foreach (var resource in resources)
        {
            var index = xaml.IndexOf(resource, StringComparison.Ordinal);
            Assert.True(index > previous, $"{resource} is missing or out of order.");
            previous = index;
        }
    }

    [Fact]
    public void TokensExposeTheAcceptedSpacingGeometryAndControlScale()
    {
        var keys = ResourceKeys("src/DevForge.Desktop/Resources/Tokens.xaml");
        Assert.All(
            ["Space.1", "Space.2", "Space.3", "Space.4", "Space.5", "Space.6",
             "Space.8", "Space.10", "Space.12", "Control.Height.Compact",
             "Control.Height.Standard", "Control.Height.Prominent", "Page.Padding",
             "Page.Padding.Compact", "Card.Padding", "Card.CornerRadius",
             "Surface.CornerRadius", "Motion.Fast", "Motion.Standard"],
            key => Assert.Contains(key, keys));
    }

    private static HashSet<string> ResourceKeys(string relativePath) =>
        KeyExpression().Matches(Read(relativePath))
            .Select(match => match.Groups[1].Value)
            .ToHashSet(StringComparer.Ordinal);

    private static string Read(string relativePath) =>
        File.ReadAllText(Path.Combine(FindRepositoryRoot(),
            relativePath.Replace('/', Path.DirectorySeparatorChar)));

    private static string FindRepositoryRoot()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "DevForge.sln")))
        {
            current = current.Parent;
        }

        return current?.FullName
            ?? throw new InvalidOperationException("Repository root not found.");
    }

    [GeneratedRegex("x:Key=\"([^\"]+)\"", RegexOptions.CultureInvariant)]
    private static partial Regex KeyExpression();
}
```

- [ ] **Step 2: Run the new contract tests and verify RED**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~DesktopPresentationContractTests'
```

Expected: FAIL because semantic keys and merged dictionaries do not exist.

- [ ] **Step 3: Implement tokens and matching semantic palettes**

Replace the current token dictionary with the accepted scale. Keep numeric values as typed resources so they can be reused by styles:

```xml
<sys:Double x:Key="Space.1">4</sys:Double>
<sys:Double x:Key="Space.2">8</sys:Double>
<sys:Double x:Key="Space.3">12</sys:Double>
<sys:Double x:Key="Space.4">16</sys:Double>
<sys:Double x:Key="Space.5">20</sys:Double>
<sys:Double x:Key="Space.6">24</sys:Double>
<sys:Double x:Key="Space.8">32</sys:Double>
<sys:Double x:Key="Space.10">40</sys:Double>
<sys:Double x:Key="Space.12">48</sys:Double>
<sys:Double x:Key="Control.Height.Compact">32</sys:Double>
<sys:Double x:Key="Control.Height.Standard">40</sys:Double>
<sys:Double x:Key="Control.Height.Prominent">44</sys:Double>
<sys:Double x:Key="Motion.Fast">120</sys:Double>
<sys:Double x:Key="Motion.Standard">180</sys:Double>
<Thickness x:Key="Page.Padding">28</Thickness>
<Thickness x:Key="Page.Padding.Compact">20</Thickness>
<Thickness x:Key="Card.Padding">20</Thickness>
<CornerRadius x:Key="Card.CornerRadius">12</CornerRadius>
<CornerRadius x:Key="Surface.CornerRadius">16</CornerRadius>
```

Make `Colors.Light.xaml` and `Colors.Dark.xaml` expose the exact same `Brush.*` keys from the test. Use the approved base colors from the design, plus semantic variants. Example dark values:

```xml
<SolidColorBrush x:Key="Brush.AppBackground" Color="#0B0E14" />
<SolidColorBrush x:Key="Brush.Navigation" Color="#0D111A" />
<SolidColorBrush x:Key="Brush.Surface" Color="#121722" />
<SolidColorBrush x:Key="Brush.SurfaceRaised" Color="#191F2D" />
<SolidColorBrush x:Key="Brush.SurfaceHover" Color="#202738" />
<SolidColorBrush x:Key="Brush.Border" Color="#283043" />
<SolidColorBrush x:Key="Brush.BorderStrong" Color="#3B455C" />
<SolidColorBrush x:Key="Brush.TextPrimary" Color="#F4F7FB" />
<SolidColorBrush x:Key="Brush.TextSecondary" Color="#9DA8BB" />
<SolidColorBrush x:Key="Brush.TextTertiary" Color="#748097" />
<SolidColorBrush x:Key="Brush.Accent" Color="#6C7CFF" />
<SolidColorBrush x:Key="Brush.AccentHover" Color="#7E8BFF" />
<SolidColorBrush x:Key="Brush.AccentSubtle" Color="#2A315B" />
<SolidColorBrush x:Key="Brush.Info" Color="#66B4FF" />
<SolidColorBrush x:Key="Brush.InfoSurface" Color="#142A3F" />
<SolidColorBrush x:Key="Brush.Success" Color="#4FD1A5" />
<SolidColorBrush x:Key="Brush.SuccessSurface" Color="#12362E" />
<SolidColorBrush x:Key="Brush.Warning" Color="#F4C152" />
<SolidColorBrush x:Key="Brush.WarningSurface" Color="#3A2D13" />
<SolidColorBrush x:Key="Brush.Error" Color="#FF7A90" />
<SolidColorBrush x:Key="Brush.ErrorSurface" Color="#3D1822" />
<SolidColorBrush x:Key="Brush.Focus" Color="#9CA7FF" />
<SolidColorBrush x:Key="Brush.DisabledSurface" Color="#1B202C" />
<SolidColorBrush x:Key="Brush.DisabledText" Color="#687286" />
```

Use these exact light values for the same keys:

```xml
<SolidColorBrush x:Key="Brush.AppBackground" Color="#F5F7FB" />
<SolidColorBrush x:Key="Brush.Navigation" Color="#FFFFFF" />
<SolidColorBrush x:Key="Brush.Surface" Color="#FFFFFF" />
<SolidColorBrush x:Key="Brush.SurfaceRaised" Color="#F8FAFD" />
<SolidColorBrush x:Key="Brush.SurfaceHover" Color="#EEF2F8" />
<SolidColorBrush x:Key="Brush.Border" Color="#DCE2EC" />
<SolidColorBrush x:Key="Brush.BorderStrong" Color="#BBC5D5" />
<SolidColorBrush x:Key="Brush.TextPrimary" Color="#172033" />
<SolidColorBrush x:Key="Brush.TextSecondary" Color="#657086" />
<SolidColorBrush x:Key="Brush.TextTertiary" Color="#8993A7" />
<SolidColorBrush x:Key="Brush.Accent" Color="#4F5FE7" />
<SolidColorBrush x:Key="Brush.AccentHover" Color="#4050D6" />
<SolidColorBrush x:Key="Brush.AccentSubtle" Color="#E8EBFF" />
<SolidColorBrush x:Key="Brush.Info" Color="#1976C9" />
<SolidColorBrush x:Key="Brush.InfoSurface" Color="#E8F3FD" />
<SolidColorBrush x:Key="Brush.Success" Color="#178765" />
<SolidColorBrush x:Key="Brush.SuccessSurface" Color="#E3F6EF" />
<SolidColorBrush x:Key="Brush.Warning" Color="#9A6500" />
<SolidColorBrush x:Key="Brush.WarningSurface" Color="#FFF4D6" />
<SolidColorBrush x:Key="Brush.Error" Color="#C43B56" />
<SolidColorBrush x:Key="Brush.ErrorSurface" Color="#FDE9ED" />
<SolidColorBrush x:Key="Brush.Focus" Color="#3447DD" />
<SolidColorBrush x:Key="Brush.DisabledSurface" Color="#EDF0F5" />
<SolidColorBrush x:Key="Brush.DisabledText" Color="#929BAD" />
```

Keep the superseded `Brush.Background`, `Brush.Foreground`, and `Brush.MutedForeground` as matching aliases in both palettes until every view is migrated in Tasks 2-8. Remove those aliases in Task 8 only after the source scan proves they have no consumers.

- [ ] **Step 4: Add typography, icon, and component resource files and merge them**

Define typography families and role styles in `Typography.xaml`:

```xml
<FontFamily x:Key="Font.Ui">Segoe UI Variable Text, Segoe UI</FontFamily>
<FontFamily x:Key="Font.Icon">Segoe Fluent Icons</FontFamily>
<FontFamily x:Key="Font.Mono">Cascadia Mono, Consolas</FontFamily>
<Style x:Key="Text.Display" TargetType="TextBlock">
    <Setter Property="FontFamily" Value="{StaticResource Font.Ui}" />
    <Setter Property="FontSize" Value="36" />
    <Setter Property="FontWeight" Value="SemiBold" />
</Style>
<Style x:Key="Text.PageTitle" TargetType="TextBlock">
    <Setter Property="FontFamily" Value="{StaticResource Font.Ui}" />
    <Setter Property="FontSize" Value="28" />
    <Setter Property="FontWeight" Value="SemiBold" />
</Style>
<Style x:Key="Text.SectionTitle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="20" />
    <Setter Property="FontWeight" Value="SemiBold" />
</Style>
<Style x:Key="Text.CardTitle" TargetType="TextBlock">
    <Setter Property="FontSize" Value="16" />
    <Setter Property="FontWeight" Value="SemiBold" />
</Style>
<Style x:Key="Text.Label" TargetType="TextBlock">
    <Setter Property="FontSize" Value="13" />
    <Setter Property="FontWeight" Value="SemiBold" />
</Style>
<Style x:Key="Text.Caption" TargetType="TextBlock">
    <Setter Property="FontSize" Value="12" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.TextSecondary}" />
</Style>
<Style x:Key="Text.Mono" TargetType="TextBlock">
    <Setter Property="FontFamily" Value="{StaticResource Font.Mono}" />
    <Setter Property="FontSize" Value="13" />
</Style>
```

Define named glyph strings in `Icons.xaml`:

```xml
<sys:String x:Key="Icon.Dashboard">&#xE80F;</sys:String>
<sys:String x:Key="Icon.Create">&#xE710;</sys:String>
<sys:String x:Key="Icon.History">&#xE81C;</sys:String>
<sys:String x:Key="Icon.Catalog">&#xE719;</sys:String>
<sys:String x:Key="Icon.Health">&#xE9D9;</sys:String>
<sys:String x:Key="Icon.Settings">&#xE713;</sys:String>
<sys:String x:Key="Icon.Refresh">&#xE72C;</sys:String>
<sys:String x:Key="Icon.Warning">&#xE7BA;</sys:String>
<sys:String x:Key="Icon.Success">&#xE73E;</sys:String>
<sys:String x:Key="Icon.Error">&#xEA39;</sys:String>
<sys:String x:Key="Icon.Info">&#xE946;</sys:String>
<sys:String x:Key="Icon.Code">&#xE943;</sys:String>
<sys:String x:Key="Icon.Folder">&#xE8B7;</sys:String>
<sys:String x:Key="Icon.Copy">&#xE8C8;</sys:String>
<sys:String x:Key="Icon.Open">&#xE8E5;</sys:String>
```

Create `Components.xaml` with valid empty resource-dictionary structure plus the common `Card`, `PageHeading`, `SectionHeading`, and `MonospaceEvidence` styles initially; Tasks 2-8 extend it. Merge all five resource files in `App.xaml` in the tested order.

- [ ] **Step 5: Run the resource tests and the Desktop suite**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~DesktopPresentationContractTests|FullyQualifiedName~DesktopAssemblyTests|FullyQualifiedName~Theme'
```

Expected: PASS; no XAML compiler errors.

- [ ] **Step 6: Commit only the resource contract**

```powershell
git add -- tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs src/DevForge.Desktop/App.xaml src/DevForge.Desktop/Resources/Tokens.xaml src/DevForge.Desktop/Resources/Colors.Light.xaml src/DevForge.Desktop/Resources/Colors.Dark.xaml src/DevForge.Desktop/Resources/Typography.xaml src/DevForge.Desktop/Resources/Icons.xaml src/DevForge.Desktop/Resources/Components.xaml
git commit -m "feat(desktop): establish professional design tokens"
```

### Task 2: Build adaptive and interaction primitives

**Files:**

- Create: `src/DevForge.Desktop/Presentation/DoubleLessThanConverter.cs`
- Create: `src/DevForge.Desktop/Presentation/EqualityMultiConverter.cs`
- Create: `tests/DevForge.E2ETests/Desktop/PresentationValueConverterTests.cs`
- Modify: `src/DevForge.Desktop/Resources/Controls.xaml`
- Modify: `src/DevForge.Desktop/Resources/Components.xaml`

- [ ] **Step 1: Write failing converter tests**

```csharp
using System.Globalization;
using DevForge.Desktop.Presentation;

namespace DevForge.E2ETests.Desktop;

public sealed class PresentationValueConverterTests
{
    [Theory]
    [InlineData(959d, "1100", true)]
    [InlineData(1099.99d, "1100", true)]
    [InlineData(1100d, "1100", false)]
    [InlineData(1280d, "1100", false)]
    public void WidthBreakpointIsStrictAndCultureInvariant(
        double value,
        string parameter,
        bool expected)
    {
        var sut = new DoubleLessThanConverter();
        Assert.Equal(expected, sut.Convert(
            value, typeof(bool), parameter, CultureInfo.GetCultureInfo("vi-VN")));
    }

    [Fact]
    public void WidthBreakpointRejectsInvalidValues()
    {
        var sut = new DoubleLessThanConverter();
        Assert.Equal(false, sut.Convert(
            "960", typeof(bool), "invalid", CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("Dashboard", "Dashboard", true)]
    [InlineData("Dashboard", "Settings", false)]
    [InlineData(null, null, true)]
    [InlineData(null, "Settings", false)]
    public void RouteEqualityRequiresExactlyTwoEqualValues(
        object? first,
        object? second,
        bool expected)
    {
        var sut = new EqualityMultiConverter();
        Assert.Equal(expected, sut.Convert(
            [first, second], typeof(bool), null, CultureInfo.InvariantCulture));
    }
}
```

- [ ] **Step 2: Run converter tests and verify RED**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~PresentationValueConverterTests'
```

Expected: FAIL to compile because both converter types are absent.

- [ ] **Step 3: Implement the two presentation-only converters**

```csharp
using System.Globalization;
using System.Windows.Data;

namespace DevForge.Desktop.Presentation;

public sealed class DoubleLessThanConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value is double actual
            && parameter is string text
            && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var threshold)
            && actual < threshold;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;
}
```

```csharp
using System.Globalization;
using System.Windows.Data;

namespace DevForge.Desktop.Presentation;

public sealed class EqualityMultiConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && Equals(values[0], values[1]);

    public object[] ConvertBack(object? value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        targetTypes.Select(_ => Binding.DoNothing).ToArray();
}
```

- [ ] **Step 4: Replace raw control styling with complete interaction variants**

In `Controls.xaml`, define implicit Window/TextBlock/Button/TextBox/ComboBox/CheckBox/ListBox/ListView styles and explicit `Button.Primary`, `Button.Secondary`, `Button.Ghost`, `Button.Danger`, `Button.Icon`, and `NavigationButton` variants. Use semantic brushes only. Every button template must expose pointer-over, pressed, keyboard-focused, and disabled states. The shared button template has this exact structure:

```xml
<ControlTemplate x:Key="Button.Template" TargetType="ButtonBase">
    <Border x:Name="Chrome"
            Background="{TemplateBinding Background}"
            BorderBrush="{TemplateBinding BorderBrush}"
            BorderThickness="{TemplateBinding BorderThickness}"
            CornerRadius="8"
            Padding="{TemplateBinding Padding}">
        <ContentPresenter HorizontalAlignment="{TemplateBinding HorizontalContentAlignment}"
                          VerticalAlignment="{TemplateBinding VerticalContentAlignment}"
                          RecognizesAccessKey="True" />
    </Border>
    <ControlTemplate.Triggers>
        <Trigger Property="IsMouseOver" Value="True">
            <Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource Brush.BorderStrong}" />
        </Trigger>
        <Trigger Property="IsPressed" Value="True">
            <Setter TargetName="Chrome" Property="Opacity" Value="0.82" />
        </Trigger>
        <Trigger Property="IsKeyboardFocused" Value="True">
            <Setter TargetName="Chrome" Property="BorderBrush" Value="{DynamicResource Brush.Focus}" />
            <Setter TargetName="Chrome" Property="BorderThickness" Value="2" />
        </Trigger>
        <Trigger Property="IsEnabled" Value="False">
            <Setter TargetName="Chrome" Property="Background" Value="{DynamicResource Brush.DisabledSurface}" />
            <Setter Property="Foreground" Value="{DynamicResource Brush.DisabledText}" />
            <Setter TargetName="Chrome" Property="Opacity" Value="0.78" />
        </Trigger>
    </ControlTemplate.Triggers>
</ControlTemplate>
```

Use these exact shared style properties:

| Style | Background | Border | Foreground | Height / padding |
| --- | --- | --- | --- | --- |
| Implicit Button | `Brush.SurfaceRaised` | `Brush.Border` | `Brush.TextPrimary` | 40 / `14,8` |
| `Button.Primary` | `Brush.Accent` | `Brush.Accent` | White | 40 / `16,8` |
| `Button.Secondary` | `Brush.SurfaceRaised` | `Brush.BorderStrong` | `Brush.TextPrimary` | 40 / `14,8` |
| `Button.Ghost` | Transparent | Transparent | `Brush.TextSecondary` | 40 / `12,8` |
| `Button.Danger` | `Brush.ErrorSurface` | `Brush.Error` | `Brush.Error` | 40 / `14,8` |
| `Button.Icon` | Transparent | Transparent | `Brush.TextSecondary` | 40 x 40 / 0 |
| TextBox / ComboBox | `Brush.SurfaceRaised` | `Brush.Border` | `Brush.TextPrimary` | 40 / `10,7` |

The implicit Window uses `Brush.AppBackground`, `Brush.TextPrimary`, and `Font.Ui`. The implicit TextBlock uses `Brush.TextPrimary`. ListBox and ListView use transparent backgrounds and semantic borders. CheckBox retains native toggle semantics and receives semantic foreground, focus, hover, and disabled colors.

Keep `ToggleButton` navigation styling separate so it can expose a selected state. Do not replace native text-editing or selection behavior with a custom control.

- [ ] **Step 5: Add reusable component styles**

Extend `Components.xaml` with:

```xml
<Style x:Key="Card" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource Brush.Surface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Border}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="CornerRadius" Value="{StaticResource Card.CornerRadius}" />
    <Setter Property="Padding" Value="{StaticResource Card.Padding}" />
</Style>
<Style x:Key="Card.Raised" TargetType="Border" BasedOn="{StaticResource Card}">
    <Setter Property="Background" Value="{DynamicResource Brush.SurfaceRaised}" />
</Style>
<Style x:Key="StatusBadge" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource Brush.AccentSubtle}" />
    <Setter Property="CornerRadius" Value="999" />
    <Setter Property="Padding" Value="10,4" />
</Style>
<Style x:Key="Callout.Info" TargetType="Border" BasedOn="{StaticResource Card}">
    <Setter Property="Background" Value="{DynamicResource Brush.InfoSurface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Info}" />
</Style>
<Style x:Key="Callout.Warning" TargetType="Border" BasedOn="{StaticResource Card}">
    <Setter Property="Background" Value="{DynamicResource Brush.WarningSurface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Warning}" />
</Style>
<Style x:Key="Callout.Error" TargetType="Border" BasedOn="{StaticResource Card}">
    <Setter Property="Background" Value="{DynamicResource Brush.ErrorSurface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Error}" />
</Style>
<Style x:Key="Callout.Success" TargetType="Border" BasedOn="{StaticResource Card}">
    <Setter Property="Background" Value="{DynamicResource Brush.SuccessSurface}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Success}" />
</Style>
<Style x:Key="ActionBar" TargetType="Border">
    <Setter Property="Background" Value="{DynamicResource Brush.AppBackground}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Border}" />
    <Setter Property="BorderThickness" Value="0,1,0,0" />
    <Setter Property="Padding" Value="0,16,0,0" />
</Style>
<Style x:Key="ConsolePanel" TargetType="ListBox">
    <Setter Property="FontFamily" Value="{StaticResource Font.Mono}" />
    <Setter Property="FontSize" Value="13" />
    <Setter Property="Background" Value="{DynamicResource Brush.Navigation}" />
    <Setter Property="BorderBrush" Value="{DynamicResource Brush.Border}" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="12" />
    <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled" />
</Style>
```

Define the remaining shared presentation styles exactly as follows; views provide their content and state-specific triggers:

```xml
<Style x:Key="EmptyState" TargetType="Border" BasedOn="{StaticResource Card.Raised}">
    <Setter Property="Padding" Value="24" />
    <Setter Property="MinHeight" Value="128" />
</Style>
<Style x:Key="EmptyStateIcon" TargetType="TextBlock">
    <Setter Property="FontFamily" Value="{StaticResource Font.Icon}" />
    <Setter Property="FontSize" Value="24" />
    <Setter Property="Foreground" Value="{DynamicResource Brush.TextTertiary}" />
    <Setter Property="HorizontalAlignment" Value="Center" />
    <Setter Property="Margin" Value="0,0,0,10" />
</Style>
<Style x:Key="FormCard" TargetType="Border" BasedOn="{StaticResource Card}">
    <Setter Property="Margin" Value="0,0,0,16" />
    <Setter Property="MaxWidth" Value="880" />
</Style>
<Style x:Key="WorkflowStepper" TargetType="ItemsControl">
    <Setter Property="Margin" Value="0,0,0,24" />
    <Setter Property="HorizontalAlignment" Value="Stretch" />
</Style>
<Style x:Key="TimelineList" TargetType="ListBox">
    <Setter Property="Background" Value="Transparent" />
    <Setter Property="BorderThickness" Value="0" />
    <Setter Property="ScrollViewer.HorizontalScrollBarVisibility" Value="Disabled" />
    <Setter Property="VirtualizingStackPanel.IsVirtualizing" Value="True" />
</Style>
<Style x:Key="ToastCard" TargetType="Border" BasedOn="{StaticResource Card.Raised}">
    <Setter Property="MinWidth" Value="280" />
    <Setter Property="MaxWidth" Value="420" />
    <Setter Property="BorderThickness" Value="1" />
    <Setter Property="Padding" Value="14" />
</Style>
```

They remain pure presentation styles with no event handlers.

- [ ] **Step 6: Run converter and presentation tests**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~PresentationValueConverterTests|FullyQualifiedName~DesktopPresentationContractTests'
```

Expected: PASS.

- [ ] **Step 7: Commit primitives only**

```powershell
git add -- src/DevForge.Desktop/Presentation/DoubleLessThanConverter.cs src/DevForge.Desktop/Presentation/EqualityMultiConverter.cs src/DevForge.Desktop/Resources/Controls.xaml src/DevForge.Desktop/Resources/Components.xaml tests/DevForge.E2ETests/Desktop/PresentationValueConverterTests.cs
git commit -m "feat(desktop): add adaptive presentation primitives"
```

### Task 3: Redesign the application shell

**Files:**

- Modify: `tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs`
- Modify: `src/DevForge.Desktop/MainWindow.xaml`

- [ ] **Step 1: Add failing shell composition assertions**

Add this test to `DesktopPresentationContractTests`:

```csharp
[Fact]
public void ShellUsesAdaptiveNavigationSafeModeAndSemanticToasts()
{
    var xaml = Read("src/DevForge.Desktop/MainWindow.xaml");
    Assert.Contains("EqualityMultiConverter", xaml, StringComparison.Ordinal);
    Assert.Contains("DoubleLessThanConverter", xaml, StringComparison.Ordinal);
    Assert.Contains("NavigationButton", xaml, StringComparison.Ordinal);
    Assert.Contains("Safe mode", xaml, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("NotificationSeverity.Warning", xaml, StringComparison.Ordinal);
    Assert.Contains("NotificationSeverity.Error", xaml, StringComparison.Ordinal);
    Assert.Contains("AutomationProperties.HelpText", xaml, StringComparison.Ordinal);
}
```

- [ ] **Step 2: Run the shell contract and verify RED**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~ShellUsesAdaptiveNavigationSafeModeAndSemanticToasts'
```

Expected: FAIL because the baseline shell has no adaptive converters or semantic toast triggers.

- [ ] **Step 3: Replace the shell layout while preserving bindings**

Add presentation and notification namespaces, converter instances, and keep every existing ViewModel DataTemplate. Use this shell structure:

```xml
<Grid x:Name="ShellRoot" Background="{DynamicResource Brush.AppBackground}">
    <Grid.Resources>
        <presentation:DoubleLessThanConverter x:Key="WidthLessThan" />
        <presentation:EqualityMultiConverter x:Key="ValuesEqual" />
    </Grid.Resources>
    <Grid.ColumnDefinitions>
        <ColumnDefinition>
            <ColumnDefinition.Style>
                <Style TargetType="ColumnDefinition">
                    <Setter Property="Width" Value="248" />
                    <Style.Triggers>
                        <DataTrigger Value="True">
                            <DataTrigger.Binding>
                                <Binding ElementName="ShellRoot" Path="ActualWidth"
                                         Converter="{StaticResource WidthLessThan}"
                                         ConverterParameter="1100" />
                            </DataTrigger.Binding>
                            <Setter Property="Width" Value="80" />
                        </DataTrigger>
                    </Style.Triggers>
                </Style>
            </ColumnDefinition.Style>
        </ColumnDefinition>
        <ColumnDefinition Width="*" />
    </Grid.ColumnDefinitions>
    <Border Grid.Column="0" Background="{DynamicResource Brush.Navigation}"
            BorderBrush="{DynamicResource Brush.Border}" BorderThickness="0,0,1,0">
        <DockPanel Margin="14,20">
            <StackPanel DockPanel.Dock="Top" Margin="6,0,6,26">
                <Border Width="36" Height="36" HorizontalAlignment="Left"
                        Background="{DynamicResource Brush.Accent}" CornerRadius="10">
                    <TextBlock Text="DF" Foreground="White" FontWeight="Bold"
                               HorizontalAlignment="Center" VerticalAlignment="Center" />
                </Border>
                <StackPanel Margin="0,10,0,0">
                    <StackPanel.Style>
                        <Style TargetType="StackPanel">
                            <Style.Triggers>
                                <DataTrigger Value="True">
                                    <DataTrigger.Binding>
                                        <Binding ElementName="ShellRoot" Path="ActualWidth"
                                                 Converter="{StaticResource WidthLessThan}"
                                                 ConverterParameter="1100" />
                                    </DataTrigger.Binding>
                                    <Setter Property="Visibility" Value="Collapsed" />
                                </DataTrigger>
                            </Style.Triggers>
                        </Style>
                    </StackPanel.Style>
                    <TextBlock Text="DEVFORGE" FontWeight="Bold" FontSize="16" />
                    <TextBlock Text="Leader Edition" Style="{StaticResource Text.Caption}" />
                </StackPanel>
            </StackPanel>
            <ItemsControl ItemsSource="{Binding Routes}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <ToggleButton Style="{StaticResource NavigationButton}"
                                      Command="{Binding DataContext.NavigateCommand, RelativeSource={RelativeSource AncestorType=Window}}"
                                      CommandParameter="{Binding Route}"
                                      IsEnabled="{Binding IsEnabled}"
                                      AutomationProperties.Name="{Binding Label}"
                                      AutomationProperties.HelpText="{Binding DisabledReason}">
                            <ToggleButton.IsChecked>
                                <MultiBinding Converter="{StaticResource ValuesEqual}" Mode="OneWay">
                                    <Binding Path="Route" />
                                    <Binding Path="DataContext.CurrentRoute" RelativeSource="{RelativeSource AncestorType=Window}" />
                                </MultiBinding>
                            </ToggleButton.IsChecked>
                            <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="28" /><ColumnDefinition Width="*" /></Grid.ColumnDefinitions>
                                <TextBlock FontFamily="{StaticResource Font.Icon}" FontSize="16" VerticalAlignment="Center">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock">
                                            <Setter Property="Text" Value="{StaticResource Icon.Dashboard}" />
                                            <Style.Triggers>
                                                <DataTrigger Binding="{Binding IconKey}" Value="Add"><Setter Property="Text" Value="{StaticResource Icon.Create}" /></DataTrigger>
                                                <DataTrigger Binding="{Binding IconKey}" Value="Folder"><Setter Property="Text" Value="{StaticResource Icon.History}" /></DataTrigger>
                                                <DataTrigger Binding="{Binding IconKey}" Value="Catalog"><Setter Property="Text" Value="{StaticResource Icon.Catalog}" /></DataTrigger>
                                                <DataTrigger Binding="{Binding IconKey}" Value="Health"><Setter Property="Text" Value="{StaticResource Icon.Health}" /></DataTrigger>
                                                <DataTrigger Binding="{Binding IconKey}" Value="Settings"><Setter Property="Text" Value="{StaticResource Icon.Settings}" /></DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                                <TextBlock Grid.Column="1" Text="{Binding Label}" VerticalAlignment="Center">
                                    <TextBlock.Style>
                                        <Style TargetType="TextBlock">
                                            <Style.Triggers>
                                                <DataTrigger Value="True">
                                                    <DataTrigger.Binding>
                                                        <Binding ElementName="ShellRoot" Path="ActualWidth" Converter="{StaticResource WidthLessThan}" ConverterParameter="1100" />
                                                    </DataTrigger.Binding>
                                                    <Setter Property="Visibility" Value="Collapsed" />
                                                </DataTrigger>
                                            </Style.Triggers>
                                        </Style>
                                    </TextBlock.Style>
                                </TextBlock>
                            </Grid>
                        </ToggleButton>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </DockPanel>
    </Border>
    <Grid Grid.Column="1">
        <Grid.RowDefinitions><RowDefinition Height="Auto" /><RowDefinition Height="*" /></Grid.RowDefinitions>
        <Border Grid.Row="0" Margin="20,16,20,0">
            <Border.Style>
                <Style TargetType="Border" BasedOn="{StaticResource Callout.Warning}">
                    <Setter Property="Visibility" Value="Collapsed" />
                    <Style.Triggers><DataTrigger Binding="{Binding IsSafeMode}" Value="True"><Setter Property="Visibility" Value="Visible" /></DataTrigger></Style.Triggers>
                </Style>
            </Border.Style>
            <StackPanel Orientation="Horizontal">
                <TextBlock Text="{StaticResource Icon.Warning}" FontFamily="{StaticResource Font.Icon}" Foreground="{DynamicResource Brush.Warning}" FontSize="18" Margin="0,0,12,0" />
                <StackPanel><TextBlock Text="Safe mode" FontWeight="SemiBold" /><TextBlock Text="{Binding SafeModeMessage}" TextWrapping="Wrap" /></StackPanel>
            </StackPanel>
        </Border>
        <ContentControl Grid.Row="1" Content="{Binding CurrentPage}" Focusable="False" />
        <ItemsControl Grid.RowSpan="2" HorizontalAlignment="Right" VerticalAlignment="Bottom" Margin="20" ItemsSource="{Binding Notifications.Items}">
            <ItemsControl.ItemTemplate>
                <DataTemplate>
                    <Border Margin="0,6,0,0">
                        <Border.Style>
                            <Style TargetType="Border" BasedOn="{StaticResource ToastCard}">
                                <Style.Triggers>
                                    <DataTrigger Binding="{Binding Severity}" Value="{x:Static notifications:NotificationSeverity.Warning}"><Setter Property="BorderBrush" Value="{DynamicResource Brush.Warning}" /></DataTrigger>
                                    <DataTrigger Binding="{Binding Severity}" Value="{x:Static notifications:NotificationSeverity.Error}"><Setter Property="BorderBrush" Value="{DynamicResource Brush.Error}" /></DataTrigger>
                                </Style.Triggers>
                            </Style>
                        </Border.Style>
                        <TextBlock Text="{Binding Message}" MaxWidth="380" TextWrapping="Wrap" />
                    </Border>
                </DataTemplate>
            </ItemsControl.ItemTemplate>
        </ItemsControl>
    </Grid>
</Grid>
```

Set `UseLayoutRounding="True"`, `SnapsToDevicePixels="True"`, retain `Width="1280"`, `Height="800"`, `MinWidth="960"`, `MinHeight="640"`, and retain all existing DataTemplates.

- [ ] **Step 4: Run shell, navigation, behavior, and architecture tests**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~ShellUsesAdaptiveNavigationSafeModeAndSemanticToasts|FullyQualifiedName~NavigationServiceTests|FullyQualifiedName~DesktopBehaviorMatrixTests|FullyQualifiedName~DesktopArchitectureTests'
```

Expected: PASS.

- [ ] **Step 5: Commit the shell**

```powershell
git add -- src/DevForge.Desktop/MainWindow.xaml tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs
git commit -m "feat(desktop): redesign the adaptive application shell"
```

### Task 4: Redesign Dashboard, Blueprint Catalog, and Run History

**Files:**

- Modify: `tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs`
- Modify: `src/DevForge.Desktop/Dashboard/DashboardView.xaml`
- Modify: `src/DevForge.Desktop/BlueprintCatalog/BlueprintCatalogView.xaml`
- Modify: `src/DevForge.Desktop/RunHistory/RunHistoryView.xaml`

- [ ] **Step 1: Add failing view-composition contracts**

```csharp
[Theory]
[InlineData("src/DevForge.Desktop/Dashboard/DashboardView.xaml", "Create Project", "EmptyState", "RefreshCommand")]
[InlineData("src/DevForge.Desktop/BlueprintCatalog/BlueprintCatalogView.xaml", "TrustLabel", "EmptyState", "CreateCommand")]
[InlineData("src/DevForge.Desktop/RunHistory/RunHistoryView.xaml", "StatusBadge", "EmptyState", "RetryPublishCommand")]
public void DataPagesExposeHierarchyEmptyStateAndBackedActions(
    string path,
    string hierarchyMarker,
    string emptyStateMarker,
    string commandMarker)
{
    var xaml = Read(path);
    Assert.Contains(hierarchyMarker, xaml, StringComparison.Ordinal);
    Assert.Contains(emptyStateMarker, xaml, StringComparison.Ordinal);
    Assert.Contains(commandMarker, xaml, StringComparison.Ordinal);
    Assert.DoesNotContain("#FFD13438", xaml, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the contracts and verify RED**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~DataPagesExposeHierarchyEmptyStateAndBackedActions'
```

Expected: FAIL because the baseline views do not use the new component and empty-state markers.

- [ ] **Step 3: Recompose Dashboard without changing bindings**

Use a Grid with a header row and scrollable content. The header keeps `CreateProjectCommand` primary and exposes existing `RefreshCommand` as a secondary icon action. Use a two-column `UniformGrid` for the four cards. Each empty state binds the existing booleans:

```xml
<Border Style="{StaticResource Card}" Margin="0,0,8,16">
    <Grid>
        <StackPanel>
            <TextBlock Style="{StaticResource Text.CardTitle}" Text="Recent projects" />
            <TextBlock Style="{StaticResource Text.Caption}" Margin="0,4,0,16" Text="Return to work you generated with DevForge." />
            <Border>
                <Border.Style>
                    <Style TargetType="Border" BasedOn="{StaticResource EmptyState}">
                        <Setter Property="Visibility" Value="Collapsed" />
                        <Style.Triggers><DataTrigger Binding="{Binding HasNoRecentProjects}" Value="True"><Setter Property="Visibility" Value="Visible" /></DataTrigger></Style.Triggers>
                    </Style>
                </Border.Style>
                <StackPanel HorizontalAlignment="Center">
                    <TextBlock Style="{StaticResource EmptyStateIcon}" Text="{StaticResource Icon.Folder}" />
                    <TextBlock Text="No recent projects" FontWeight="SemiBold" HorizontalAlignment="Center" />
                    <TextBlock Style="{StaticResource Text.Caption}" Text="Created projects will appear here." HorizontalAlignment="Center" />
                </StackPanel>
            </Border>
            <ItemsControl ItemsSource="{Binding Snapshot.RecentProjects}">
                <ItemsControl.ItemTemplate>
                    <DataTemplate>
                        <Grid Margin="0,8"><Grid.ColumnDefinitions><ColumnDefinition /><ColumnDefinition Width="Auto" /></Grid.ColumnDefinitions>
                            <StackPanel><TextBlock Text="{Binding DisplayName}" FontWeight="SemiBold" /><TextBlock Text="{Binding ProjectPath}" ToolTip="{Binding ProjectPath}" Style="{StaticResource Text.Caption}" /></StackPanel>
                            <Border Grid.Column="1" Style="{StaticResource StatusBadge}"><TextBlock Text="{Binding LocationStatus}" /></Border>
                        </Grid>
                    </DataTemplate>
                </ItemsControl.ItemTemplate>
            </ItemsControl>
        </StackPanel>
    </Grid>
</Border>
```

Build the remaining cards with this exact binding map:

| Card | Empty condition | Content source | Backed action |
| --- | --- | --- | --- |
| Action Needed | `HasNoActionNeededRuns` | `Snapshot.ActionNeededRuns` with `RunId` and `Status` tooltip | None |
| Saved Presets | `HasNoSavedPresets` | `Snapshot.SavedPresets`, `DisplayMemberPath=Name` | None |
| Environment Health | no fabricated empty flag | `Snapshot.EnvironmentHealth.ScannedAt` and `Snapshot.EnvironmentHealth.Tools.Length` | `OpenEnvironmentDoctorCommand` |

Each empty branch uses `EmptyStateIcon`, a short title, and one explanatory caption. Do not synthesize counts or metadata absent from `DashboardSnapshot`.

- [ ] **Step 4: Recompose Blueprint Catalog as responsive cards**

Use an `ItemsControl` with a two-column `UniformGrid`, a loading overlay bound to `IsBusy`, and an empty-state DataTrigger on `Items.Length` equal to zero. Each card contains identifier, version, trust badge, issue callout, and `CreateCommand`:

```xml
<Button Style="{StaticResource Button.Primary}" Content="Use blueprint"
        IsEnabled="{Binding CanCreate}"
        Command="{Binding DataContext.CreateCommand, RelativeSource={RelativeSource AncestorType=ItemsControl}}"
        CommandParameter="{Binding}"
        AutomationProperties.Name="Create project from blueprint" />
```

Retain `RefreshCommand`; do not add search or filter controls.

- [ ] **Step 5: Recompose Run History as recovery-aware cards**

Use a virtualized `ListBox`, status badge with glyph plus label, trimmed run ID, error code callout, and a wrapping action row. Keep these exact commands and eligibility bindings:

```xml
<Button Content="Resume" Style="{StaticResource Button.Secondary}"
        IsEnabled="{Binding CanResume}"
        Command="{Binding DataContext.ResumeCommand, RelativeSource={RelativeSource AncestorType=ListBox}}"
        CommandParameter="{Binding}" />
<Button Content="Retry" Style="{StaticResource Button.Secondary}"
        IsEnabled="{Binding CanRetry}"
        Command="{Binding DataContext.RetryCommand, RelativeSource={RelativeSource AncestorType=ListBox}}"
        CommandParameter="{Binding}" />
<Button Content="Cleanup" Style="{StaticResource Button.Ghost}"
        IsEnabled="{Binding CanCleanup}"
        Command="{Binding DataContext.CleanupCommand, RelativeSource={RelativeSource AncestorType=ListBox}}"
        CommandParameter="{Binding}" />
<Button Content="Retry publish" Style="{StaticResource Button.Primary}"
        IsEnabled="{Binding CanRetryPublish}"
        Command="{Binding DataContext.RetryPublishCommand, RelativeSource={RelativeSource AncestorType=ListBox}}"
        CommandParameter="{Binding}"
        AutomationProperties.Name="Retry Publish" />
```

Show a purposeful `EmptyState` when `Items.Length` is zero. Do not display timestamps or blueprint metadata because `RunHistoryItemViewModel` does not expose them.

- [ ] **Step 6: Run page contracts and existing behavior tests**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~DataPagesExposeHierarchyEmptyStateAndBackedActions|FullyQualifiedName~DashboardViewModelTests|FullyQualifiedName~BlueprintCatalogViewModelTests|FullyQualifiedName~RunHistoryViewModelTests'
```

Expected: PASS.

- [ ] **Step 7: Commit the three data pages**

```powershell
git add -- src/DevForge.Desktop/Dashboard/DashboardView.xaml src/DevForge.Desktop/BlueprintCatalog/BlueprintCatalogView.xaml src/DevForge.Desktop/RunHistory/RunHistoryView.xaml tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs
git commit -m "feat(desktop): redesign dashboard catalog and history"
```

### Task 5: Redesign Environment Doctor and Settings

**Files:**

- Modify: `tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs`
- Modify: `src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorView.xaml`
- Modify: `src/DevForge.Desktop/Settings/SettingsView.xaml`

- [ ] **Step 1: Add failing diagnostics/settings contracts**

```csharp
[Fact]
public void DoctorAndSettingsUseSemanticStateAndStickyActions()
{
    var doctor = Read("src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorView.xaml");
    Assert.Contains("Callout.Warning", doctor, StringComparison.Ordinal);
    Assert.Contains("Scan failed; cached results shown", doctor, StringComparison.Ordinal);
    Assert.Contains("StatusBadge", doctor, StringComparison.Ordinal);
    Assert.Contains("CopyDiagnosticsCommand", doctor, StringComparison.Ordinal);

    var settings = Read("src/DevForge.Desktop/Settings/SettingsView.xaml");
    Assert.Contains("Getting started", settings, StringComparison.Ordinal);
    Assert.Contains("Project defaults", settings, StringComparison.Ordinal);
    Assert.Contains("Appearance", settings, StringComparison.Ordinal);
    Assert.Contains("ActionBar", settings, StringComparison.Ordinal);
    Assert.Contains("ValidationMessages", settings, StringComparison.Ordinal);
    Assert.DoesNotContain("#FFD13438", settings, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the contract and verify RED**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~DoctorAndSettingsUseSemanticStateAndStickyActions'
```

Expected: FAIL on the new semantic component markers.

- [ ] **Step 3: Redesign Environment Doctor**

Keep the existing header bindings and command hierarchy. Replace the dark raw status block with separate stale and failed-cache warning callouts:

```xml
<Border Grid.Row="1" Style="{StaticResource Callout.Warning}" Margin="0,0,0,16"
        Visibility="{Binding IsStale, Converter={StaticResource BooleanToVisibilityConverter}}">
    <StackPanel Orientation="Horizontal">
        <TextBlock Text="{StaticResource Icon.Warning}" FontFamily="{StaticResource Font.Icon}" Foreground="{DynamicResource Brush.Warning}" FontSize="18" Margin="0,0,12,0" />
        <StackPanel><TextBlock Text="Cached results are stale" FontWeight="SemiBold" /><TextBlock Text="Rescan to verify the current toolchain before creating a project." TextWrapping="Wrap" /></StackPanel>
    </StackPanel>
</Border>
<Border Grid.Row="2" Style="{StaticResource Callout.Error}" Margin="0,0,0,16"
        Visibility="{Binding ScanFailed, Converter={StaticResource BooleanToVisibilityConverter}}">
    <StackPanel><TextBlock Text="Scan failed; cached results shown" FontWeight="SemiBold" /><TextBlock Text="The last safe snapshot remains visible below." TextWrapping="Wrap" /></StackPanel>
</Border>
```

Use a raised card around the virtualized GridView. Style tool/status cells with text+glyph evidence, allow Compatibility and Remediation to wrap in cell DataTemplates, and keep horizontal scrolling available. Bind the busy overlay to `IsBusy`; do not hide cached data during a rescan.

- [ ] **Step 4: Redesign Settings without changing save semantics**

Use a root Grid with scrollable content and a bottom `ActionBar`. Compose three cards:

1. Getting started — four read-only readiness rows bound to `HasProjectRoot`, `HasIdeSelection`, `HasTeamProfile`, and `HasLanguage` with check glyph plus text.
2. Project defaults — existing root, IDE, and team-profile bindings.
3. Appearance — existing language and theme bindings plus onboarding completion.

Show validation in an error callout:

```xml
<ItemsControl ItemsSource="{Binding ValidationMessages}">
    <ItemsControl.ItemTemplate>
        <DataTemplate>
            <Border Style="{StaticResource Callout.Error}" Margin="0,0,0,8">
                <StackPanel Orientation="Horizontal">
                    <TextBlock Text="{StaticResource Icon.Error}" FontFamily="{StaticResource Font.Icon}" Foreground="{DynamicResource Brush.Error}" Margin="0,0,10,0" />
                    <TextBlock Text="{Binding}" TextWrapping="Wrap" />
                </StackPanel>
            </Border>
        </DataTemplate>
    </ItemsControl.ItemTemplate>
</ItemsControl>
```

The bottom bar retains Reset/Save, binds their existing commands, gives Save `Button.Primary`, and exposes busy/read-only disabled states through command availability.

- [ ] **Step 5: Run focused ViewModel and presentation tests**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~DoctorAndSettingsUseSemanticStateAndStickyActions|FullyQualifiedName~EnvironmentDoctorViewModelTests|FullyQualifiedName~SettingsViewModelTests|FullyQualifiedName~DesktopSettingsServiceTests'
```

Expected: PASS.

- [ ] **Step 6: Commit Doctor and Settings**

```powershell
git add -- src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorView.xaml src/DevForge.Desktop/Settings/SettingsView.xaml tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs
git commit -m "feat(desktop): refine diagnostics and settings experience"
```

### Task 6: Redesign Configure and Review Plan

**Files:**

- Modify: `tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs`
- Modify: `src/DevForge.Desktop/CreateProject/CreateProjectView.xaml`

- [ ] **Step 1: Add a failing workflow-presentation contract**

```csharp
[Fact]
public void CreateProjectPresentsAllFourWorkflowStagesAndReviewEvidence()
{
    var xaml = Read("src/DevForge.Desktop/CreateProject/CreateProjectView.xaml");
    Assert.Contains("Configure", xaml, StringComparison.Ordinal);
    Assert.Contains("Review", xaml, StringComparison.Ordinal);
    Assert.Contains("Execute", xaml, StringComparison.Ordinal);
    Assert.Contains("Complete", xaml, StringComparison.Ordinal);
    Assert.Contains("WorkflowStepper", xaml, StringComparison.Ordinal);
    Assert.Contains("FormCard", xaml, StringComparison.Ordinal);
    Assert.Contains("ActionBar", xaml, StringComparison.Ordinal);
    Assert.Contains("Text.Mono", xaml, StringComparison.Ordinal);
    Assert.Contains("CreateAndValidateCommand", xaml, StringComparison.Ordinal);
    Assert.Contains("BackToConfigureCommand", xaml, StringComparison.Ordinal);
    Assert.DoesNotContain("#FFD13438", xaml, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the contract and verify RED**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~CreateProjectPresentsAllFourWorkflowStagesAndReviewEvidence'
```

Expected: FAIL because the baseline view has no stepper, form-card, or action-bar composition.

- [ ] **Step 3: Preserve dynamic input templates and stage routing exactly**

Keep all four existing DataTemplates and bindings:

| Input kind | Backing binding |
| --- | --- |
| Text | `TextValue`, `UpdateSourceTrigger=PropertyChanged` |
| Choice | `AllowedValues` + `TextValue` |
| Boolean | `BooleanValue` |
| WholeNumber | `WholeNumberValue`, `UpdateSourceTrigger=PropertyChanged` |

Keep the outer stage triggers that hide Configure/Review during Execute, LocalReady, PublishPending, and Completed. Keep both `ContentControl` stage mappings to `ExecutionCenterView` and `LocalReadyView` unchanged.

- [ ] **Step 4: Compose Configure with workflow and form cards**

Add a four-item stepper at the top. Use DataTriggers on `Stage` to style the current/completed stage; each step shows number, label, and connecting line. Compose Project, Blueprint Options, Git & GitHub, and Open After Generation as `FormCard` borders. Every field follows label/control/hint/error order. Example Project field:

```xml
<StackPanel Margin="0,0,0,16">
    <TextBlock Style="{StaticResource Text.Label}" Text="Project name" />
    <TextBox Margin="0,6,0,0"
             Text="{Binding Name, UpdateSourceTrigger=PropertyChanged}"
             AutomationProperties.Name="Project name" />
    <TextBlock Style="{StaticResource Text.Caption}" Margin="0,5,0,0"
               Text="Used for the project identity and default output folder." />
</StackPanel>
```

Keep every existing editable binding and enablement binding: `Name`, `RootPath`, `OutputFolder`, `Blueprints`, `SelectedBlueprint`, `Inputs`, `InitializeRepository`, `CanEdit`, `BranchPolicyChoices`, `BranchPolicy`, `CanConfigureGit`, `PublishToGitHub`, `IsPrivate`, `CanConfigureGitHub`, `GitHubAccount`, `GitHubRepository`, `IdeChoices`, and `IdeId`.

Render `ValidationIssues` as semantic error callouts. Put `ReviewPlanCommand` in `ActionBar` as the only primary action while `PlanPreview` is null.

- [ ] **Step 5: Compose Review Plan as progressive disclosure**

When `PlanPreview` is non-null, show a review header card with `BlueprintLabel`, `TrustLabel`, `PlanHash`, `GitSummary`, `GitHubSummary`, and `RepositoryVisibility`. Use `Text.Mono` for the plan hash.

Replace consecutive raw ListBoxes with labeled `Expander` sections. Keep the exact sources and member templates:

```xml
<Expander Header="Execution steps" IsExpanded="True" Margin="0,0,0,10">
    <ListBox ItemsSource="{Binding Steps}" VirtualizingStackPanel.IsVirtualizing="True">
        <ListBox.ItemTemplate>
            <DataTemplate>
                <Border Style="{StaticResource Card.Raised}" Margin="0,0,0,8">
                    <StackPanel>
                        <TextBlock Text="{Binding Id}" FontWeight="SemiBold" />
                        <TextBlock Style="{StaticResource Text.Caption}" Text="{Binding HandlerId}" />
                        <TextBlock Style="{StaticResource Text.Mono}" Margin="0,6,0,0" Text="{Binding ProcessPreview}" TextWrapping="Wrap" />
                    </StackPanel>
                </Border>
            </DataTemplate>
        </ListBox.ItemTemplate>
    </ListBox>
</Expander>
```

Add equivalent bounded sections for Artifacts, Dependencies, Tools, Validators, Effective Inputs, Features, and Warnings. Put `BackToConfigureCommand` secondary and `CreateAndValidateCommand` primary in the bottom `ActionBar`.

- [ ] **Step 6: Run creation, plan-preview, behavior, and architecture tests**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~CreateProjectPresentsAllFourWorkflowStagesAndReviewEvidence|FullyQualifiedName~CreateProjectViewModelTests|FullyQualifiedName~PlanPreviewViewModelTests|FullyQualifiedName~DesktopBehaviorMatrixTests|FullyQualifiedName~DesktopArchitectureTests'
```

Expected: PASS.

- [ ] **Step 7: Commit the complete Create/Review workflow**

```powershell
git add -- src/DevForge.Desktop/CreateProject/CreateProjectView.xaml tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs
git commit -m "feat(desktop): redesign project creation and review"
```

### Task 7: Redesign Execution Center and completion states

**Files:**

- Modify: `tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs`
- Modify: `src/DevForge.Desktop/Execution/ExecutionCenterView.xaml`
- Modify: `src/DevForge.Desktop/Execution/LocalReadyView.xaml`

- [ ] **Step 1: Add failing execution/completion contracts**

```csharp
[Fact]
public void ExecutionAndCompletionSeparateProgressRecoveryAndPublicationState()
{
    var execution = Read("src/DevForge.Desktop/Execution/ExecutionCenterView.xaml");
    Assert.Contains("TimelineList", execution, StringComparison.Ordinal);
    Assert.Contains("ConsolePanel", execution, StringComparison.Ordinal);
    Assert.Contains("CancelCommand", execution, StringComparison.Ordinal);
    Assert.Contains("ResumeCommand", execution, StringComparison.Ordinal);
    Assert.Contains("CleanupCommand", execution, StringComparison.Ordinal);

    var completion = Read("src/DevForge.Desktop/Execution/LocalReadyView.xaml");
    Assert.Contains("validated local project remains safe", completion, StringComparison.OrdinalIgnoreCase);
    Assert.Contains("Callout.Success", completion, StringComparison.Ordinal);
    Assert.Contains("Callout.Warning", completion, StringComparison.Ordinal);
    Assert.Contains("RetryPublishCommand", completion, StringComparison.Ordinal);
    Assert.Contains("OpenIdeCommand", completion, StringComparison.Ordinal);
    Assert.DoesNotContain("#FFD13438", completion, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 2: Run the contract and verify RED**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~ExecutionAndCompletionSeparateProgressRecoveryAndPublicationState'
```

Expected: FAIL because the baseline screens do not use semantic timeline/console/callout components.

- [ ] **Step 3: Recompose Execution Center**

Use a root Grid with header, two-pane content, and action bar. The header shows `Status.Glyph` plus `Status.Label`, keeps `CancelCommand`, and uses an indeterminate accent progress bar only while the command/state indicates active execution. Do not create a fake percentage.

Use `TimelineList` for `Steps`; each item shows `StatusGlyph`, `DisplayName`, `StatusLabel`, `ErrorCode`, and `Remediation`. Use semantic error styling when `ErrorCode` is non-null. Use `ConsolePanel` for `ProgressLines`, with `TextWrapping="Wrap"` and virtualization retained.

Keep the existing commands and future-disabled actions exactly:

```xml
<Border Grid.Row="2" Style="{StaticResource ActionBar}">
    <WrapPanel HorizontalAlignment="Right">
        <Button Content="Resume" Style="{StaticResource Button.Secondary}" Command="{Binding ResumeCommand}" />
        <Button Content="Retry" Style="{StaticResource Button.Secondary}" Command="{Binding RetryCommand}" Margin="8,0,0,0" />
        <Button Content="Cleanup" Style="{StaticResource Button.Ghost}" Command="{Binding CleanupCommand}" Margin="8,0,0,0" />
        <Button Content="Open staging" Style="{StaticResource Button.Ghost}" IsEnabled="False" Margin="8,0,0,0" AutomationProperties.HelpText="Available in a future release" />
        <Button Content="Support bundle" Style="{StaticResource Button.Ghost}" IsEnabled="False" Margin="8,0,0,0" AutomationProperties.HelpText="Available in a future release" />
    </WrapPanel>
</Border>
```

- [ ] **Step 4: Recompose Local Ready, Completed, and Publish Pending**

Lead with a success card that always protects the local-result message:

```xml
<Border Style="{StaticResource Callout.Success}" Margin="0,0,0,18">
    <Grid><Grid.ColumnDefinitions><ColumnDefinition Width="Auto" /><ColumnDefinition Width="*" /><ColumnDefinition Width="Auto" /></Grid.ColumnDefinitions>
        <TextBlock Text="{StaticResource Icon.Success}" FontFamily="{StaticResource Font.Icon}" Foreground="{DynamicResource Brush.Success}" FontSize="28" Margin="0,0,16,0" />
        <StackPanel Grid.Column="1"><TextBlock Style="{StaticResource Text.PageTitle}" Text="{Binding StatusLabel}" /><TextBlock Text="The validated local project remains safe and available throughout completion." TextWrapping="Wrap" /></StackPanel>
        <Button Grid.Column="2" Content="Open IDE" Style="{StaticResource Button.Primary}" Command="{Binding OpenIdeCommand}" Margin="20,0,0,0" AutomationProperties.Name="Open IDE" />
    </Grid>
</Border>
```

Show target, blueprint, plan hash, elapsed, finalization, and report state in a summary card. Use `Text.Mono` for plan hash and report references. Compose Evidence, Warnings, and Publication as separate cards.

Show the publication warning card only when `PublicationErrorMessage` is non-null; include the exact recoverability statement, error, remediation, and `RetryPublishCommand`. Show IDE error as an independent error callout so it never replaces local success. Retain all existing bindings: `ReportReferences`, `Evidence`, `Warnings`, `InitialCommitId`, `Branches`, `RepositoryUrl`, `PublicationReceiptReferences`, `PublicationErrorMessage`, `PublicationRemediation`, and `IdeErrorMessage`.

- [ ] **Step 5: Run execution, completion, publication, and behavior tests**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~ExecutionAndCompletionSeparateProgressRecoveryAndPublicationState|FullyQualifiedName~ExecutionCenterViewModelTests|FullyQualifiedName~LocalReadyViewModelTests|FullyQualifiedName~ProjectPublicationWorkflowDesktopTests|FullyQualifiedName~DesktopBehaviorMatrixTests'
```

Expected: PASS.

- [ ] **Step 6: Commit execution and completion**

```powershell
git add -- src/DevForge.Desktop/Execution/ExecutionCenterView.xaml src/DevForge.Desktop/Execution/LocalReadyView.xaml tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs
git commit -m "feat(desktop): clarify execution and completion states"
```

### Task 8: Add compiled-XAML, theme, and minimum-size smoke coverage

**Files:**

- Create: `tests/DevForge.E2ETests/Desktop/DesktopXamlSmokeTests.cs`
- Modify: `tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs`
- Modify: presentation XAML files only if the smoke test reveals defects

- [ ] **Step 1: Write the STA smoke test before final visual fixes**

```csharp
using System.Runtime.ExceptionServices;
using System.Windows;
using DevForge.Desktop.BlueprintCatalog;
using DevForge.Desktop.CreateProject;
using DevForge.Desktop.Dashboard;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Execution;
using DevForge.Desktop.RunHistory;
using DevForge.Desktop.Settings;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopXamlSmokeTests
{
    [Fact]
    public void EveryDesktopViewLoadsWithBothThemesAtTheSupportedMinimum()
    {
        RunSta(() =>
        {
            var application = new Application
            {
                ShutdownMode = ShutdownMode.OnExplicitShutdown,
            };
            try
            {
                foreach (var theme in new[] { "Light", "Dark" })
                {
                    application.Resources.MergedDictionaries.Clear();
                    Merge(application, $"Resources/Colors.{theme}.xaml");
                    Merge(application, "Resources/Tokens.xaml");
                    Merge(application, "Resources/Typography.xaml");
                    Merge(application, "Resources/Icons.xaml");
                    Merge(application, "Resources/Controls.xaml");
                    Merge(application, "Resources/Components.xaml");

                    FrameworkElement[] views =
                    [
                        new DashboardView(), new CreateProjectView(), new RunHistoryView(),
                        new BlueprintCatalogView(), new EnvironmentDoctorView(), new SettingsView(),
                        new ExecutionCenterView(), new LocalReadyView(),
                    ];
                    Assert.All(views, view =>
                    {
                        view.Measure(new Size(880, 640));
                        view.Arrange(new Rect(0, 0, 880, 640));
                        Assert.True(view.IsMeasureValid, view.GetType().Name);
                        Assert.True(view.IsArrangeValid, view.GetType().Name);
                    });

                    var window = new DevForge.Desktop.MainWindow();
                    Assert.Equal(960, window.MinWidth);
                    Assert.Equal(640, window.MinHeight);
                    window.Close();
                }
            }
            finally
            {
                application.Shutdown();
            }
        });
    }

    private static void Merge(Application application, string relativePath) =>
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri($"/DevForge.Desktop;component/{relativePath}", UriKind.Relative),
        });

    private static void RunSta(Action action)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            try
            {
                action();
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null)
        {
            ExceptionDispatchInfo.Capture(failure).Throw();
        }
    }
}
```

- [ ] **Step 2: Run the smoke test and verify it finds any remaining integration gap**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~DesktopXamlSmokeTests'
```

Expected before final corrections: FAIL if a resource key, template target, pack URI, theme key, or minimum-size layout is invalid. If it passes immediately, retain it as regression coverage.

- [ ] **Step 3: Add final source-level accessibility and unsupported-action assertions**

```csharp
[Fact]
public void RedesignedViewsRetainAutomationAndDoNotInventBehavior()
{
    var paths = new[]
    {
        "src/DevForge.Desktop/MainWindow.xaml",
        "src/DevForge.Desktop/Dashboard/DashboardView.xaml",
        "src/DevForge.Desktop/CreateProject/CreateProjectView.xaml",
        "src/DevForge.Desktop/RunHistory/RunHistoryView.xaml",
        "src/DevForge.Desktop/BlueprintCatalog/BlueprintCatalogView.xaml",
        "src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorView.xaml",
        "src/DevForge.Desktop/Settings/SettingsView.xaml",
        "src/DevForge.Desktop/Execution/ExecutionCenterView.xaml",
        "src/DevForge.Desktop/Execution/LocalReadyView.xaml",
    };

    Assert.All(paths, path => Assert.Contains(
        "AutomationProperties.Name", Read(path), StringComparison.Ordinal));
    var all = string.Join(Environment.NewLine, paths.Select(Read));
    Assert.DoesNotContain("Click=", all, StringComparison.Ordinal);
    Assert.DoesNotContain("File.", all, StringComparison.Ordinal);
    Assert.DoesNotContain("Process.", all, StringComparison.Ordinal);
    Assert.DoesNotContain("#FFD13438", all, StringComparison.OrdinalIgnoreCase);
}
```

- [ ] **Step 4: Correct only defects proven by the smoke/accessibility tests**

Use semantic resource aliases to fix missing keys, correct `TargetType` mismatches, preserve all existing commands, and add missing automation names. Do not weaken the tests, load Infrastructure from the UI, or add new ViewModel state to make the presentation test pass.

- [ ] **Step 5: Run all focused Desktop tests**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~DevForge.E2ETests.Desktop'
```

Expected: all Desktop tests pass, including resource/view smoke coverage.

- [ ] **Step 6: Commit smoke coverage and proven fixes**

Stage `DesktopXamlSmokeTests.cs`, `DesktopPresentationContractTests.cs`, and only the presentation XAML files changed in Step 4:

```powershell
git add -- tests/DevForge.E2ETests/Desktop/DesktopXamlSmokeTests.cs tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs
git commit -m "test(desktop): protect redesigned WPF presentation"
```

If Step 4 changes XAML, add each exact changed XAML path to the `git add --` command before committing.

### Task 9: Visual QA and release-grade verification

**Files:**

- Modify only presentation XAML/resources when visual evidence proves a defect
- Do not modify `docs/implementation-status.md` unless the user explicitly expands this UI task into milestone bookkeeping

- [ ] **Step 1: Verify formatting, build, focused tests, and full test projects**

Run in this exact order:

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' restore DevForge.sln --locked-mode --verbosity minimal
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' format DevForge.sln --verify-no-changes --no-restore --verbosity minimal
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' build DevForge.sln -c Release --no-restore --disable-build-servers -m:1 -nodeReuse:false --verbosity minimal
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-build --no-restore -m:1 -nodeReuse:false --filter 'FullyQualifiedName~DevForge.E2ETests.Desktop'
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.UnitTests/DevForge.UnitTests.csproj -c Release --no-build --no-restore -m:1 -nodeReuse:false
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.IntegrationTests/DevForge.IntegrationTests.csproj -c Release --no-build --no-restore -m:1 -nodeReuse:false
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.BlueprintTests/DevForge.BlueprintTests.csproj -c Release --no-build --no-restore -m:1 -nodeReuse:false
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' test tests/DevForge.E2ETests/DevForge.E2ETests.csproj -c Release --no-build --no-restore -m:1 -nodeReuse:false
```

Expected: restore succeeds from lock files; format has no diagnostics; Release build has 0 warnings and 0 errors; every test project passes with 0 failed and 0 skipped unless the existing repository baseline proves a pre-existing skip.

- [ ] **Step 2: Launch the Release app and capture both themes and supported sizes**

```powershell
& 'E:\MyProjects\DevForge\.tools\dotnet\dotnet.exe' run --project src/DevForge.Desktop/DevForge.Desktop.csproj -c Release --no-build
```

Inspect Dashboard, Create Configure, Review Plan, Execution Center, Local Ready/Completed, Publish Pending, Blueprint Catalog, Run History, Environment Doctor, and Settings where the current safe fixtures/data permit. Capture or inspect Light and Dark at 1280 x 800 and 960 x 640. Also inspect safe mode, focus visuals, disabled actions, empty states, busy states, validation, stale scan, failure/recovery, and toast styling where the backing state is reachable.

For each screen verify:

- No clipped heading, action, field, remediation, or status text.
- Primary action is visually unambiguous.
- Focus ring remains visible with keyboard-only navigation.
- Status includes glyph and text, not color alone.
- Long paths/IDs trim with tooltip or wrap as designed.
- Lists remain scrollable/virtualized and do not force the window wider.
- Light and dark themes have consistent hierarchy and readable contrast.
- The minimum size remains usable without overlapping or hidden actions.

- [ ] **Step 3: Fix only visually proven defects and rerun the narrowest relevant tests**

For each defect, first add or tighten a presentation/smoke assertion when the defect is mechanically testable, verify it fails, make the smallest resource/XAML correction, and rerun that test plus the affected ViewModel tests. Repeat until the visual checklist is clean.

- [ ] **Step 4: Run final repository integrity checks**

```powershell
git diff --check
rg -n -S 'cmd /c|powershell|Process\.Start|System\.IO|Microsoft\.EntityFrameworkCore' src/DevForge.Desktop -g '*.cs' -g '*.xaml'
git status --short
git diff --name-only 6be9688..HEAD
```

Expected: no whitespace errors; no new forbidden Desktop boundary; status contains only known concurrent M10 changes; UI commit range contains only the planned Desktop resources/views/tests and plan/spec documents.

- [ ] **Step 5: Commit any final visual corrections selectively**

```powershell
git add -- src/DevForge.Desktop/App.xaml src/DevForge.Desktop/MainWindow.xaml src/DevForge.Desktop/Resources/Tokens.xaml src/DevForge.Desktop/Resources/Colors.Light.xaml src/DevForge.Desktop/Resources/Colors.Dark.xaml src/DevForge.Desktop/Resources/Typography.xaml src/DevForge.Desktop/Resources/Icons.xaml src/DevForge.Desktop/Resources/Controls.xaml src/DevForge.Desktop/Resources/Components.xaml src/DevForge.Desktop/Dashboard/DashboardView.xaml src/DevForge.Desktop/CreateProject/CreateProjectView.xaml src/DevForge.Desktop/RunHistory/RunHistoryView.xaml src/DevForge.Desktop/BlueprintCatalog/BlueprintCatalogView.xaml src/DevForge.Desktop/EnvironmentDoctor/EnvironmentDoctorView.xaml src/DevForge.Desktop/Settings/SettingsView.xaml src/DevForge.Desktop/Execution/ExecutionCenterView.xaml src/DevForge.Desktop/Execution/LocalReadyView.xaml tests/DevForge.E2ETests/Desktop/DesktopPresentationContractTests.cs tests/DevForge.E2ETests/Desktop/DesktopXamlSmokeTests.cs
git commit -m "fix(desktop): close visual quality gaps"
```

Omit this commit if Step 3 required no changes. Never stage concurrent M10 files.

## Completion evidence

Before claiming completion, record the exact command results and inspect current files against every acceptance criterion in `docs/superpowers/specs/2026-08-26-ui-ux-redesign-design.md`. Completion requires evidence for all nine views, both explicit themes plus system-theme switching, the 960 x 640 minimum, existing behavior preservation, keyboard/automation accessibility, semantic states, no unsupported actions, and unchanged Desktop security boundaries.

The redesign is incomplete if it only compiles, only recolors the shell, only covers the six supplied screenshots, or lacks runtime visual evidence.
