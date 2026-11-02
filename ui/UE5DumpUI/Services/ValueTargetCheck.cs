using System.Globalization;
using UE5DumpUI.Helpers;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// [VALUESCAN-NONFINITE-TARGET] What a numeric scan target must be before either matcher sees it. Infinity, NaN or a
/// number past double's range (1e400) gave different answers live and in the snapshot search: the DLL's stod throws
/// on "1e400" and its float leaves take an infinity that GroupMatch refuses, while .NET reads "1e400" as +Infinity.
/// Nonsense input on both sides, so it is refused where it is typed.
/// </summary>
public static class ValueTargetCheck
{
    internal const string KeyNonFinite = "str.ValueSearch.NonFiniteTarget";

    /// <summary>The refusal for the first value of <paramref name="values"/> a numeric scan of
    /// <paramref name="dataType"/> cannot take, or null when every one is fine. Only a value that reads as a number
    /// and is not a finite one is refused; anything else is the matchers' to judge, as before.</summary>
    public static string? NonFiniteError(ValueScanDataType dataType, params string?[] values)
    {
        if (!IsNumeric(dataType)) return null;
        foreach (var v in values)
        {
            if (string.IsNullOrWhiteSpace(v)) continue;
            var t = v.Trim();
            if (t is "∞" or "+∞" or "-∞"
                || (double.TryParse(t, NumberStyles.Float | NumberStyles.AllowThousands, CultureInfo.InvariantCulture,
                                    out var d) && !double.IsFinite(d)))
                return AobMakerStatus.Say(KeyNonFinite,
                    "⚠ \"{0}\" is not a finite number: a scan target must be one.", t);
        }
        return null;
    }

    /// <summary>The refusal for a scan target as its scan type sends it.</summary>
    public static string? NonFiniteTargetError(ValueScanDataType dataType, ValueScanType scanType, string? value,
                                               string? value2)
        => NonFiniteError(dataType, value, value2);

    private static bool IsNumeric(ValueScanDataType dt) => dt is
        ValueScanDataType.Int8 or ValueScanDataType.Int16 or ValueScanDataType.Int32 or ValueScanDataType.Int64 or
        ValueScanDataType.UInt8 or ValueScanDataType.UInt16 or ValueScanDataType.UInt32 or ValueScanDataType.UInt64 or
        ValueScanDataType.Float or ValueScanDataType.Double or
        ValueScanDataType.NumericNoByte or ValueScanDataType.NumericAll;
}
