using System.Collections.Immutable;
using DevForge.Application.Contracts;
using DevForge.Domain.Validation;

namespace DevForge.Desktop.Execution;

public sealed record ExecutionProgressItem(string StepId, string Text);

public sealed class ExecutionSessionCoordinator : IDisposable
{
    private const int MaximumProgressLines = 500;
    private const int MaximumProgressCharacters = 65_536;

    private readonly IProjectCreationWorkflow _workflow;
    private readonly IRunRecoveryService _recovery;
    private readonly IProjectRecoveryWorkflow? _projectRecovery;
    private readonly object _sync = new();
    private readonly Queue<ExecutionProgressItem> _progressLines = new();
    private CancellationTokenSource? _activeCancellation;
    private int _progressCharacterCount;
    private int _active;
    private int _isReadOnly;

    public ExecutionSessionCoordinator(
        IProjectCreationWorkflow workflow,
        IRunRecoveryService recovery)
        : this(workflow, recovery, projectRecovery: null)
    {
    }

    public ExecutionSessionCoordinator(
        IProjectCreationWorkflow workflow,
        IRunRecoveryService recovery,
        IProjectRecoveryWorkflow? projectRecovery)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _recovery = recovery ?? throw new ArgumentNullException(nameof(recovery));
        _projectRecovery = projectRecovery;
    }

    public event EventHandler? ProgressChanged;

    public bool IsActive => Volatile.Read(ref _active) != 0;

    public bool IsReadOnly => Volatile.Read(ref _isReadOnly) != 0;

    public void EnterReadOnlyMode()
    {
        Interlocked.Exchange(ref _isReadOnly, 1);
    }

    public ImmutableArray<ExecutionProgressItem> ProgressLines
    {
        get
        {
            lock (_sync)
            {
                return [.. _progressLines];
            }
        }
    }

    public async Task<ValidationResult<ProjectCreationExecutionSnapshot>> ExecuteAsync(
        ProjectCreationPlanSnapshot plan,
        CancellationToken shutdownToken)
    {
        ArgumentNullException.ThrowIfNull(plan);
        using var linked = BeginOperation(clearProgress: true, shutdownToken);
        try
        {
            var progress = new InlineProgress(ReportProgress);
            return await _workflow.ExecuteAsync(
                plan,
                progress,
                linked.Token).ConfigureAwait(false);
        }
        finally
        {
            EndOperation(linked);
        }
    }

    public async Task<ExecutionOperationResult<RunRecoveryBatch>> RecoverInterruptedAsync(
        CancellationToken shutdownToken)
    {
        using var linked = BeginOperation(clearProgress: false, shutdownToken);
        try
        {
            return await _recovery.RecoverInterruptedAsync(linked.Token).ConfigureAwait(false);
        }
        finally
        {
            EndOperation(linked);
        }
    }

    public async Task<ExecutionOperationResult<RunCheckpoint>> ResumeAsync(
        ExecutionRequest request,
        CancellationToken shutdownToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        using var linked = BeginOperation(clearProgress: false, shutdownToken);
        try
        {
            return await _recovery.ResumeAsync(request, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            EndOperation(linked);
        }
    }

    public bool Cancel()
    {
        CancellationTokenSource? source;
        lock (_sync)
        {
            source = _activeCancellation;
        }

        if (source is null)
        {
            return false;
        }

        try
        {
            source.Cancel();
            return true;
        }
        catch (ObjectDisposedException)
        {
            return false;
        }
    }

    public Task<ProjectRecoveryEligibility> InspectAsync(
        RunCheckpoint checkpoint,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        return _projectRecovery is null
            ? Task.FromResult(ProjectRecoveryEligibility.None)
            : _projectRecovery.InspectAsync(checkpoint, cancellationToken);
    }

    public async Task<ProjectRecoverySnapshot> ContinueAsync(
        string runId,
        ExecutionMode mode,
        CancellationToken shutdownToken)
    {
        if (_projectRecovery is null)
        {
            throw new InvalidOperationException("Project recovery actions are unavailable.");
        }

        using var linked = BeginOperation(clearProgress: false, shutdownToken);
        try
        {
            return await _projectRecovery.ContinueAsync(runId, mode, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            EndOperation(linked);
        }
    }

    public async Task CleanupAsync(string runId, CancellationToken shutdownToken)
    {
        if (_projectRecovery is null)
        {
            throw new InvalidOperationException("Project recovery actions are unavailable.");
        }

        using var linked = BeginOperation(clearProgress: false, shutdownToken);
        try
        {
            await _projectRecovery.CleanupAsync(runId, linked.Token).ConfigureAwait(false);
        }
        finally
        {
            EndOperation(linked);
        }
    }

    public void Dispose()
    {
        Cancel();
    }

    private CancellationTokenSource BeginOperation(
        bool clearProgress,
        CancellationToken shutdownToken)
    {
        if (IsReadOnly)
        {
            throw new InvalidOperationException("Project execution is unavailable in safe read-only mode.");
        }

        if (Interlocked.CompareExchange(ref _active, 1, 0) != 0)
        {
            throw new InvalidOperationException("A creation session is already active.");
        }

        try
        {
            var linked = CancellationTokenSource.CreateLinkedTokenSource(shutdownToken);
            lock (_sync)
            {
                _activeCancellation = linked;
                if (clearProgress)
                {
                    _progressLines.Clear();
                    _progressCharacterCount = 0;
                }
            }

            return linked;
        }
        catch
        {
            Volatile.Write(ref _active, 0);
            throw;
        }
    }

    private void EndOperation(CancellationTokenSource source)
    {
        lock (_sync)
        {
            if (ReferenceEquals(_activeCancellation, source))
            {
                _activeCancellation = null;
            }
        }

        Volatile.Write(ref _active, 0);
    }

    private void ReportProgress(ExecutionProgressLine line)
    {
        var text = line.Text.Value;
        if (text.Length > MaximumProgressCharacters)
        {
            text = text[..MaximumProgressCharacters];
        }

        lock (_sync)
        {
            _progressLines.Enqueue(new ExecutionProgressItem(line.StepId, text));
            _progressCharacterCount += text.Length;
            while (_progressLines.Count > MaximumProgressLines
                || _progressCharacterCount > MaximumProgressCharacters)
            {
                _progressCharacterCount -= _progressLines.Dequeue().Text.Length;
            }
        }

        PublishProgressChanged();
    }

    private void PublishProgressChanged()
    {
        var handlers = ProgressChanged?.GetInvocationList();
        if (handlers is null)
        {
            return;
        }

        foreach (var handler in handlers)
        {
            try
            {
                ((EventHandler)handler)(this, EventArgs.Empty);
            }
            catch (Exception)
            {
                // UI observers are isolated from durable execution.
            }
        }
    }

    private sealed class InlineProgress(Action<ExecutionProgressLine> callback)
        : IProgress<ExecutionProgressLine>
    {
        public void Report(ExecutionProgressLine value)
        {
            callback(value);
        }
    }
}
