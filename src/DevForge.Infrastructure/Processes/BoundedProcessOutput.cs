using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Domain.Privacy;

namespace DevForge.Infrastructure.Processes;

internal sealed class BoundedProcessOutput
{
    private readonly ProcessOutputRedactor _redactor;
    private readonly IProgress<ProcessOutputLine>? _progress;
    private readonly Lock _sync = new();
    private readonly ImmutableArray<ProcessOutputLine>.Builder _retainedLines =
        ImmutableArray.CreateBuilder<ProcessOutputLine>();
    private int _retainedCharacterCount;
    private bool _isOutputTruncated;

    public BoundedProcessOutput(
        IEnumerable<SensitiveProcessValue> redactionNeedles,
        IProgress<ProcessOutputLine>? progress)
    {
        _redactor = new ProcessOutputRedactor(redactionNeedles);
        _progress = progress;
    }

    public void Observe(ProcessOutputChannel channel, string? rawLine)
    {
        if (!_redactor.TryRedact(rawLine, out var redactedText))
        {
            return;
        }

        lock (_sync)
        {
            var boundedText = BoundLine(redactedText!);
            var line = ProcessOutputLine.Create(channel, boundedText).Value;
            _progress?.Report(line);

            if (_retainedLines.Count >= ProcessResult.MaxRetainedOutputLines
                || _retainedCharacterCount + line.Text.Value.Length
                    > ProcessResult.MaxRetainedOutputCharacters)
            {
                _isOutputTruncated = true;
                return;
            }

            _retainedLines.Add(line);
            _retainedCharacterCount += line.Text.Value.Length;
        }
    }

    public ProcessResult CreateResult(ProcessTerminationReason reason, int? exitCode)
    {
        lock (_sync)
        {
            var result = ProcessResult.Create(
                reason,
                exitCode,
                _retainedLines,
                _isOutputTruncated);
            if (!result.IsValid)
            {
                throw new InvalidOperationException("The bounded process result is invalid.");
            }

            return result.Value;
        }
    }

    public void MarkTruncated()
    {
        lock (_sync)
        {
            _isOutputTruncated = true;
        }
    }

    private RedactedText BoundLine(RedactedText redactedText)
    {
        if (redactedText.Value.Length <= ProcessOutputLine.MaxTextLength)
        {
            return redactedText;
        }

        _isOutputTruncated = true;
        return RedactedText.FromTrustedRedaction(
            redactedText.Value[..ProcessOutputLine.MaxTextLength]).Value;
    }
}
