using System.Collections.Immutable;
using System.Globalization;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Execution;
using DevForge.Domain.Projects;
using DevForge.Domain.Validation;

namespace DevForge.Desktop.CreateProject;

public sealed record PlanPreviewStepItem(
    string Id,
    string HandlerId,
    TimeSpan Timeout,
    string? ProcessPreview);

public sealed record PlanPreviewInputItem(string Id, string Kind, string DisplayValue);

public sealed partial class PlanPreviewViewModel : ObservableObject
{
    private readonly Func<ProjectCreationPlanSnapshot, CancellationToken, Task> _createAndValidate;
    private readonly Action _backToConfigure;

    [ObservableProperty]
    private bool _isBusy;

    public PlanPreviewViewModel(
        ProjectCreationPlanSnapshot snapshot,
        Func<ProjectCreationPlanSnapshot, CancellationToken, Task> createAndValidate,
        Action backToConfigure)
    {
        Snapshot = snapshot ?? throw new ArgumentNullException(nameof(snapshot));
        _createAndValidate = createAndValidate ?? throw new ArgumentNullException(nameof(createAndValidate));
        _backToConfigure = backToConfigure ?? throw new ArgumentNullException(nameof(backToConfigure));
        var preview = snapshot.PlannedProject.Preview;
        Artifacts = preview.Artifacts;
        Dependencies = preview.Dependencies;
        Tools = preview.ToolStatuses;
        Steps =
        [
            .. preview.Steps.Select(item => new PlanPreviewStepItem(
                item.Id,
                item.HandlerId,
                item.Timeout,
                item.ProcessPreview?.Value)),
        ];
        Validators =
        [
            .. preview.Validators.Select(item => new PlanPreviewStepItem(
                item.Id,
                item.HandlerId,
                item.Timeout,
                item.ProcessPreview?.Value)),
        ];
        Inputs =
        [
            .. preview.EffectiveInputs.Select(item => new PlanPreviewInputItem(
                item.Key,
                item.Value.Kind.ToString(),
                FormatValue(item.Value))),
        ];
        Features = preview.EnabledFeatures;
        Warnings = preview.Warnings;
        CreateAndValidateCommand = new AsyncRelayCommand(
            CreateAndValidateAsync,
            () => !IsBusy);
        BackToConfigureCommand = new RelayCommand(
            _backToConfigure,
            () => !IsBusy);
    }

    public ProjectCreationPlanSnapshot Snapshot { get; }

    public string PlanHash => Snapshot.PlannedProject.Preview.PlanHash;

    public string BlueprintLabel =>
        $"{Snapshot.Draft.Blueprint.Id} {Snapshot.Draft.Blueprint.Version}";

    public string TrustLabel => Snapshot.PlannedProject.BlueprintFingerprint.Trust.ToString();

    public bool GitEnabled => Snapshot.PlannedProject.Preview.Git.InitializeRepository;

    public string GitSummary => GitEnabled
        ? Snapshot.PlannedProject.Preview.Git.BranchPolicy == GitBranchPolicy.MainAndDevelop
            ? "Initialize Git with main + develop"
            : "Initialize Git with main"
        : "Git disabled";

    public string GitHubSummary
    {
        get
        {
            var git = Snapshot.PlannedProject.Preview.Git;
            return git.PublishToGitHub
                ? $"Publish github.com/{git.GitHubAccount}/{git.GitHubRepository}"
                : "Not requested";
        }
    }

    public string RepositoryVisibility =>
        Snapshot.PlannedProject.Preview.Git.IsPrivate ? "Private" : "Public";

    public ImmutableArray<BlueprintArtifact> Artifacts { get; }

    public ImmutableArray<BlueprintDependency> Dependencies { get; }

    public ImmutableArray<PlanPreviewToolStatus> Tools { get; }

    public ImmutableArray<PlanPreviewStepItem> Steps { get; }

    public ImmutableArray<PlanPreviewStepItem> Validators { get; }

    public ImmutableArray<PlanPreviewInputItem> Inputs { get; }

    public ImmutableArray<string> Features { get; }

    public ImmutableArray<ValidationIssue> Warnings { get; }

    public IAsyncRelayCommand CreateAndValidateCommand { get; }

    public IRelayCommand BackToConfigureCommand { get; }

    public async Task CreateAndValidateAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            await _createAndValidate(Snapshot, cancellationToken).ConfigureAwait(true);
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string FormatValue(PlanValue value)
    {
        return value.Kind switch
        {
            PlanValueKind.Text => value.StringValue!,
            PlanValueKind.Boolean => value.BooleanValue ? "true" : "false",
            PlanValueKind.WholeNumber => value.IntegerValue.ToString(CultureInfo.InvariantCulture),
            PlanValueKind.Sequence => $"[{value.ArrayValue.Length} items]",
            PlanValueKind.Map => $"{{{value.ObjectValue.Count} items}}",
            _ => "[unsupported]",
        };
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        CreateAndValidateCommand.NotifyCanExecuteChanged();
        BackToConfigureCommand.NotifyCanExecuteChanged();
    }
}
