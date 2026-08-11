using System.Text;
using Scriban.Runtime;

namespace DevForge.Infrastructure.Templates;

internal sealed class BoundedTemplateOutput : IScriptOutput
{
    private readonly StringBuilder _builder;
    private readonly CancellationToken _renderToken;
    private readonly int _maximumLength;

    public BoundedTemplateOutput(int maximumLength, CancellationToken renderToken)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maximumLength);
        _maximumLength = maximumLength;
        _renderToken = renderToken;
        _builder = new StringBuilder(Math.Min(maximumLength, 4096));
    }

    public void Write(string text, int offset, int count)
    {
        ArgumentNullException.ThrowIfNull(text);
        _renderToken.ThrowIfCancellationRequested();
        ArgumentOutOfRangeException.ThrowIfNegative(offset);
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(offset, text.Length - count);

        if (_builder.Length > _maximumLength - count)
        {
            throw new TemplateOutputLimitExceededException();
        }

        _builder.Append(text, offset, count);
    }

    public ValueTask WriteAsync(
        string text,
        int offset,
        int count,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Write(text, offset, count);
        return ValueTask.CompletedTask;
    }

    public override string ToString()
    {
        return _builder.ToString();
    }
}
