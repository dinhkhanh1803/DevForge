using System.Collections.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Application.Contracts;
using DevForge.Desktop.Navigation;

namespace DevForge.Desktop.BlueprintCatalog;

public sealed record BlueprintCatalogItemViewModel(
    string Id,
    string Version,
    string TrustLabel,
    bool CanCreate,
    string? Issue);

public sealed partial class BlueprintCatalogViewModel : ObservableObject
{
    private readonly IProjectCreationWorkflow _workflow;
    private readonly NavigationService _navigation;
    private readonly ProjectCreationSelection _selection;

    [ObservableProperty]
    private ImmutableArray<BlueprintCatalogItemViewModel> _items = [];

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isReadOnly;

    public BlueprintCatalogViewModel(
        IProjectCreationWorkflow workflow,
        NavigationService navigation,
        ProjectCreationSelection selection)
    {
        _workflow = workflow ?? throw new ArgumentNullException(nameof(workflow));
        _navigation = navigation ?? throw new ArgumentNullException(nameof(navigation));
        _selection = selection ?? throw new ArgumentNullException(nameof(selection));
        RefreshCommand = new AsyncRelayCommand(
            cancellationToken => LoadCoreAsync(forceRefresh: true, cancellationToken),
            () => !IsBusy && !IsReadOnly);
        CreateCommand = new RelayCommand<BlueprintCatalogItemViewModel>(
            SelectForCreation,
            item => item?.CanCreate == true);
    }

    public IAsyncRelayCommand RefreshCommand { get; }

    public IRelayCommand<BlueprintCatalogItemViewModel> CreateCommand { get; }

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        return LoadCoreAsync(forceRefresh: false, cancellationToken);
    }

    public void EnterReadOnlyMode()
    {
        IsReadOnly = true;
        RefreshCommand.NotifyCanExecuteChanged();
    }

    private async Task LoadCoreAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (IsBusy || (forceRefresh && IsReadOnly))
        {
            return;
        }

        SetBusy(true);
        try
        {
            var snapshot = await _workflow.LoadCatalogAsync(
                forceRefresh,
                cancellationToken).ConfigureAwait(true);
            var items = new List<BlueprintCatalogItemViewModel>();
            var executableKeys = new HashSet<string>(StringComparer.Ordinal);
            foreach (var blueprint in snapshot.ExecutableBlueprints)
            {
                var key = Key(blueprint.Manifest.Id, blueprint.Manifest.Version);
                executableKeys.Add(key);
                items.Add(new BlueprintCatalogItemViewModel(
                    blueprint.Manifest.Id,
                    blueprint.Manifest.Version,
                    blueprint.Fingerprint.Trust.ToString(),
                    CanCreate: true,
                    Issue: null));
            }

            foreach (var inspection in snapshot.Inspections)
            {
                var id = inspection.Reference?.Id ?? "unidentified";
                var version = inspection.Reference?.Version ?? "unknown";
                if (executableKeys.Contains(Key(id, version)))
                {
                    continue;
                }

                items.Add(new BlueprintCatalogItemViewModel(
                    id,
                    version,
                    inspection.Trust.ToString(),
                    CanCreate: false,
                    inspection.Issues.FirstOrDefault()?.Summary
                        ?? (inspection.IsDisabled ? "Package is disabled." : "Package is inspect-only.")));
            }

            Items =
            [
                .. items.OrderBy(item => item.Id, StringComparer.Ordinal)
                    .ThenBy(item => item.Version, StringComparer.Ordinal),
            ];
        }
        finally
        {
            SetBusy(false);
        }
    }

    private static string Key(string id, string version) => $"{id}\0{version}";

    private void SelectForCreation(BlueprintCatalogItemViewModel? item)
    {
        if (item?.CanCreate != true)
        {
            return;
        }

        var reference = BlueprintReference.Create(item.Id, item.Version);
        if (reference.IsValid)
        {
            _selection.Select(reference.Value);
            _navigation.TryNavigate(DesktopRoute.CreateProject);
        }
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        RefreshCommand.NotifyCanExecuteChanged();
    }
}
