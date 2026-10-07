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
    /// <summary>What the summary line records: the table the rows came from, everything on the panel that hid
    /// rows from it (so <c>rows &lt; fetched</c> has a recorded reason), whether a diff's NEW flags can be trusted
    /// (a partial baseline makes rare idle functions NEW), and whether the rows came from a fetch made while the
    /// DLL was still recording (a peek left on screen after the recording ended without a final fetch).</summary>
    internal sealed record Summary(
        int Rows, int Fetched, int Distinct, long TotalCalls, int FetchLimit, bool RecordingAtFetch,
        string Filter, bool HideWidgets, bool HideEvents, bool PeriodicOnly, int MinCalls,
        bool Diff, bool NewChangedOnly, int BaselineFuncs, bool BaselinePartial, int BaselineDistinct,
        DateTime SavedAtUtc, bool HidePerFrame = false, int? PerFrameHidden = null, bool BaselineHidePerFrame = false);

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
        Bool(sb, "recording_at_fetch", s.RecordingAtFetch);
        Str(sb, "filter", s.Filter);
        Bool(sb, "hide_widgets", s.HideWidgets);
        Bool(sb, "hide_events", s.HideEvents);
        Bool(sb, "periodic_only", s.PeriodicOnly);
        Int(sb, "min_calls", s.MinCalls);
        // [LIVEFUNCS-HIDE-PERFRAME] Whether the rows were asked for without the per-frame functions, and how many the
        // DLL left out; no count when it was not asked, or the DLL predates the option and left nothing out.
        Bool(sb, "hide_per_frame", s.HidePerFrame);
        if (s.PerFrameHidden is { } hidden) Int(sb, "per_frame_hidden", hidden);
        Bool(sb, "diff", s.Diff);
        if (s.Diff)
        {
            // Written only in diff mode, where they mean something.
            Bool(sb, "new_changed_only", s.NewChangedOnly);
            Int(sb, "baseline_funcs", s.BaselineFuncs);
            Bool(sb, "baseline_partial", s.BaselinePartial);
            Int(sb, "baseline_distinct", s.BaselineDistinct);
            // [LIVEFUNCS-HIDE-PERFRAME] Against a baseline fetched the other way, the per-frame rows' NEW flags are
            // not to be trusted.
            Bool(sb, "baseline_hide_per_frame", s.BaselineHidePerFrame);
        }
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
            // [TRACE-UNLOADED-NAMES] Unloaded since it fired: named from its first call, and its addr is dead.
            Bool(sb, "unloaded", e.IsUnloaded);
            Dbl(sb, "period_ms", e.MeanPeriodMs);
            Dbl(sb, "cv", e.Cv);
            Int(sb, "gap_samples", e.GapSamples);
            // The panel's own verdict, so a reader need not re-derive its thresholds from the values above.
            Bool(sb, "periodic", e.IsPeriodic);
            Str(sb, "badge", e.Kind);
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

    /// <summary>The shortest text that reads back as the same double (an exponent is valid JSON). NaN and the
    /// infinities have no JSON form, so they become null.</summary>
    private static void Dbl(StringBuilder sb, string key, double value)
        => sb.Append(",\"").Append(key).Append("\":")
             .Append(double.IsFinite(value) ? value.ToString(CultureInfo.InvariantCulture) : "null");
}
