using DevForge.Desktop.Execution;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Execution;
using DevForge.Domain.Privacy;
using DevForge.Domain.Runs;

namespace DevForge.E2ETests.Desktop;

public sealed class ExecutionCenterViewModelTests
{
    [Theory]
    [InlineData(RunStatus.Draft, "DRAFT")]
    [InlineData(RunStatus.Executing, "EXECUTING")]
    [InlineData(RunStatus.ValidationFailed, "VALIDATION FAILED")]
    [InlineData(RunStatus.LocalReady, "LOCAL PROJECT READY")]
    [InlineData(RunStatus.Cancelled, "CANCELLED")]
    [InlineData(RunStatus.Failed, "FAILED")]
    public void StatusProjectionHasExactTextAndIcon(RunStatus status, string expected)
    {
        var projection = ExecutionCenterViewModel.ProjectStatus(status);

        Assert.Equal(expected, projection.Label);
        Assert.False(string.IsNullOrWhiteSpace(projection.Glyph));
    }

    [Fact]
    public void StepProjectionUsesAttemptEvidenceAndNeverEnablesM10Actions()
    {
        var error = DevForgeError.Create(
            "DF-EXEC-001",
            "Execution failed.",
            RedactedText.FromTrustedRedaction("Safe remediation.").Value,
            "Execute",
            "create",
            isRetryable: false,
            ["Review the generated input."],
            []).Value;
        var failed = StepAttempt.Rehydrate(
            "create",
            1,
            DateTimeOffset.UnixEpoch,
            DateTimeOffset.UnixEpoch.AddSeconds(2),
            StepAttemptOutcome.Failed,
            exitCode: 1,
            error,
            outputDigest: $"sha256:{new string('1', 64)}").Value;

        var item = ExecutionStepViewModel.From("create", "Create files", failed);

        Assert.Equal("FAILED", item.StatusLabel);
        Assert.Equal("DF-EXEC-001", item.ErrorCode);
        Assert.Equal(1, item.AttemptNumber);
        Assert.Equal(TimeSpan.FromSeconds(2), item.Duration);
        Assert.False(item.CanOpenStaging);
        Assert.False(item.CanCreateSupportBundle);
    }
}
