using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Application.Contracts;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

namespace DevForge.Desktop.Execution;

public sealed record RunStatusProjection(string Label, string Glyph);

public sealed partial class ExecutionCenterViewModel : ObservableObject, IDisposable
{
    private readonly ExecutionSessionCoordinator _coordinator;
    private readonly bool _m10ActionsEnabled;

    [ObservableProperty]
    private ProjectCreationExecutionSnapshot? _snapshot;

    [ObservableProperty]
    private ImmutableArray<ExecutionStepViewModel> _steps = [];

    [ObservableProperty]
    private ImmutableArray<ExecutionProgressItem> _progressLines = [];

    [ObservableProperty]
    private ImmutableArray<ValidationIssue> _validationIssues = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private RunCheckpoint? _recoveredCheckpoint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanResume))]
    private ExecutionRequest? _resumeRequest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanRetry))]
    private ExecutionRequest? _retryRequest;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCleanup))]
    private RunCheckpoint? _cleanupCheckpoint;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCleanup))]
    private IWorkspaceFileSystem? _cleanupWorkspace;

    public ExecutionCenterViewModel(ExecutionSessionCoordinator coordinator)
    {
        _coordinator = coordinator ?? throw new ArgumentNullException(nameof(coordinator));
        _m10ActionsEnabled = false;
        _coordinator.ProgressChanged += OnProgressChanged;
        CancelCommand = new RelayCommand(
            () => _coordinator.Cancel(),
            () => IsBusy);
        ResumeCommand = new AsyncRelayCommand(
            ResumeAsync,
            () => !IsBusy && CanResume);
        RetryCommand = new AsyncRelayCommand(
            RetryAsync,
            () => !IsBusy && CanRetry);
        CleanupCommand = new AsyncRelayCommand(
            CleanupAsync,
            () => !IsBusy && CanCleanup);
    }

    public IRelayCommand CancelCommand { get; }

    public IAsyncRelayCommand ResumeCommand { get; }

    public IAsyncRelayCommand RetryCommand { get; }

    public IAsyncRelayCommand CleanupCommand { get; }

    public bool CanResume => ResumeRequest?.Mode == ExecutionMode.Resume;

    public bool CanRetry => RetryRequest?.Mode == ExecutionMode.ManualRetry;

    public bool CanCleanup => CleanupCheckpoint is not null && CleanupWorkspace is not null;

    public bool CanOpenStaging => _m10ActionsEnabled;

    public bool CanCreateSupportBundle => _m10ActionsEnabled;

    public RunStatusProjection Status => ProjectStatus(
        Snapshot?.Checkpoint.Run.Status ?? RecoveredCheckpoint?.Run.Status ?? RunStatus.Draft);

    public void ApplyRecovered(PlannedProject plannedProject, RunCheckpoint checkpoint)
    {
        ArgumentNullException.ThrowIfNull(plannedProject);
        ArgumentNullException.ThrowIfNull(checkpoint);
        RecoveredCheckpoint = checkpoint;
        Snapshot = null;
        ValidationIssues = [];
        Steps =
        [
            .. plannedProject.Plan.Steps.Select(step =>
                ExecutionStepViewModel.From(
                    step.Id,
                    step.Name,
                    checkpoint.Run.Attempts.LastOrDefault(
                        attempt => StringComparer.Ordinal.Equals(attempt.StepId, step.Id)))),
        ];
        OnPropertyChanged(nameof(Status));
    }

    public async Task ExecuteAsync(
        ProjectCreationPlanSnapshot plan,
        CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        Snapshot = null;
        RecoveredCheckpoint = null;
        ValidationIssues = [];
        Steps = [];
        ProgressLines = [];
        OnPropertyChanged(nameof(Status));
        SetBusy(true);
        try
        {
            var result = await _coordinator.ExecuteAsync(plan, cancellationToken).ConfigureAwait(true);
            if (!result.IsValid)
            {
                ValidationIssues = result.Issues;
                return;
            }

            ApplySnapshot(result.Value);
        }
        finally
        {
            SetBusy(false);
        }
    }

    public static RunStatusProjection ProjectStatus(RunStatus status)
    {
        return status switch
        {
            RunStatus.Draft => new("DRAFT", "○"),
            RunStatus.Planning => new("PLANNING", "…"),
            RunStatus.PreflightFailed => new("PREFLIGHT FAILED", "!"),
            RunStatus.Executing => new("EXECUTING", "▶"),
            RunStatus.ValidationFailed => new("VALIDATION FAILED", "!"),
            RunStatus.LocalReady => new("LOCAL PROJECT READY", "✓"),
            RunStatus.PublishPending => new("PUBLISH PENDING", "…"),
            RunStatus.Completed => new("COMPLETED", "✓"),
            RunStatus.Cancelled => new("CANCELLED", "■"),
            RunStatus.Failed => new("FAILED", "✕"),
            _ => new("UNKNOWN", "?"),
        };
    }

    public void Dispose()
    {
        _coordinator.ProgressChanged -= OnProgressChanged;
    }

    partial void OnResumeRequestChanged(ExecutionRequest? value) =>
        ResumeCommand.NotifyCanExecuteChanged();

    partial void OnRetryRequestChanged(ExecutionRequest? value) =>
        RetryCommand.NotifyCanExecuteChanged();

    partial void OnCleanupCheckpointChanged(RunCheckpoint? value) =>
        CleanupCommand.NotifyCanExecuteChanged();

    partial void OnCleanupWorkspaceChanged(IWorkspaceFileSystem? value) =>
        CleanupCommand.NotifyCanExecuteChanged();

    private void ApplySnapshot(ProjectCreationExecutionSnapshot snapshot)
    {
        Snapshot = snapshot;
        ValidationIssues = [];
        Steps =
        [
            .. snapshot.Plan.PlannedProject.Plan.Steps.Select(step =>
                ExecutionStepViewModel.From(
                    step.Id,
                    step.Name,
                    snapshot.Checkpoint.Run.Attempts.LastOrDefault(
                        attempt => StringComparer.Ordinal.Equals(attempt.StepId, step.Id)))),
        ];
        OnPropertyChanged(nameof(Status));
    }

    private void OnProgressChanged(object? sender, EventArgs args)
    {
        ProgressLines = _coordinator.ProgressLines;
    }

    private async Task ResumeAsync(CancellationToken cancellationToken)
    {
        await ContinueAsync(ResumeRequest, cancellationToken).ConfigureAwait(true);
    }

    private async Task RetryAsync(CancellationToken cancellationToken)
    {
        await ContinueAsync(RetryRequest, cancellationToken).ConfigureAwait(true);
    }

    private async Task ContinueAsync(
        ExecutionRequest? request,
        CancellationToken cancellationToken)
    {
        if (request is null || Snapshot is null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _coordinator.ResumeAsync(request, cancellationToken).ConfigureAwait(true);
            if (!result.IsSuccessful)
            {
                ValidationIssues =
                [
                    new ValidationIssue(
                        result.Error!.Code,
                        result.Error.Summary,
                        "execution"),
                ];
                return;
            }

            var snapshot = ProjectCreationExecutionSnapshot.Create(Snapshot.Plan, result.Value);
            if (snapshot.IsValid)
            {
                ApplySnapshot(snapshot.Value);
            }
            else
            {
                ValidationIssues = snapshot.Issues;
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CleanupAsync(CancellationToken cancellationToken)
    {
        if (CleanupCheckpoint is null || CleanupWorkspace is null)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _coordinator.CleanupAsync(
                CleanupCheckpoint,
                CleanupWorkspace,
                cancellationToken).ConfigureAwait(true);
            if (!result.IsSuccessful)
            {
                ValidationIssues =
                [
                    new ValidationIssue(
                        result.Error!.Code,
                        result.Error.Summary,
                        "cleanup"),
                ];
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        CancelCommand.NotifyCanExecuteChanged();
        ResumeCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        CleanupCommand.NotifyCanExecuteChanged();
    }
}
