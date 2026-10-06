using System.Globalization;
using System.Text;
using System.Text.Json;
using UE5DumpUI.Models;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [EXTPR-539-540-2026-10-02] The text Live Funcs' Save .jsonl writes: one summary line, then one line per row, in the
/// order the game first called each function. That order is the panel's causal signal (an action's entry point fires
/// before the reactions it triggers), and it is what a ranked-by-count grid loses once saved; a reader can re-sort by
/// <c>calls</c> but cannot recover the call order from a count-sorted file.
///
/// Written by hand like Dump All's lines (<see cref="JsonEncodedText"/> for strings, invariant-culture numbers), so it
/// needs no serializer metadata under Native AOT and its key order is fixed.
/// </summary>
internal static class LiveFuncsJsonl
{
    /// <summary>What the summary line records about the table the rows came from.</summary>
    internal sealed record Summary(
        int Rows, int Fetched, int Distinct, long TotalCalls, int FetchLimit,
        string Filter, bool Diff, int BaselineFuncs, DateTime SavedAtUtc);

    /// <summary>First call first; a row whose order is unknown (0) goes last. Ties by name keep the file stable.</summary>
    internal static IEnumerable<PeProfileEntry> InFirstCallOrder(IEnumerable<PeProfileEntry> rows)
        => rows.OrderBy(e => e.FirstSeq <= 0 ? long.MaxValue : e.FirstSeq)
               .ThenBy(e => e.ClassName, StringComparer.Ordinal)
               .ThenBy(e => e.FuncName, StringComparer.Ordinal);

    internal static string Format(Summary s, IEnumerable<PeProfileEntry> rows)
    {
        var sb = new StringBuilder();
        sb.Append("{\"kind\":\"live_funcs\"");
        Int(sb, "rows", s.Rows);
        Int(sb, "fetched", s.Fetched);
        Int(sb, "distinct", s.Distinct);
        Int(sb, "total_calls", s.TotalCalls);
        Int(sb, "fetch_limit", s.FetchLimit);
        Str(sb, "filter", s.Filter);
        Bool(sb, "diff", s.Diff);
        if (s.Diff) Int(sb, "baseline_funcs", s.BaselineFuncs);
        Str(sb, "saved_at", s.SavedAtUtc.ToString("o", CultureInfo.InvariantCulture));
        sb.Append("}\n");

        foreach (var e in InFirstCallOrder(rows))
        {
            sb.Append("{\"kind\":\"func\"");
            Int(sb, "order", e.FirstSeq);
            Str(sb, "class", e.ClassName);
            Str(sb, "func", e.FuncName);
            Str(sb, "addr", e.FuncAddr);
            Int(sb, "calls", e.Count);
            Int(sb, "params", e.NumParms);
            Int(sb, "params_size", e.ParmsSize);
            Str(sb, "flags", "0x" + e.FunctionFlags.ToString("X8", CultureInfo.InvariantCulture));
            Str(sb, "type", e.TypeLabel);
            Bool(sb, "widget", e.IsWidget);
            Dbl(sb, "period_ms", e.MeanPeriodMs);
            Dbl(sb, "cv", e.Cv);
            Int(sb, "gap_samples", e.GapSamples);
            if (s.Diff)
            {
                Int(sb, "delta", e.Delta);
                Bool(sb, "new", e.IsNew);
            }
            sb.Append("}\n");
        }
        return sb.ToString();
    }

    private static void Str(StringBuilder sb, string key, string value)
        => sb.Append(",\"").Append(key).Append("\":\"").Append(JsonEncodedText.Encode(value ?? "").ToString()).Append('"');

    private static void Int(StringBuilder sb, string key, long value)
        => sb.Append(",\"").Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));

    private static void Bool(StringBuilder sb, string key, bool value)
        => sb.Append(",\"").Append(key).Append("\":").Append(value ? "true" : "false");

    /// <summary>A double the JSON grammar can hold: NaN and infinities have no JSON form, so they become null.</summary>
    private static void Dbl(StringBuilder sb, string key, double value)
        => sb.Append(",\"").Append(key).Append("\":")
             .Append(double.IsFinite(value) ? value.ToString("0.###", CultureInfo.InvariantCulture) : "null");
}
