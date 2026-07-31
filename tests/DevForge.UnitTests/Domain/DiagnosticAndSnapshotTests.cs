using DevForge.Domain.Diagnostics;
using DevForge.Domain.Environment;
using DevForge.Domain.Privacy;
using DevForge.Domain.Reports;

namespace DevForge.UnitTests.Domain;

public sealed class DiagnosticAndSnapshotTests
{
    [Fact]
    public void DevForgeErrorSnapshotsSuggestedActionsAndRedactedContext()
    {
        var actions = new List<string> { "Authenticate and retry." };
        var safeDetail = SanitizedText.Create("The GitHub CLI reported a redacted authentication error.").Value;
        var context = new Dictionary<string, SanitizedText> { ["repository"] = SanitizedText.Create("redacted-owner/repo").Value };

        var result = DevForgeError.Create(
            "github.auth.failed",
            "GitHub authentication failed.",
            safeDetail,
            "publish",
            "publish-github",
            true,
            actions,
            context);
        Assert.True(result.IsValid);
        var error = result.Value;

        actions[0] = "changed";
        context["repository"] = SanitizedText.Create("changed").Value;

        Assert.Equal(["Authenticate and retry."], error.SuggestedActions.ToArray());
        Assert.Equal("redacted-owner/repo", error.RedactedContext["repository"].Value);
    }

    [Fact]
    public void EnvironmentSnapshotSnapshotsToolsAndProperties()
    {
        var tools = new List<EnvironmentTool>
        {
            new(" dotnet ", " 10.0.100 ", true),
        };
        var properties = new Dictionary<string, SanitizedText> { [" architecture "] = SanitizedText.Create("x64").Value };

        var snapshot = EnvironmentSnapshot.Create(DateTimeOffset.UtcNow, tools, properties).Value;
        tools.Clear();
        properties[" architecture "] = SanitizedText.Create("changed").Value;

        Assert.Single(snapshot.Tools);
        Assert.Equal("dotnet", snapshot.Tools[0].Name);
        Assert.Equal("10.0.100", snapshot.Tools[0].Version);
        Assert.Equal("x64", snapshot.Properties["architecture"].Value);
    }

    [Fact]
    public void GenerationReportSnapshotsValidationResultsErrorsAndArtifacts()
    {
        var checks = new List<ValidationCheck>
        {
            new(" build ", ValidationCheckStatus.Passed, "Build passed.", SanitizedText.Create("Safe detail.").Value),
        };
        var errors = new List<DevForgeError>();
        var artifacts = new List<string> { "README.md" };

        var report = GenerationReport.Create("run-1", DateTimeOffset.UtcNow, checks, errors, artifacts).Value;
        checks.Clear();
        artifacts[0] = "changed";

        Assert.Single(report.Validations);
        Assert.Equal(["README.md"], report.GeneratedArtifacts.ToArray());
        Assert.Equal("build", report.Validations[0].Id);
        Assert.Equal("Safe detail.", report.Validations[0].Detail?.Value);
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
    [Fact]
    public void DiagnosticBoundariesRejectSecretShapedKeys()
    {
        var safe = SanitizedText.Create("[REDACTED]").Value;
        var error = DevForgeError.Create(
            "build.failed",
            "Build failed.",
            safe,
            "validation",
            null,
            false,
            [],
            new Dictionary<string, SanitizedText> { ["api_token"] = safe });
        var environment = EnvironmentSnapshot.Create(
            DateTimeOffset.UtcNow,
            [],
            new Dictionary<string, SanitizedText> { ["connectionString"] = safe });

        Assert.Equal("error.context.key.secret-shaped", Assert.Single(error.Issues).Code);
        Assert.Equal("environment.property.name.secret-shaped", Assert.Single(environment.Issues).Code);
    }

    [Fact]
    public void GenerationReportRejectsNormalizedDuplicateIdsAndUndefinedStatuses()
    {
        var checks = new[]
        {
            new ValidationCheck(" build ", ValidationCheckStatus.Passed, "Passed.", null),
            new ValidationCheck("build", (ValidationCheckStatus)999, "Invalid.", null),
        };

        var result = GenerationReport.Create("run-1", DateTimeOffset.UtcNow, checks, [], []);

        Assert.False(result.IsValid);
        Assert.Equal(
            ["report.validation.id.duplicate", "report.validation.status.invalid"],
            result.Issues.Select(issue => issue.Code));
    }
}
