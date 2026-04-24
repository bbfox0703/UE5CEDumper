using System.Globalization;
using Avalonia.Data.Converters;

namespace UE5DumpUI.Converters;

/// <summary>
/// Multiplies a double by a factor supplied via ConverterParameter (invariant culture).
/// </summary>
public sealed class MultiplyConverter : IValueConverter
{
    public static readonly MultiplyConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not double d) return value;
        double factor = 1.0;
        if (parameter is string s && double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
            factor = f;
        else if (parameter is double pd) factor = pd;
        return d * factor;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
