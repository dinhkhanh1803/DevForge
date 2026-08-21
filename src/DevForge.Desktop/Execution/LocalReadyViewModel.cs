using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Application.Contracts;
using DevForge.Domain.Diagnostics;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

namespace DevForge.Desktop.Execution;

public sealed record LocalReadyEvidenceItem(
    ExecutionEvidenceKind Kind,
    string Id,
    ExecutionEvidenceStatus Status)
{
    public string DisplayText => $"{Kind} | {Id} | {Status}";
}

public sealed partial class LocalReadyViewModel : ObservableObject
{
    private readonly ILocalReadyService _localReadyService;
    private readonly IProjectPublicationWorkflow? _publicationWorkflow;
    private readonly bool _isReadOnly;
    private RunCheckpoint _checkpoint;

    [ObservableProperty]
    private string? _ideErrorMessage;

    [ObservableProperty]
    private string? _publicationErrorMessage;

    [ObservableProperty]
    private bool _isPublishing;

    public LocalReadyViewModel(
        ProjectCreationExecutionSnapshot snapshot,
        ILocalReadyService localReadyService,
        IProjectPublicationWorkflow? publicationWorkflow = null,
        bool isReadOnly = false,
        DevForgeError? publicationError = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Checkpoint.Run.Status is not (RunStatus.LocalReady
                or RunStatus.PublishPending
                or RunStatus.Completed))
        {
            throw new ArgumentException(
                "A LocalReady checkpoint is required.",
                nameof(snapshot));
        }

