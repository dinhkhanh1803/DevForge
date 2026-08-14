using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Application.Contracts;
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
    private readonly RunCheckpoint _checkpoint;

    [ObservableProperty]
    private string? _ideErrorMessage;

    public LocalReadyViewModel(
        ProjectCreationExecutionSnapshot snapshot,
        ILocalReadyService localReadyService)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Checkpoint.Run.Status != RunStatus.LocalReady)
        {
            throw new ArgumentException(
                "A LocalReady checkpoint is required.",
                nameof(snapshot));
        }

        Snapshot = snapshot;
        _checkpoint = snapshot.Checkpoint;
        _localReadyService = localReadyService ?? throw new ArgumentNullException(nameof(localReadyService));
        var presentation = _localReadyService.Describe(_checkpoint);
        TargetDisplayPath = presentation.TargetDisplayPath;
        ReportReferences = presentation.ReportReferences;
        Warnings = snapshot.Plan.PlannedProject.Preview.Warnings;
        Evidence = [.. _checkpoint.Evidence.Select(item =>
            new LocalReadyEvidenceItem(item.Kind, item.Id, item.Status))];
        OpenIdeCommand = new AsyncRelayCommand(OpenSelectedIdeAsync, () => CanOpenIde);
    }

    public LocalReadyViewModel(
        RunCheckpoint checkpoint,
        PlanPreview preview,
        ILocalReadyService localReadyService)
    {
        ArgumentNullException.ThrowIfNull(checkpoint);
        ArgumentNullException.ThrowIfNull(preview);
        if (checkpoint.Run.Status != RunStatus.LocalReady
            || !StringComparer.Ordinal.Equals(checkpoint.PlanHash, preview.PlanHash))
        {
            throw new ArgumentException("Matching LocalReady evidence is required.", nameof(checkpoint));
        }

        _checkpoint = checkpoint;
        _localReadyService = localReadyService ?? throw new ArgumentNullException(nameof(localReadyService));
        var presentation = _localReadyService.Describe(_checkpoint);
        TargetDisplayPath = presentation.TargetDisplayPath;
        ReportReferences = presentation.ReportReferences;
        Warnings = preview.Warnings;
        Evidence = [.. _checkpoint.Evidence.Select(item =>
            new LocalReadyEvidenceItem(item.Kind, item.Id, item.Status))];
        OpenIdeCommand = new AsyncRelayCommand(OpenSelectedIdeAsync, () => CanOpenIde);
    }

    public ProjectCreationExecutionSnapshot Snapshot { get; } = null!;

    public string StatusLabel => _checkpoint.Run.Status == RunStatus.LocalReady
        ? "LOCAL PROJECT READY"
        : "UNAVAILABLE";

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

    public IAsyncRelayCommand OpenIdeCommand { get; }

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
}
