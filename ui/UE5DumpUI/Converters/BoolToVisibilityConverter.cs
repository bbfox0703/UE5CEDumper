using System.Globalization;
using Avalonia.Data.Converters;

namespace UE5DumpUI.Converters;

/// <summary>
/// Converts bool to IsVisible.
/// </summary>
public sealed class BoolToVisibilityConverter : IValueConverter
{
    public static readonly BoolToVisibilityConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        bool invert = parameter is string s && s == "invert";
        bool visible = value is true;
        return invert ? !visible : visible;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }
}