        Snapshot = snapshot;
        _checkpoint = snapshot.Checkpoint;
        _localReadyService = localReadyService ?? throw new ArgumentNullException(nameof(localReadyService));
        _publicationWorkflow = publicationWorkflow;
        _isReadOnly = isReadOnly;
        PublicationErrorMessage = CreatePublicationMessage(publicationError, snapshot.Plan.PlannedProject.Preview.Git.PublishToGitHub);
        var presentation = _localReadyService.Describe(_checkpoint);
        TargetDisplayPath = presentation.TargetDisplayPath;
        ReportReferences = presentation.ReportReferences;
        Warnings = snapshot.Plan.PlannedProject.Preview.Warnings;
        Evidence = [.. _checkpoint.Evidence.Select(item =>
            new LocalReadyEvidenceItem(item.Kind, item.Id, item.Status))];
        OpenIdeCommand = new AsyncRelayCommand(OpenSelectedIdeAsync, () => CanOpenIde);
        RetryPublishCommand = new AsyncRelayCommand(RetryPublishAsync, () => CanRetryPublish);
    }

    public LocalReadyViewModel(
        RunCheckpoint checkpoint,
        PlanPreview preview,
        ILocalReadyService localReadyService,
        IProjectPublicationWorkflow? publicationWorkflow = null,
        bool isReadOnly = false,
        DevForgeError? publicationError = null)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(preview);
        if (checkpoint.Run.Status is not (RunStatus.LocalReady or RunStatus.PublishPending or RunStatus.Completed)
            || !StringComparer.Ordinal.Equals(checkpoint.PlanHash, preview.PlanHash))
        {
            throw new ArgumentException("Matching LocalReady evidence is required.", nameof(checkpoint));
        }

        _checkpoint = checkpoint;
        _localReadyService = localReadyService ?? throw new ArgumentNullException(nameof(localReadyService));
        _publicationWorkflow = publicationWorkflow;
        _isReadOnly = isReadOnly;
        PublicationErrorMessage = CreatePublicationMessage(publicationError, preview.Git.PublishToGitHub);
        var presentation = _localReadyService.Describe(_checkpoint);
        TargetDisplayPath = presentation.TargetDisplayPath;
        ReportReferences = presentation.ReportReferences;
        Warnings = preview.Warnings;
        Evidence = [.. _checkpoint.Evidence.Select(item =>
            new LocalReadyEvidenceItem(item.Kind, item.Id, item.Status))];
        OpenIdeCommand = new AsyncRelayCommand(OpenSelectedIdeAsync, () => CanOpenIde);
        RetryPublishCommand = new AsyncRelayCommand(RetryPublishAsync, () => CanRetryPublish);
    }

    public ProjectCreationExecutionSnapshot Snapshot { get; } = null!;

    public RunCheckpoint Checkpoint => _checkpoint;

    public string StatusLabel => ExecutionCenterViewModel.ProjectStatus(_checkpoint.Run.Status).Label;

    public bool IsDomainCompleted => _checkpoint.Run.Status == RunStatus.Completed;

    public FinalizationState FinalizationState => _checkpoint.FinalizationState;

    public ReportPersistenceState ReportState => _checkpoint.ReportState;

    public ImmutableArray<ValidationIssue> Warnings { get; }

    public string TargetDisplayPath { get; }

    public string PlanHash => _checkpoint.PlanHash;

    public string BlueprintLabel => $"{_checkpoint.Blueprint.Id} {_checkpoint.Blueprint.Version}";

    public ImmutableArray<string> ReportReferences { get; }

    public ImmutableArray<LocalReadyEvidenceItem> Evidence { get; }

    public TimeSpan Elapsed => _checkpoint.Run.Attempts
        .Where(item => item.CompletedAt is not null)
        .Aggregate(TimeSpan.Zero, (total, item) =>
            total + (item.CompletedAt!.Value - item.StartedAt));

    public string? SelectedIdeId => Snapshot?.Plan.Draft.IdeId
        ?? _checkpoint.Preview?.Completion.IdeId;

    public bool CanOpenIde => !string.IsNullOrWhiteSpace(SelectedIdeId)
        && !StringComparer.Ordinal.Equals(SelectedIdeId, "none");

    public bool CanRetryPublish => _checkpoint.Run.Status == RunStatus.PublishPending
        && _publicationWorkflow is not null
        && !_isReadOnly
        && !IsPublishing;

    public string PublicationRemediation => _checkpoint.Run.Status == RunStatus.PublishPending
        ? _checkpoint.Preview?.Git.PublishToGitHub == true
            ? "The local project is safe. Verify GitHub CLI authentication with gh auth status or gh auth login, then retry publication."
            : "The local project is safe. Resolve the local Git issue, then retry publication."
        : string.Empty;

    public string? InitialCommitId => _checkpoint.Publication.InitialCommitId;

    public ImmutableArray<string> Branches => _checkpoint.Publication.Branches;

    public string? RepositoryUrl => _checkpoint.Publication.RepositoryUrl;

    public ImmutableArray<string> PublicationReceiptReferences =>
        _checkpoint.Publication.ReceiptReference is null
            ? []
            : [_checkpoint.Publication.ReceiptReference];

    public IAsyncRelayCommand OpenIdeCommand { get; }

    public IAsyncRelayCommand RetryPublishCommand { get; }

    public event EventHandler? CheckpointChanged;

    private async Task RetryPublishAsync(CancellationToken cancellationToken)
    {
        if (!CanRetryPublish)
        {
            return;
        }

        IsPublishing = true;
        RetryPublishCommand.NotifyCanExecuteChanged();
        try
        {
            var result = await _publicationWorkflow!.CompleteAsync(
                _checkpoint.Run.Id,
                PublicationMutationMode.Normal,
                cancellationToken).ConfigureAwait(true);
            if (!result.IsSuccessful)
            {
                PublicationErrorMessage = "Publication could not be resumed.";
                return;
            }

            _checkpoint = result.Value.Checkpoint;
            PublicationErrorMessage = CreatePublicationMessage(
                result.Value.Error,
                _checkpoint.Preview?.Git.PublishToGitHub == true);
            NotifyCheckpointProjectionChanged();
            CheckpointChanged?.Invoke(this, EventArgs.Empty);
        }
        finally
        {
            IsPublishing = false;
            RetryPublishCommand.NotifyCanExecuteChanged();
        }
    }

    private async Task OpenSelectedIdeAsync(CancellationToken cancellationToken)
    {
        if (!CanOpenIde)
        {
            return;
        }

        try
        {
            await _localReadyService.OpenIdeAsync(
                _checkpoint,
                SelectedIdeId!,
                cancellationToken).ConfigureAwait(true);
            IdeErrorMessage = null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            IdeErrorMessage = "IDE could not be opened.";
        }
    }

    private void NotifyCheckpointProjectionChanged()
    {
        OnPropertyChanged(nameof(StatusLabel));
        OnPropertyChanged(nameof(IsDomainCompleted));
        OnPropertyChanged(nameof(CanRetryPublish));
        OnPropertyChanged(nameof(PublicationRemediation));
        OnPropertyChanged(nameof(InitialCommitId));
        OnPropertyChanged(nameof(Branches));
        OnPropertyChanged(nameof(RepositoryUrl));
        OnPropertyChanged(nameof(PublicationReceiptReferences));
        RetryPublishCommand.NotifyCanExecuteChanged();
    }

    private static string? CreatePublicationMessage(DevForgeError? error, bool githubRequested)
    {
        if (error is null)
        {
            return null;
        }

        return githubRequested
            ? "GitHub publication is pending. The local project remains available."
            : "Git completion is pending. The local project remains available.";
    }
}
