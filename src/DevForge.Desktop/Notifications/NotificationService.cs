using System.Collections.ObjectModel;
using DevForge.Domain.Privacy;

namespace DevForge.Desktop.Notifications;

public enum NotificationSeverity
{
    Information = 1,
    Warning = 2,
    Error = 3,
}

public sealed record UserNotification(NotificationSeverity Severity, string Message);

public sealed class NotificationService
{
    private const int Capacity = 20;
    private const int MaxMessageLength = 256;
    private readonly ObservableCollection<UserNotification> _items = [];

    public ReadOnlyObservableCollection<UserNotification> Items { get; }

    public NotificationService()
    {
        Items = new ReadOnlyObservableCollection<UserNotification>(_items);
    }

    public bool TryPublish(NotificationSeverity severity, string? message)
    {
        if (!Enum.IsDefined(severity)
            || string.IsNullOrWhiteSpace(message)
            || message.Length > MaxMessageLength
            || message.Any(char.IsControl))
        {
            return false;
        }

        var redacted = RedactedText.FromTrustedRedaction(message);
        if (!redacted.IsValid)
        {
            return false;
        }

        if (_items.Count == Capacity)
        {
            _items.RemoveAt(0);
        }

        _items.Add(new UserNotification(severity, redacted.Value.Value));
        return true;
    }
}
