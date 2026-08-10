using DevForge.Application.Contracts;
using DevForge.Infrastructure.Processes;

namespace DevForge.IntegrationTests.Infrastructure.Processes;

public sealed class ProcessOutputRedactionTests
{
    [Fact]
    public void ObserveRedactsSensitiveNeedlesBeforeProgressAndRetention()
    {
        var secretText = "fixture-sensitive-value-123456";
        var sensitive = SensitiveProcessValue.Create(secretText).Value;
        var progress = new RecordingProgress();
        var output = new BoundedProcessOutput(
            [sensitive],
            progress);

        output.Observe(
            ProcessOutputChannel.StandardError,
            "prefix " + secretText + " suffix");
        var result = output.CreateResult(ProcessTerminationReason.Exited, 0);

        var retained = Assert.Single(result.RetainedLines);
        Assert.DoesNotContain(secretText, retained.Text.Value, StringComparison.Ordinal);
        Assert.Contains("[REDACTED]", retained.Text.Value, StringComparison.Ordinal);
        var progressLine = Assert.Single(progress.Lines);
        Assert.DoesNotContain(secretText, progressLine.Text.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Authorization: Bearer abcdefghijklmnopqrstuvwxyz")]
    [InlineData("token=github_pat_abcdefghijklmnopqrstuvwxyz")]
    [InlineData("-----BEGIN PRIVATE KEY-----")]
    [InlineData(".env contents: DATABASE_URL=server-value")]
    public void RedactorRemovesCredentialShapedOutput(string rawOutput)
    {
        var output = new BoundedProcessOutput([], progress: null);

        output.Observe(ProcessOutputChannel.StandardOutput, rawOutput);
        var result = output.CreateResult(ProcessTerminationReason.Exited, 0);

        var retained = Assert.Single(result.RetainedLines);
        Assert.Contains("[REDACTED", retained.Text.Value, StringComparison.Ordinal);
        Assert.DoesNotContain(rawOutput, retained.Text.Value, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Foreign key: FK_ProjectRun")]
    [InlineData("The .env file was not read")]
    public void RedactorPreservesSafeDiagnosticText(string rawOutput)
    {
        var output = new BoundedProcessOutput([], progress: null);

        output.Observe(ProcessOutputChannel.StandardOutput, rawOutput);
        var result = output.CreateResult(ProcessTerminationReason.Exited, 0);

        Assert.Equal(rawOutput, Assert.Single(result.RetainedLines).Text.Value);
    }

    [Fact]
    public void OutputRetentionIsBoundedByLineCountAndCharacters()
    {
        var output = new BoundedProcessOutput([], progress: null);
        for (var index = 0; index < ProcessResult.MaxRetainedOutputLines + 10; index++)
        {
            output.Observe(ProcessOutputChannel.StandardOutput, new string('x', 500));
        }

        var result = output.CreateResult(ProcessTerminationReason.Exited, 0);

        Assert.True(result.IsOutputTruncated);
        Assert.True(result.RetainedLines.Length <= ProcessResult.MaxRetainedOutputLines);
        Assert.True(result.RetainedCharacterCount <= ProcessResult.MaxRetainedOutputCharacters);
    }

    [Fact]
    public void PhysicalLineIsTruncatedBeforeItCrossesTheOutputContract()
    {
        var output = new BoundedProcessOutput([], progress: null);

        output.Observe(
            ProcessOutputChannel.StandardOutput,
            new string('x', ProcessOutputLine.MaxTextLength + 100));
        var result = output.CreateResult(ProcessTerminationReason.Exited, 0);

        Assert.True(result.IsOutputTruncated);
        Assert.Equal(
            ProcessOutputLine.MaxTextLength,
            Assert.Single(result.RetainedLines).Text.Value.Length);
    }

    [Fact]
    public void BlankPhysicalLinesAreNotRetained()
    {
        var output = new BoundedProcessOutput([], progress: null);

        output.Observe(ProcessOutputChannel.StandardOutput, "   ");
        var result = output.CreateResult(ProcessTerminationReason.Exited, 0);

        Assert.Empty(result.RetainedLines);
    }

    [Fact]
    public async Task ConcurrentStandardStreamsRemainBoundedAndReportEveryRedactedLine()
    {
        var progress = new RecordingProgress();
        var output = new BoundedProcessOutput([], progress);
        const int producerCount = 16;
        const int linesPerProducer = 200;

        var producers = Enumerable.Range(0, producerCount)
            .Select(producer => Task.Run(() =>
            {
                var channel = producer % 2 == 0
                    ? ProcessOutputChannel.StandardOutput
                    : ProcessOutputChannel.StandardError;
                for (var line = 0; line < linesPerProducer; line++)
                {
                    output.Observe(channel, $"producer-{producer}-line-{line}");
                }
            }))
            .ToArray();

        await Task.WhenAll(producers);
        var result = output.CreateResult(ProcessTerminationReason.Exited, 0);

        Assert.Equal(producerCount * linesPerProducer, progress.Lines.Count);
        Assert.True(result.IsOutputTruncated);
        Assert.Equal(ProcessResult.MaxRetainedOutputLines, result.RetainedLines.Length);
    }

    private sealed class RecordingProgress : IProgress<ProcessOutputLine>
    {
        public List<ProcessOutputLine> Lines { get; } = [];

        public void Report(ProcessOutputLine value)
        {
            Lines.Add(value);
        }
    }
}
