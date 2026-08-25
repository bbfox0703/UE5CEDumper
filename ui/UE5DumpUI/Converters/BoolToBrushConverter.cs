using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;

namespace UE5DumpUI.Converters;

/// <summary>
/// Picks one of two brushes from a bool, for a <c>Style</c> setter that has to react to a property
/// on the bound item. [LWREFRESH-2026-08-21]
///
/// <para><b>Why this exists rather than the imperative painter it replaces.</b> Live Walker used to
/// tint its rows from <c>FieldGrid_LoadingRow</c>, which runs only when a row is <i>realized</i>.
/// That was fine while every refresh replaced the row objects — and it is exactly what stopped
/// being fine when the refresh started reusing them to keep the grid from scrolling. A realization
/// hook cannot see a property change on a row that was never re-realized.</para>
///
/// <para>⚠ The old handler could not simply be left in place as a fallback:
/// <c>e.Row.Background = …</c> writes at <b>LocalValue</b> priority, which outranks a
/// <c>Style</c> setter, so even its <c>Transparent</c> branch would have pinned every non-matching
/// row and the binding would never have shown. It had to be deleted, and was.</para>
///
/// <para>Colours are passed as <c>ConverterParameter="&lt;whenTrue&gt;|&lt;whenFalse&gt;"</c> so the
/// two call sites keep their own palette in the AXAML beside everything else, instead of hiding
/// UI colours in a converter.</para>
/// </summary>
public sealed class BoolToBrushConverter : IValueConverter
{
    public static readonly BoolToBrushConverter Instance = new();

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (parameter is not string spec) return null;
        var bar = spec.IndexOf('|');
        if (bar < 0) return null;

        var pick = value is true ? spec[..bar] : spec[(bar + 1)..];
        // "none" rather than an empty segment: an empty string parses to nothing useful and would
        // silently fall through to null, which reads on screen as "the binding is broken".
        if (pick.Equals("none", StringComparison.OrdinalIgnoreCase)) return Brushes.Transparent;

        return Color.TryParse(pick, out var c) ? new SolidColorBrush(c) : null;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
