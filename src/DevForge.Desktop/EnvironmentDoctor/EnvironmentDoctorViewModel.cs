using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using DevForge.Desktop.Notifications;

namespace DevForge.Desktop.EnvironmentDoctor;

public sealed partial class EnvironmentDoctorViewModel : ObservableObject
{
    private const int MaxDiagnosticLength = 4_096;
    private readonly IEnvironmentDoctorService _doctorService;
    private readonly IClipboardService _clipboard;
    private readonly NotificationService _notifications;
    private readonly ObservableCollection<EnvironmentHealthItem> _tools = [];

    [ObservableProperty]
    private DateTimeOffset? _lastScannedAt;

    [ObservableProperty]
    private bool _isStale;

    [ObservableProperty]
    private bool _scanFailed;

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private bool _isReadOnly;

    public EnvironmentDoctorViewModel(
        IEnvironmentDoctorService doctorService,
        IClipboardService clipboard,
        NotificationService notifications)
    {
        _doctorService = doctorService ?? throw new ArgumentNullException(nameof(doctorService));
        _clipboard = clipboard ?? throw new ArgumentNullException(nameof(clipboard));
        _notifications = notifications ?? throw new ArgumentNullException(nameof(notifications));
        Tools = new ReadOnlyObservableCollection<EnvironmentHealthItem>(_tools);
        RescanCommand = new AsyncRelayCommand(RescanAsync, () => !IsBusy && !IsReadOnly);
        CopyDiagnosticsCommand = new RelayCommand(CopyDiagnostics, () => !IsBusy && _tools.Count != 0);
    }

    public ReadOnlyObservableCollection<EnvironmentHealthItem> Tools { get; }

    public IAsyncRelayCommand RescanCommand { get; }

    public IRelayCommand CopyDiagnosticsCommand { get; }

    public Task LoadAsync(CancellationToken cancellationToken)
    {
        return RefreshAsync(forceRefresh: false, cancellationToken);
    }

    public Task RescanAsync(CancellationToken cancellationToken)
    {
        return RefreshAsync(forceRefresh: true, cancellationToken);
    }

    public void EnterReadOnlyMode()
    {
        IsReadOnly = true;
        RescanCommand.NotifyCanExecuteChanged();
    }

    private async Task RefreshAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        if (IsBusy)
        {
            return;
        }

        SetBusy(true);
        try
        {
            var snapshot = await _doctorService.LoadAsync(forceRefresh, cancellationToken)
                .ConfigureAwait(true);
            ApplySnapshot(snapshot);
            if (snapshot.ScanFailed)
            {
                _notifications.TryPublish(
                    NotificationSeverity.Warning,
                    "Environment scan failed. The last cached results are shown.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            _notifications.TryPublish(
                NotificationSeverity.Error,
                "Environment health could not be loaded.");
        }
        finally
        {
            SetBusy(false);
        }
    }

    internal void ApplySnapshot(EnvironmentHealthSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        _tools.Clear();
        foreach (var tool in snapshot.Tools)
        {
            _tools.Add(tool);
        }

        LastScannedAt = snapshot.ScannedAt;
        IsStale = snapshot.IsStale;
        ScanFailed = snapshot.ScanFailed;
        CopyDiagnosticsCommand.NotifyCanExecuteChanged();
    }

    private void CopyDiagnostics()
    {
        try
        {
            _clipboard.SetText(CreateDiagnosticSummary());
            _notifications.TryPublish(NotificationSeverity.Information, "Diagnostics copied.");
        }
        catch (Exception)
        {
            _notifications.TryPublish(NotificationSeverity.Error, "Diagnostics could not be copied.");
        }
    }

    private string CreateDiagnosticSummary()
    {
        var builder = new StringBuilder(MaxDiagnosticLength);
        builder.AppendLine("DevForge Studio Environment Health");
        if (LastScannedAt is not null)
        {
            builder.Append("Scanned: ")
                .AppendLine(LastScannedAt.Value.ToString("O", CultureInfo.InvariantCulture));
        }

        foreach (var tool in _tools)
        {
            var line = string.Create(
                CultureInfo.InvariantCulture,
                $"{tool.Id} | {tool.StatusLabel} | {tool.Version ?? "unknown"}");
            if (builder.Length + line.Length + Environment.NewLine.Length > MaxDiagnosticLength)
            {
                break;
            }

            builder.AppendLine(line);
        }

        return builder.ToString().TrimEnd();
    }

    private void SetBusy(bool value)
    {
        IsBusy = value;
        RescanCommand.NotifyCanExecuteChanged();
        CopyDiagnosticsCommand.NotifyCanExecuteChanged();
    }
}
