using System.Collections.Immutable;
using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Application.Contracts;
using DevForge.Blueprints.Abstractions.Models;
using DevForge.Domain.Validation;

namespace DevForge.Desktop.CreateProject;

public sealed partial class CreateProjectViewModel : ObservableObject
{
    private readonly IProjectCreationWorkflow _workflow;

    [ObservableProperty]
    private string? _name;

    [ObservableProperty]
    private string? _rootPath;

    [ObservableProperty]
    private string? _outputFolder;

    [ObservableProperty]
    private string _ideId = "none";

    [ObservableProperty]
    private ResolvedBlueprint? _selectedBlueprint;

    [ObservableProperty]
    private ProjectCreationStage _stage = ProjectCreationStage.Configure;

    [ObservableProperty]
    private ProjectCreationPlanSnapshot? _reviewedPlan;

    [ObservableProperty]
    private ImmutableArray<ValidationIssue> _validationIssues = [];

    [ObservableProperty]
    private bool _isBusy;

    public CreateProjectViewModel(IProjectCreationWorkflow workflow)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        LoadCommand = new AsyncRelayCommand(LoadAsync, () => !IsBusy);
        ReviewPlanCommand = new AsyncRelayCommand(ReviewPlanAsync, () => !IsBusy);
    }

    public ImmutableArray<ResolvedBlueprint> Blueprints { get; private set; } = [];

    public ObservableCollection<DynamicInputViewModel> Inputs { get; } = [];

    public ImmutableArray<string> IdeChoices { get; } =
        ["none", "vscode", "visual-studio", "rider", "unity"];

    public IAsyncRelayCommand LoadCommand { get; }

    public IAsyncRelayCommand ReviewPlanCommand { get; }

    public async Task LoadAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var catalog = await _workflow.LoadCatalogAsync(
                forceRefresh: false,
                cancellationToken).ConfigureAwait(true);
            Blueprints = catalog.ExecutableBlueprints;
            OnPropertyChanged(nameof(Blueprints));
            SelectedBlueprint ??= Blueprints.FirstOrDefault();
        }
        finally
        {
            SetBusy(false);
        }
    }

    public async Task ReviewPlanAsync(CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var inputValues = new Dictionary<string, DynamicInputValue?>(StringComparer.Ordinal);
            var issues = new List<ValidationIssue>();
            foreach (var input in Inputs)
            {
                if (!input.HasValue && !input.IsRequired)
                {
                    continue;
                }

                var value = input.BuildValue();
                if (value.IsValid)
                {
                    inputValues.Add(input.Id, value.Value);
                }
                else
                {
                    issues.AddRange(value.Issues);
                }
            }

            var reference = SelectedBlueprint is null
                ? null
                : BlueprintReference.Create(
                    SelectedBlueprint.Manifest.Id,
                    SelectedBlueprint.Manifest.Version).Value;
            var draft = ProjectCreationDraft.Create(
                Name,
                RootPath,
                OutputFolder,
                reference,
                inputValues,
                [],
                IdeId);
            if (!draft.IsValid)
            {
                issues.AddRange(draft.Issues);
            }

            if (issues.Count != 0)
            {
                ValidationIssues = [.. issues];
                return;
            }

            var planned = await _workflow.CreatePlanAsync(
                draft.Value,
                cancellationToken).ConfigureAwait(true);
            if (!planned.IsValid)
            {
                ValidationIssues = planned.Issues;
                return;
            }

            ReviewedPlan = planned.Value;
            ValidationIssues = [];
            Stage = ProjectCreationStage.ReviewPlan;
        }
        finally
        {
            SetBusy(false);
        }
    }

    partial void OnSelectedBlueprintChanged(ResolvedBlueprint? value)
    {
        foreach (var input in Inputs)
        {
            input.ValueChanged -= OnInputValueChanged;
        }

        Inputs.Clear();
        if (value is not null)
        {
            foreach (var definition in value.InputSchema)
            {
                var input = new DynamicInputViewModel(definition);
                input.ValueChanged += OnInputValueChanged;
                Inputs.Add(input);
            }
        }

        InvalidatePlan();
    }

    partial void OnNameChanged(string? value) => InvalidatePlan();

    partial void OnRootPathChanged(string? value) => InvalidatePlan();

    partial void OnOutputFolderChanged(string? value) => InvalidatePlan();

    partial void OnIdeIdChanged(string value) => InvalidatePlan();

    private void OnInputValueChanged(object? sender, EventArgs args) => InvalidatePlan();

    private void InvalidatePlan()
    {
        ReviewedPlan = null;
        ValidationIssues = [];
        Stage = ProjectCreationStage.Configure;
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        LoadCommand.NotifyCanExecuteChanged();
        ReviewPlanCommand.NotifyCanExecuteChanged();
    }
}
