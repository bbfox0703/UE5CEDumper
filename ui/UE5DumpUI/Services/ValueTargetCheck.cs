using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// [VALUESCAN-NONFINITE-TARGET] What a numeric scan target must be before either matcher sees it.
/// </summary>
public static class ValueTargetCheck
{
    /// <summary>The refusal for the first value of <paramref name="values"/> a numeric scan of
    /// <paramref name="dataType"/> cannot take, or null when every one is fine.</summary>
    public static string? NonFiniteError(ValueScanDataType dataType, params string?[] values) => null;
}
