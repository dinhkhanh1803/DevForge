using DevForge.Domain.Diagnostics;
using DevForge.Domain.Environment;
using DevForge.Domain.Reports;

namespace DevForge.UnitTests.Domain;

public sealed class DiagnosticAndSnapshotTests
{
    [Fact]
    public void DevForgeErrorSnapshotsSuggestedActionsAndRedactedContext()
    {
        var actions = new List<string> { "Authenticate and retry." };
        var context = new Dictionary<string, string> { ["repository"] = "redacted-owner/repo" };

        var result = DevForgeError.Create(
            "github.auth.failed",
            "GitHub authentication failed.",
            "The GitHub CLI reported a redacted authentication error.",
            "publish",
            "publish-github",
            true,
            actions,
            context);
        Assert.True(result.IsValid);
        var error = result.Value;

        actions[0] = "changed";
        context["repository"] = "changed";

        Assert.Equal(["Authenticate and retry."], error.SuggestedActions.ToArray());
        Assert.Equal("redacted-owner/repo", error.RedactedContext["repository"]);
    }

    [Fact]
    public void EnvironmentSnapshotSnapshotsToolsAndProperties()
    {
        var tools = new List<EnvironmentTool>
        {
            new("dotnet", "10.0.100", true),
        };
        var properties = new Dictionary<string, string> { ["architecture"] = "x64" };

        var snapshot = EnvironmentSnapshot.Create(DateTimeOffset.UtcNow, tools, properties).Value;
        tools.Clear();
        properties["architecture"] = "changed";

        Assert.Single(snapshot.Tools);
        Assert.Equal("x64", snapshot.Properties["architecture"]);
    }

    [Fact]
    public void GenerationReportSnapshotsValidationResultsErrorsAndArtifacts()
    {
        var checks = new List<ValidationCheck>
        {
            new("build", ValidationCheckStatus.Passed, "Build passed.", null),
        };
        var errors = new List<DevForgeError>();
        var artifacts = new List<string> { "README.md" };

        var report = GenerationReport.Create("run-1", DateTimeOffset.UtcNow, checks, errors, artifacts).Value;
        checks.Clear();
        artifacts[0] = "changed";

        Assert.Single(report.Validations);
        Assert.Equal(["README.md"], report.GeneratedArtifacts.ToArray());
    }

    [Fact]
    public void DevForgeErrorCreateAggregatesExpectedInputIssues()
    {
        var result = DevForgeError.Create(null, null, null, null, null, false, null, null);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "error.code.required",
                "error.summary.required",
                "error.technical-detail.required",
                "error.phase.required",
                "error.suggested-actions.required",
                "error.context.required",
            ],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void EnvironmentSnapshotCreateAggregatesExpectedInputIssues()
    {
        var result = EnvironmentSnapshot.Create(DateTimeOffset.UtcNow, null, null);

        Assert.False(result.IsValid);
        Assert.Equal(
            ["environment.tools.required", "environment.properties.required"],
            result.Issues.Select(issue => issue.Code));
    }

    [Fact]
    public void GenerationReportCreateAggregatesExpectedInputIssues()
    {
        var result = GenerationReport.Create(null, DateTimeOffset.UtcNow, null, null, null);

        Assert.False(result.IsValid);
        Assert.Equal(
            [
                "report.run-id.required",
                "report.validations.required",
                "report.errors.required",
                "report.artifacts.required",
            ],
            result.Issues.Select(issue => issue.Code));
    }
}
