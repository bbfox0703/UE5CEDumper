using System.Globalization;
using System.Text;
using System.Text.Json;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [LIVEFUNCS-TIMELINE-2026-10-04] What the Call Trace tab exports: every call in the order it was made, with its
/// caller (parent_seq) so a reader can rebuild the tree. JSONL is written by hand like Live Funcs' Save .jsonl (no
/// serializer metadata under Native AOT, a fixed key order); the CSV holds the same columns for a spreadsheet,
/// quoted and formula-armoured by the coordinate codec's rules.
/// </summary>
internal static class CallTraceExport
{
    internal static readonly string[] CsvColumns =
    {
        "seq", "t_ms", "dur_us", "returned", "tid", "depth", "parent_seq", "class", "func", "func_addr",
        "object", "object_class", "object_addr", "object_live", "scope_root", "func_unloaded", "func_reused",
    };

    internal static void WriteJsonl(CallTrace t, TextWriter w, DateTime savedAtUtc)
    {
        var sb = new StringBuilder(256);
        sb.Append("{\"kind\":\"call_trace\"");
        Int(sb, "calls", t.Count);
        Int(sb, "first_valid", (long)t.Info.FirstValid);
        Int(sb, "written", (long)t.Info.Written);
        Int(sb, "buffer_bytes", t.Info.Bytes);
        Int(sb, "ticked", t.Info.Ticked);
        Int(sb, "excluded_per_frame", t.Info.Excluded);
        Int(sb, "returns_before_window", t.ReturnsBeforeWindow);
        // [TRACE-UNLOADED-NAMES] What the names below rest on.
        Int(sb, "distinct_funcs", t.DistinctFuncs);
        Int(sb, "unloaded_funcs", t.UnloadedFuncs);
        Int(sb, "unloaded_calls", t.UnloadedCalls);
        Int(sb, "unnamed_funcs", t.UnnamedFuncs);
        Int(sb, "unnamed_calls", t.UnnamedCalls);
        Dbl(sb, "window_s", t.WindowSeconds);
        Str(sb, "saved_at", savedAtUtc.ToString("o", CultureInfo.InvariantCulture));
        sb.Append("}\n");
        w.Write(sb);

        for (int i = 0; i < t.Count; i++)
        {
            sb.Clear();
            sb.Append("{\"kind\":\"call\"");
            Int(sb, "seq", (long)t.Seq[i]);
            Dbl(sb, "t_ms", t.StartMs(i));
            if (t.DurationUs(i) is { } us) Dbl(sb, "dur_us", us); else sb.Append(",\"dur_us\":null");
            Int(sb, "tid", t.Tid[i]);
            Int(sb, "depth", t.Depth[i]);
            if (t.Parent[i] >= 0) Int(sb, "parent_seq", (long)t.Seq[t.Parent[i]]); else sb.Append(",\"parent_seq\":null");
            Str(sb, "class", t.ClassName(i));
            Str(sb, "func", t.FuncName(i));
            Str(sb, "func_addr", Hex(t.Func[i]));
            if (t.Obj[i] != 0)
            {
                Str(sb, "object", t.ObjStale(i) ? "" : t.ObjName(i));
                Str(sb, "object_class", t.ObjClass(i));
                Str(sb, "object_addr", Hex(t.Obj[i]));
                Bool(sb, "object_live", !t.ObjStale(i));
            }
            Bool(sb, "scope_root", (t.Flags[i] & TraceRecord.ScopeRootFlag) != 0);
            // Only when true, like the object keys only with an object: a live function's line is as it was.
            if (t.FuncUnloaded(i)) Bool(sb, "func_unloaded", true);
            if (t.FuncReused(i)) Bool(sb, "func_reused", true);
            sb.Append("}\n");
            w.Write(sb);
        }
    }

    internal static void WriteCsv(CallTrace t, TextWriter w)
    {
        w.Write(string.Join(",", CsvColumns));
        w.Write("\r\n");
        var cells = new string[CsvColumns.Length];
        for (int i = 0; i < t.Count; i++)
        {
            cells[0] = t.Seq[i].ToString(CultureInfo.InvariantCulture);
            cells[1] = t.StartMs(i).ToString("R", CultureInfo.InvariantCulture);
            cells[2] = t.DurationUs(i) is { } us ? us.ToString("R", CultureInfo.InvariantCulture) : "";
            cells[3] = t.Returned[i] ? "true" : "false";
            cells[4] = t.Tid[i].ToString(CultureInfo.InvariantCulture);
            cells[5] = t.Depth[i].ToString(CultureInfo.InvariantCulture);
            cells[6] = t.Parent[i] >= 0 ? t.Seq[t.Parent[i]].ToString(CultureInfo.InvariantCulture) : "";
            cells[7] = Cell(t.ClassName(i));
            cells[8] = Cell(t.FuncName(i));
            cells[9] = Hex(t.Func[i]);
            cells[10] = t.Obj[i] == 0 || t.ObjStale(i) ? "" : Cell(t.ObjName(i));
            cells[11] = Cell(t.ObjClass(i));
            cells[12] = t.Obj[i] == 0 ? "" : Hex(t.Obj[i]);
            cells[13] = t.Obj[i] == 0 ? "" : (t.ObjStale(i) ? "false" : "true");
            cells[14] = (t.Flags[i] & TraceRecord.ScopeRootFlag) != 0 ? "true" : "false";
            cells[15] = t.FuncUnloaded(i) ? "true" : "false";
            cells[16] = t.FuncReused(i) ? "true" : "false";
            w.Write(string.Join(",", cells));
            w.Write("\r\n");
        }
    }

    // [LIVEFUNCS-STEP2] U14 red: the parameters CSV, not written yet.
    internal static readonly string[] ParamsCsvColumns = Array.Empty<string>();

    internal static void WriteParamsCsv(CallTrace t, TextWriter w) { }

    // A name comes from the game: quote it when it needs it, and keep a spreadsheet from reading it as a formula.
    private static string Cell(string s) => CoordCsvCodec.Field(CoordCsvCodec.Armour(s));

    private static string Hex(ulong v) => "0x" + v.ToString("X", CultureInfo.InvariantCulture);

    private static void Str(StringBuilder sb, string key, string value)
        => sb.Append(",\"").Append(key).Append("\":\"").Append(JsonEncodedText.Encode(value ?? "").ToString()).Append('"');

    private static void Int(StringBuilder sb, string key, long value)
        => sb.Append(",\"").Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));

    private static void Bool(StringBuilder sb, string key, bool value)
        => sb.Append(",\"").Append(key).Append("\":").Append(value ? "true" : "false");

    private static void Dbl(StringBuilder sb, string key, double value)
        => sb.Append(",\"").Append(key).Append("\":")
             .Append(double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "null");
}
