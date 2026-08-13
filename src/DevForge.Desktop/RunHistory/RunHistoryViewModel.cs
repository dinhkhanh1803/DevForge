using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Application.Contracts;
using DevForge.Desktop.Execution;
using DevForge.Domain.Runs;

namespace DevForge.Desktop.RunHistory;

public sealed record RunHistoryItemViewModel(
    string RunId,
    string StatusLabel,
    string StatusGlyph,
    string? CurrentStep,
    bool CanResume,
    bool CanRetry,
    bool CanCleanup,
    string? ErrorCode)
{
    public static RunHistoryItemViewModel From(ProjectRun run)
    {
        ArgumentNullException.ThrowIfNull(run);
        var projection = ExecutionCenterViewModel.ProjectStatus(run.Status);
        var hasRunningAttempt = run.CurrentStepId is not null
            || run.Attempts.Any(attempt => attempt.Outcome == StepAttemptOutcome.Running);
        var lastAttempt = run.Attempts.LastOrDefault();
        var canResume = run.ResumeExecution().IsValid;
        var canRetry = run.Status == RunStatus.Executing
            && !hasRunningAttempt
            && lastAttempt?.Outcome == StepAttemptOutcome.Failed
            && lastAttempt.Error?.IsRetryable == true;
        var canCleanup = run.Status is RunStatus.PreflightFailed
            or RunStatus.ValidationFailed
            or RunStatus.Cancelled
            or RunStatus.Failed;
        return new RunHistoryItemViewModel(
            run.Id,
            projection.Label,
            projection.Glyph,
            run.CurrentStepId,
            canResume,
            canRetry,
            canCleanup,
            run.Errors.LastOrDefault()?.Code ?? lastAttempt?.Error?.Code);
    }
}

public sealed partial class RunHistoryViewModel : ObservableObject
{
    private readonly IRunCheckpointStore _store;

    [ObservableProperty]
    private ImmutableArray<RunHistoryItemViewModel> _items = [];

    [ObservableProperty]
    private bool _isBusy;

    public RunHistoryViewModel(IRunCheckpointStore store)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var checkpoints = await _store.ListAsync(cancellationToken).ConfigureAwait(true);
            Items =
            [
                .. checkpoints.OrderBy(item => item.Run.Id, StringComparer.Ordinal)
                    .Select(item => RunHistoryItemViewModel.From(item.Run)),
            ];
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        RefreshCommand.NotifyCanExecuteChanged();
    }
}
