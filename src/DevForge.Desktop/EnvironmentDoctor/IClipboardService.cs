namespace DevForge.Desktop.EnvironmentDoctor;

public interface IClipboardService
{
    void SetText(string text);
}

public sealed class WindowsClipboardService : IClipboardService
{
    public void SetText(string text)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);
        System.Windows.Clipboard.SetText(text);
    }
}
