using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using DevForge.Application.Contracts;
using DevForge.Domain.Runs;
using DevForge.Domain.Validation;

namespace DevForge.Desktop.Execution;

public sealed partial class LocalReadyViewModel : ObservableObject
{
    private readonly IIdeLauncher _ideLauncher;

    [ObservableProperty]
    private string? _ideErrorMessage;

    public LocalReadyViewModel(
        ProjectCreationExecutionSnapshot snapshot,
        IIdeLauncher ideLauncher)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (snapshot.Checkpoint.Run.Status != RunStatus.LocalReady)
        {
            throw new ArgumentException(
                "A LocalReady checkpoint is required.",
                nameof(snapshot));
        }

        Snapshot = snapshot;
        _ideLauncher = ideLauncher ?? throw new ArgumentNullException(nameof(ideLauncher));
        Warnings = snapshot.Plan.PlannedProject.Preview.Warnings;
    }

    public ProjectCreationExecutionSnapshot Snapshot { get; }

    public string StatusLabel => Snapshot.Checkpoint.Run.Status == RunStatus.LocalReady
        ? "LOCAL PROJECT READY"
        : "UNAVAILABLE";

    public bool IsDomainCompleted => Snapshot.Checkpoint.Run.Status == RunStatus.Completed;

    public FinalizationState FinalizationState => Snapshot.Checkpoint.FinalizationState;

    public ReportPersistenceState ReportState => Snapshot.Checkpoint.ReportState;

    public ImmutableArray<ValidationIssue> Warnings { get; }

    public async Task OpenIdeAsync(
        IWorkspaceFileSystem workspace,
        string ideId,
        CancellationToken cancellationToken)
    {
        var request = IdeLaunchRequest.Create(workspace, ideId);
        if (!request.IsValid)
        {
            IdeErrorMessage = "IDE could not be opened.";
            return;
        }

        try
        {
            await _ideLauncher.LaunchAsync(request.Value, cancellationToken).ConfigureAwait(true);
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
