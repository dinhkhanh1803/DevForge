using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;

namespace DevForge.UnitTests.Application.Diagnostics;

public sealed class DiagnosticContractsTests
{
    [Fact]
    public void EventAcceptsCanonicalBoundedRedactedData()
    {
        var result = DiagnosticEvent.Create(
            new DateTimeOffset(2026, 8, 26, 7, 30, 0, TimeSpan.Zero),
            DiagnosticLevel.Information,
            "execution.step.completed",
            "run-001",
            "restore",
            1,
            "execution-orchestrator",
            RedactedText.FromTrustedRedaction("Restore completed.").Value,
            125,
            null);

        Assert.True(result.IsValid);
        Assert.Equal("execution.step.completed", result.Value.EventId);
        Assert.Equal(125, result.Value.DurationMs);
    }

    [Theory]
    [InlineData(0, 16 * 1024 * 1024L)]
    [InlineData(366, 16 * 1024 * 1024L)]
    [InlineData(30, 16 * 1024 * 1024L - 1)]
    [InlineData(30, 2L * 1024 * 1024 * 1024 + 1)]
    public void RetentionRejectsOutOfRangeValues(int days, long bytes)
    {
        var result = DiagnosticRetentionPolicy.Create(days, bytes);

        Assert.False(result.IsValid);
    }

    [Fact]
    public void RetentionDefaultsAreReleaseDefaults()
    {
        var policy = DiagnosticRetentionPolicy.Default;

        Assert.Equal(30, policy.MaxAgeDays);
        Assert.Equal(256L * 1024 * 1024, policy.MaxTotalBytes);
    }

    [Fact]
    public void EventRejectsNonUtcControlBearingAndInvalidNumericFields()
    {
        var result = DiagnosticEvent.Create(
            new DateTimeOffset(2026, 8, 26, 7, 30, 0, TimeSpan.FromHours(7)),
            (DiagnosticLevel)999,
            "event\nforged",
            "run\tforged",
            "step",
            0,
            "source",
            RedactedText.FromTrustedRedaction("Safe message.").Value,
            -1,
            "DF\rFORGED");

        Assert.False(result.IsValid);
        Assert.True(result.Issues.Length >= 6);
    }

    [Theory]
    [InlineData("eventId")]
    [InlineData("runId")]
    [InlineData("stepId")]
    [InlineData("source")]
    [InlineData("errorCode")]
    public void EventRejectsCredentialShapedValuesInEveryStructuredField(string field)
    {
        const string credential = "ghp_abcdefghijklmnop";
        var result = DiagnosticEvent.Create(
            DateTimeOffset.UnixEpoch,
            DiagnosticLevel.Error,
            field == "eventId" ? credential : "safe.event",
            field == "runId" ? credential : "run-001",
            field == "stepId" ? credential : "step-001",
            1,
            field == "source" ? credential : "safe-source",
            RedactedText.FromTrustedRedaction("Safe message.").Value,
            1,
            field == "errorCode" ? credential : "DF-SAFE-001");

        Assert.False(result.IsValid);
    }
}
