using System.Collections.Immutable;

namespace DevForge.Domain.Execution;

public sealed class ExecutionStep
{
    private ExecutionStep(
        string id,
        string name,
        string handler,
        IEnumerable<KeyValuePair<string, string>> inputs,
        TimeSpan timeout,
        RetryPolicy retryPolicy)
    {
        Id = id;
        Name = name;
        Handler = handler;
        Inputs = inputs.ToImmutableDictionary(StringComparer.Ordinal);
        Timeout = timeout;
        RetryPolicy = retryPolicy;
    }

    public string Id { get; }

    public string Name { get; }

    public string Handler { get; }

    public ImmutableDictionary<string, string> Inputs { get; }

    public TimeSpan Timeout { get; }

    public RetryPolicy RetryPolicy { get; }

    public static ExecutionStep Create(
        string id,
        string name,
        string handler,
        IEnumerable<KeyValuePair<string, string>> inputs,
        TimeSpan timeout,
        RetryPolicy retryPolicy)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentException.ThrowIfNullOrWhiteSpace(handler);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(retryPolicy);
        if (timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(timeout), "Step timeout must be positive.");
        }

        return new ExecutionStep(
            id.Trim(),
            name.Trim(),
            handler.Trim(),
            inputs,
            timeout,
            retryPolicy);
    }
}

public sealed class ExecutionPlan
{
    public ExecutionPlan(string id, IEnumerable<ExecutionStep> steps)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(id);
        ArgumentNullException.ThrowIfNull(steps);

        Id = id.Trim();
        Steps = [.. steps];
    }

    public string Id { get; }

    public ImmutableArray<ExecutionStep> Steps { get; }
}
