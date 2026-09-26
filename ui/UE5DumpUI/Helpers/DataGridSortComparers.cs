using System.Collections;
using System.Collections.Generic;
using Avalonia.Controls;

namespace UE5DumpUI.Helpers;

/// <summary>
/// AOT-safe <see cref="IComparer"/> factories for DataGrid column sorting.
///
/// WHY THIS EXISTS (the three-layer sort trap, explained below):
/// Avalonia's default column sort reads each row's value by reflecting on
/// the column's <c>SortMemberPath</c> (<c>Type.GetProperty(path)</c>) and
/// even decides whether a column is sortable at all by reflecting the
/// property's type. Under Native-AOT/trim that reflection metadata is
/// dropped for any property a compiled <c>Binding</c> doesn't root —
/// notably every <c>DataGridTemplateColumn</c> (no column-level binding)
/// and any text column whose <c>SortMemberPath</c> differs from its
/// <c>Binding</c> path. The header glyph then animates but the rows never
/// reorder (or the header is silently un-clickable).
///
/// The fix is a strongly-typed <see cref="IComparer"/> wired onto the
/// column's <see cref="DataGridColumn.CustomSortComparer"/>: it reads the
/// value through a plain delegate (no reflection), so it survives trimming.
/// Pair it with <c>SortMemberPath="X"</c> + <c>CanUserSort="True"</c> on the
/// column in XAML (the path lets <see cref="DataGridSortExtensions.WireSortComparers"/>
/// match the comparer; CanUserSort="True" bypasses the reflection-based
/// sortable check).
/// </summary>
public static class DataGridSortComparers
{
    /// <summary>Integer-keyed comparer (offsets, counts, flags).</summary>
    public static IComparer Number<T>(System.Func<T, long> getter) =>
        Comparer<object?>.Create((a, b) =>
        {
            if (a is not T ta || b is not T tb) return 0;
            return getter(ta).CompareTo(getter(tb));
        });

    /// <summary>Floating-point-keyed comparer (scores).</summary>
    public static IComparer Double<T>(System.Func<T, double> getter) =>
        Comparer<object?>.Create((a, b) =>
        {
            if (a is not T ta || b is not T tb) return 0;
            return getter(ta).CompareTo(getter(tb));
        });

    /// <summary>Ordinal (case-insensitive) string comparer.</summary>
    public static IComparer Ordinal<T>(System.Func<T, string?> getter) =>
        Comparer<object?>.Create((a, b) =>
        {
            if (a is not T ta || b is not T tb) return 0;
            return string.Compare(getter(ta), getter(tb),
                System.StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>Unsigned-hex-keyed comparer (addresses). Compares the ulong
    /// directly — NOT via a long cast — so an address with bit 63 set still
    /// orders above a small one.</summary>
    public static IComparer Hex<T>(System.Func<T, ulong> getter) =>
        Comparer<object?>.Create((a, b) =>
        {
            if (a is not T ta || b is not T tb) return 0;
            return getter(ta).CompareTo(getter(tb));
        });

    /// <summary>Boolean-keyed comparer (false &lt; true).</summary>
    public static IComparer Bool<T>(System.Func<T, bool> getter) =>
        Number<T>(x => getter(x) ? 1 : 0);
}

/// <summary>
/// Wires a dictionary of <see cref="IComparer"/>s (keyed by column
/// <see cref="DataGridColumn.SortMemberPath"/>) onto a grid's columns. Call
/// once after the grid's columns exist (panel constructor, post-Init, or
/// the grid's Loaded handler). Idempotent — re-running just re-assigns the
/// same comparers, so it's safe across visual-tree re-attach.
/// </summary>
public static class DataGridSortExtensions
{
    public static void WireSortComparers(this DataGrid grid,
        IReadOnlyDictionary<string, IComparer> comparers)
    {
        foreach (var col in grid.Columns)
        {
            string? path = col.SortMemberPath;
            if (string.IsNullOrEmpty(path)) continue;
            if (comparers.TryGetValue(path, out var cmp))
                col.CustomSortComparer = cmp;
        }
    }
}
