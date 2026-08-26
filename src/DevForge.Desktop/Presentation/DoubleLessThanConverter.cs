using System.Globalization;
using System.Windows.Data;

namespace DevForge.Desktop.Presentation;

public sealed class DoubleLessThanConverter : IValueConverter
{
    public object Convert(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture)
    {
        return value is double actual
            && parameter is string text
            && double.TryParse(
                text,
                NumberStyles.Float,
                CultureInfo.InvariantCulture,
                out var threshold)
            && actual < threshold;
    }

    public object ConvertBack(
        object? value,
        Type targetType,
        object? parameter,
        CultureInfo culture) =>
        Binding.DoNothing;
}
