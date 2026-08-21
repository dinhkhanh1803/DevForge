using DevForge.Application.Contracts;
using DevForge.Application.Contracts.Persistence;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Desktop.Bootstrap;
using DevForge.Desktop.EnvironmentDoctor;
using DevForge.Desktop.Execution;
using DevForge.Desktop.Navigation;
using DevForge.Desktop.RunHistory;
using DevForge.Desktop.Theming;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.Desktop;

public sealed class DesktopBehaviorMatrixTests
{
    [Fact]
    public void M7RouteMatrixHasExactEnabledBoundary()
    {
        var matrix = NavigationService.Descriptors.ToDictionary(item => item.Route, item => item.IsEnabled);

        Assert.True(matrix[DesktopRoute.Dashboard]);
        Assert.True(matrix[DesktopRoute.EnvironmentDoctor]);
        Assert.True(matrix[DesktopRoute.Settings]);
        Assert.True(matrix[DesktopRoute.CreateProject]);
        Assert.True(matrix[DesktopRoute.RunHistory]);
        Assert.True(matrix[DesktopRoute.BlueprintCatalog]);
        Assert.Equal("Run History", NavigationService.Descriptors.Single(
            item => item.Route == DesktopRoute.RunHistory).Label);
    }

    [Fact]
    public void StatusMatrixAlwaysHasTextAndIconEvidence()
    {
        var scannedAt = DateTimeOffset.UnixEpoch;

        Assert.All(Enum.GetValues<EnvironmentToolStatus>(), status =>
        {
            var item = new EnvironmentHealthItem("tool", null, status, scannedAt);
            Assert.False(string.IsNullOrWhiteSpace(item.StatusLabel));
            Assert.False(string.IsNullOrWhiteSpace(item.StatusGlyph));
            Assert.False(string.IsNullOrWhiteSpace(item.CompatibilitySummary));
            Assert.False(string.IsNullOrWhiteSpace(item.Remediation));
        });
    }

    [Fact]
    public void ClosedDesktopEnumsHaveExplicitNonzeroValues()
    {
        Assert.Equal([1, 2, 3], Enum.GetValues<ThemePreference>().Select(value => (int)value));
        Assert.Equal([1, 2], Enum.GetValues<DesktopStartupMode>().Select(value => (int)value));
        Assert.Equal([1, 2, 3], Enum.GetValues<DesktopMigrationOutcome>().Select(value => (int)value));
    }

    [Fact]
    public void M7ClosedWorkflowMatricesHaveExactValues()
    {
        Assert.Equal(
            [BlueprintInputKind.Text, BlueprintInputKind.Boolean, BlueprintInputKind.WholeNumber, BlueprintInputKind.Choice],
            Enum.GetValues<BlueprintInputKind>());
        Assert.Equal(
            [ProjectCreationStage.Configure, ProjectCreationStage.ReviewPlan, ProjectCreationStage.Execute, ProjectCreationStage.LocalReady, ProjectCreationStage.PublishPending, ProjectCreationStage.Completed],
            Enum.GetValues<ProjectCreationStage>());
        Assert.Equal(
            [ExecutionMode.Fresh, ExecutionMode.Resume, ExecutionMode.ManualRetry],
            Enum.GetValues<ExecutionMode>());
        Assert.Equal(
            [BlueprintTrust.BuiltIn, BlueprintTrust.TrustedLocal, BlueprintTrust.Untrusted, BlueprintTrust.Quarantined],
            Enum.GetValues<BlueprintTrust>());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    [InlineData(false, false, true)]
    public void RunActionMatrixUsesApplicationEligibilityOnly(
        bool canResume,
        bool canRetry,
        bool canCleanup)
    {
        var run = ProjectRun.Create(
            $"run-{new string('1', 32)}",
            $"recipe-{new string('2', 32)}").Value;
        var item = RunHistoryItemViewModel.From(
            run,
            new ProjectRecoveryEligibility(canResume, canRetry, canCleanup));

        Assert.Equal(canResume, item.CanResume);
        Assert.Equal(canRetry, item.CanRetry);
        Assert.Equal(canCleanup, item.CanCleanup);
    }

    [Fact]
    public void PresetAndDeferredFeatureBoundariesReflectM8ReviewedIntent()
    {
        Assert.Equal(
            ["Blueprint", "Features", "Git", "IdeId", "Inputs"],
            typeof(ProjectCreationPresetDraft).GetProperties()
                .Select(property => property.Name)
                .Order(StringComparer.Ordinal));
        Assert.Equal("LOCAL PROJECT READY", ExecutionCenterViewModel.ProjectStatus(RunStatus.LocalReady).Label);
        Assert.Equal("COMPLETED", ExecutionCenterViewModel.ProjectStatus(RunStatus.Completed).Label);
        Assert.NotEqual(
            ExecutionCenterViewModel.ProjectStatus(RunStatus.LocalReady),
            ExecutionCenterViewModel.ProjectStatus(RunStatus.Completed));
        var deferred = ExecutionStepViewModel.From("create", "Create", attempt: null);
        Assert.False(deferred.CanOpenStaging);
        Assert.False(deferred.CanCreateSupportBundle);
    }

    [Theory]
    [InlineData(RunStatus.Draft, false)]
    [InlineData(RunStatus.Planning, true)]
    [InlineData(RunStatus.PreflightFailed, false)]
    [InlineData(RunStatus.Executing, true)]
    [InlineData(RunStatus.ValidationFailed, true)]
    [InlineData(RunStatus.LocalReady, false)]
    [InlineData(RunStatus.PublishPending, false)]
    [InlineData(RunStatus.Completed, false)]
    [InlineData(RunStatus.Cancelled, true)]
    [InlineData(RunStatus.Failed, false)]
    public void RecoveryStateMatrixMatchesTheDomainLifecycle(
        RunStatus status,
        bool expectedCanResume)
    {
        var run = ProjectRun.Rehydrate(
            $"run-{new string('1', 32)}",
            $"recipe-{new string('2', 32)}",
            status,
            currentStepId: null,
            attempts: [],
            errors: []);
        Assert.True(run.IsValid);

        Assert.Equal(expectedCanResume, run.Value.ResumeExecution().IsValid);
    }
}
