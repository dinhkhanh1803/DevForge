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

        var error = new DevForgeError(
            "github.auth.failed",
            "GitHub authentication failed.",
            "The GitHub CLI reported a redacted authentication error.",
            "publish",
            "publish-github",
            true,
            actions,
            context);
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

        var snapshot = new EnvironmentSnapshot(DateTimeOffset.UtcNow, tools, properties);
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

        var report = new GenerationReport("run-1", DateTimeOffset.UtcNow, checks, errors, artifacts);
        checks.Clear();
        artifacts[0] = "changed";

        Assert.Single(report.Validations);
        Assert.Equal(["README.md"], report.GeneratedArtifacts.ToArray());
    }
}
