using System.Globalization;
using Avalonia.Data.Converters;

namespace UE5DumpUI.Converters;

/// <summary>
/// Converts ulong address to "0x{addr:X}" hex string.
/// </summary>
public sealed class AddressToStringConverter : IValueConverter
{
    public static readonly AddressToStringConverter Instance = new();

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        return value switch
        {
            ulong addr => $"0x{addr:X}",
            long addr => $"0x{addr:X}",
            string s => s,
            _ => ""
        };
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is string s && s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            if (ulong.TryParse(s[2..], NumberStyles.HexNumber, null, out var addr))
                return addr;
        }
        return (ulong)0;
    }
}
