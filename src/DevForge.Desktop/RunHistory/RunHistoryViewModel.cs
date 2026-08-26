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
    bool CanRetryPublish,
    bool CanCreateSupportBundle,
    string? ErrorCode)
{
    public static RunHistoryItemViewModel From(ProjectRun run)
    {
        return From(run, ProjectRecoveryEligibility.None);
    }

    public static RunHistoryItemViewModel From(
        ProjectRun run,
        ProjectRecoveryEligibility eligibility)
    {
        ArgumentNullException.ThrowIfNull(run);
        ArgumentNullException.ThrowIfNull(eligibility);
        var projection = ExecutionCenterViewModel.ProjectStatus(run.Status);
        var lastAttempt = run.Attempts.LastOrDefault();
        return new RunHistoryItemViewModel(
            run.Id,
            projection.Label,
            projection.Glyph,
            run.CurrentStepId,
            eligibility.CanResume,
            eligibility.CanRetry,
            eligibility.CanCleanup,
            run.Status == RunStatus.PublishPending,
            SupportBundleRequest.Create(run.Id, includeEnvironmentSnapshot: true).IsValid,
            run.Errors.LastOrDefault()?.Code ?? lastAttempt?.Error?.Code);
    }
}

public sealed partial class RunHistoryViewModel : ObservableObject
{
    private readonly IRunCheckpointStore _store;
    private readonly RunHistoryActionCoordinator _actions;
    private readonly ExecutionCenterViewModel _executionCenter;
    private readonly ILocalReadyService _localReadyService;
    private bool _isReadOnly;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanCopySupportBundleReceipt))]
    private SupportBundleReceipt? _lastSupportBundleReceipt;

    [ObservableProperty]
    private ImmutableArray<RunHistoryItemViewModel> _items = [];

    [ObservableProperty]
    private bool _isBusy;

    public RunHistoryViewModel(
        IRunCheckpointStore store,
        RunHistoryActionCoordinator actions,
        ExecutionCenterViewModel executionCenter,
        ILocalReadyService localReadyService)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _actions = actions ?? throw new ArgumentNullException(nameof(actions));
        _executionCenter = executionCenter ?? throw new ArgumentNullException(nameof(executionCenter));
        _localReadyService = localReadyService ?? throw new ArgumentNullException(nameof(localReadyService));
        RefreshCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        ResumeCommand = new AsyncRelayCommand<RunHistoryItemViewModel>(
            (item, token) => ContinueAsync(item, ExecutionMode.Resume, token),
            item => !IsBusy && item?.CanResume == true);
        RetryCommand = new AsyncRelayCommand<RunHistoryItemViewModel>(
            (item, token) => ContinueAsync(item, ExecutionMode.ManualRetry, token),
            item => !IsBusy && item?.CanRetry == true);
        CleanupCommand = new AsyncRelayCommand<RunHistoryItemViewModel>(
            CleanupAsync,
            item => !IsBusy && item?.CanCleanup == true);
        RetryPublishCommand = new AsyncRelayCommand<RunHistoryItemViewModel>(
            RetryPublishAsync,
            item => !IsBusy && !_isReadOnly && item?.CanRetryPublish == true);
        CreateSupportBundleCommand = new AsyncRelayCommand<RunHistoryItemViewModel>(
            CreateSupportBundleAsync,
            item => !IsBusy
                && _actions.CanExportSupportBundle
                && item?.CanCreateSupportBundle == true);
        CopySupportBundleReceiptCommand = new RelayCommand(
            CopySupportBundleReceipt,
            () => CanCopySupportBundleReceipt);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IAsyncRelayCommand<RunHistoryItemViewModel> ResumeCommand { get; }

    public IAsyncRelayCommand<RunHistoryItemViewModel> RetryCommand { get; }

    public IAsyncRelayCommand<RunHistoryItemViewModel> CleanupCommand { get; }

    public IAsyncRelayCommand<RunHistoryItemViewModel> RetryPublishCommand { get; }

    public IAsyncRelayCommand<RunHistoryItemViewModel> CreateSupportBundleCommand { get; }

    public IRelayCommand CopySupportBundleReceiptCommand { get; }

    public bool CanCopySupportBundleReceipt => !IsBusy && LastSupportBundleReceipt is not null;

    public void EnterReadOnlyMode()
    {
        _isReadOnly = true;
        _actions.EnterReadOnlyMode();
        RetryPublishCommand.NotifyCanExecuteChanged();
        CreateSupportBundleCommand.NotifyCanExecuteChanged();
    }

    public event EventHandler? ExecutionOpened;

    public object? OpenedPage { get; private set; }

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
            var items = new List<RunHistoryItemViewModel>();
            foreach (var checkpoint in checkpoints.OrderBy(item => item.Run.Id, StringComparer.Ordinal))
            {
                var eligibility = await _actions.InspectAsync(
                    checkpoint,
                    cancellationToken).ConfigureAwait(true);
                items.Add(RunHistoryItemViewModel.From(checkpoint.Run, eligibility));
            }

            Items = [.. items];
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
        ResumeCommand.NotifyCanExecuteChanged();
        RetryCommand.NotifyCanExecuteChanged();
        CleanupCommand.NotifyCanExecuteChanged();
        RetryPublishCommand.NotifyCanExecuteChanged();
        CreateSupportBundleCommand.NotifyCanExecuteChanged();
        CopySupportBundleReceiptCommand.NotifyCanExecuteChanged();
    }

    private async Task ContinueAsync(
        RunHistoryItemViewModel? item,
        ExecutionMode mode,
        CancellationToken cancellationToken)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var recovered = await _actions.ContinueAsync(
                item.RunId,
                mode,
                cancellationToken).ConfigureAwait(true);
            var eligibility = await _actions.InspectAsync(
                recovered.Checkpoint,
                cancellationToken).ConfigureAwait(true);
            _executionCenter.ApplyRecovered(
                recovered.PlannedProject,
                recovered.Checkpoint,
                eligibility);
            OpenedPage = recovered.Checkpoint.Run.Status == RunStatus.LocalReady
                ? new LocalReadyViewModel(
                    recovered.Checkpoint,
                    recovered.PlannedProject.Preview,
                    _localReadyService)
                : _executionCenter;
            ExecutionOpened?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CleanupAsync(
        RunHistoryItemViewModel? item,
        CancellationToken cancellationToken)
    {
        if (item is null || IsBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await _actions.CleanupAsync(item.RunId, cancellationToken).ConfigureAwait(true);
            var checkpoints = await _store.ListAsync(cancellationToken).ConfigureAwait(true);
            var items = new List<RunHistoryItemViewModel>();
            foreach (var checkpoint in checkpoints.OrderBy(value => value.Run.Id, StringComparer.Ordinal))
            {
                items.Add(RunHistoryItemViewModel.From(
                    checkpoint.Run,
                    await _actions.InspectAsync(checkpoint, cancellationToken).ConfigureAwait(true)));
            }

            Items = [.. items];
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task RetryPublishAsync(
        RunHistoryItemViewModel? item,
        CancellationToken cancellationToken)
    {
        if (item is null || IsBusy || _isReadOnly)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var outcome = await _actions.RetryPublicationAsync(
                item.RunId,
                cancellationToken).ConfigureAwait(true);
            if (!outcome.IsSuccessful)
            {
                return;
            }

            var checkpoint = outcome.Value.Checkpoint;
            if (checkpoint.Preview is null)
            {
                return;
            }

            OpenedPage = new LocalReadyViewModel(
                checkpoint,
                checkpoint.Preview,
                _localReadyService,
                _actions.PublicationWorkflow,
                _isReadOnly,
                outcome.Value.Error);
            ExecutionOpened?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private async Task CreateSupportBundleAsync(
        RunHistoryItemViewModel? item,
        CancellationToken cancellationToken)
    {
        if (item is null || IsBusy || !item.CanCreateSupportBundle
            || !_actions.CanExportSupportBundle)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var result = await _actions.ExportSupportBundleAsync(
                item.RunId,
                cancellationToken).ConfigureAwait(true);
            if (result.IsSuccessful)
            {
                LastSupportBundleReceipt = result.Value;
            }
        }
        finally
        {
            SetBusy(false);
        }
    }

    private void CopySupportBundleReceipt()
    {
        if (!IsBusy && LastSupportBundleReceipt is not null)
        {
            _actions.CopySupportBundleReceipt(LastSupportBundleReceipt);
        }
    }

    partial void OnLastSupportBundleReceiptChanged(SupportBundleReceipt? value) =>
        CopySupportBundleReceiptCommand.NotifyCanExecuteChanged();
}
