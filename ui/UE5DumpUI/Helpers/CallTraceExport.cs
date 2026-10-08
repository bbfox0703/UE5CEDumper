using System.Globalization;
using System.Text;
using System.Text.Json;
using UE5DumpUI.Models;
using UE5DumpUI.Services;

namespace UE5DumpUI.Helpers;

/// <summary>
/// [LIVEFUNCS-TIMELINE-2026-10-04] What the Call Trace tab exports: every call in the order it was made, with its
/// caller (parent_seq) so a reader can rebuild the tree. JSONL is written by hand like Live Funcs' Save .jsonl (no
/// serializer metadata under Native AOT, a fixed key order); the calls CSV holds the same columns for a spreadsheet,
/// quoted and formula-armoured by the coordinate codec's rules. [LIVEFUNCS-STEP2] A trace that took parameter
/// snapshots carries them in the JSONL, on the chosen calls only, and in a CSV of its own: a row per parameter would
/// multiply the calls CSV's rows, and columns per parameter cannot hold functions whose parameters differ.
/// </summary>
internal static class CallTraceExport
{
    // [LIVEFUNCS-STEP2] The entry-record flags of a chosen call (docs/pipe-protocol.md, "Entry-record flags").
    private const uint SnapTakenFlag = 2, SnapLoneFlag = 4, SnapExcludedFlag = 8, SnapBudgetFlag = 16;
    // [LIVEFUNCS-STEP3] The flags that say a call was chosen for its parameters. Lone and Excluded say it was chosen
    // for something: a call chosen for its stack alone carries them too, and has no parameters to report.
    private const uint ParamChosenFlags = SnapTakenFlag | SnapBudgetFlag;
    private const uint AloneFlags = SnapLoneFlag | SnapExcludedFlag;

    internal static readonly string[] CsvColumns =
    {
        "seq", "t_ms", "dur_us", "returned", "tid", "depth", "parent_seq", "class", "func", "func_addr",
        "object", "object_class", "object_addr", "object_live", "scope_root", "func_unloaded", "func_reused",
    };

    /// <summary>[LIVEFUNCS-STEP2] The parameters CSV, one row per (call, parameter). Its seq is the calls CSV's and the
    /// JSONL's, so the two files join on it.</summary>
    internal static readonly string[] ParamsCsvColumns =
    {
        "seq", "t_ms", "tid", "class", "func", "arm", "param", "kind", "type", "value_at_call", "value_after", "marks",
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
        // [LIVEFUNCS-STEP2] What was recorded: only inside the ticked calls (scoped, by address or by name), or every
        // call; with snapshots and nothing ticked, only the chosen calls. A DLL from before names sends no scoped: its
        // address ticks were the scope.
        Int(sb, "ticked_names", t.Info.TickedNames);
        Bool(sb, "scoped", t.Info.Scoped ?? t.Info.Ticked > 0);
        Bool(sb, "snapshots_only", t.Info.SnapOnly);
        // [LIVEFUNCS-STEP3] Only with a parameter ring: a Start that chose stacks alone allocates the buffer with none,
        // and has no parameter budget to account for.
        if (t.Info.Snap is { Rings: > 0 } snap)
        {
            // Calls the budget left without parameters: the skipped ones are below; the dropped ones, for which nothing
            // they were chosen for was taken, were not recorded.
            Int(sb, "snapshot_skipped_budget", (long)snap.SkippedBudget);
            Int(sb, "snapshot_dropped_budget", (long)snap.DroppedBudget);
        }
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

        // [LIVEFUNCS-STEP2] Every arm's layout before the calls: a call's hex decodes with the arm that wrote it, and a
        // reloaded function is another arm whose offsets need not match the first one's.
        if (t.Snapshots is { } snaps)
        {
            foreach (var arm in snaps.Arms)
            {
                sb.Clear();
                AppendArm(sb, arm);
                w.Write(sb);
            }
        }

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
            // [LIVEFUNCS-STEP2] Last, and on the chosen calls only, for the same reason. [LIVEFUNCS-STEP3] A call chosen
            // for its stack alone gets no snapshot field; recorded on its own, it still says so.
            uint f = t.Flags[i];
            if ((f & ParamChosenFlags) != 0 || t.Snapshots?.Has(i) == true) AppendSnapshot(sb, t, i);
            else if ((f & AloneFlags) != 0) AppendAlone(sb, f);
            sb.Append("}\n");
            w.Write(sb);
        }
    }

    private static void AppendArm(StringBuilder sb, SnapArm a)
    {
        sb.Append("{\"kind\":\"snapshot_layout\"");
        Int(sb, "arm", a.Index);
        Int(sb, "ring", a.Ring);
        Str(sb, "class", a.ClassName);
        Str(sb, "func", a.FuncName);
        Str(sb, "func_addr", Hex(a.Addr));
        Str(sb, "function_flags", Hex(a.FunctionFlags));
        Int(sb, "parms_size", a.ParmsSize);
        Str(sb, "state", a.State);
        Str(sb, "why", a.Why);
        if (a.Layout is not { } layout)
        {
            sb.Append(",\"params\":null}\n");   // no layout (state says why): its calls carry hex alone
            return;
        }
        sb.Append(",\"params\":[");
        for (int k = 0; k < layout.Params.Count; k++)
        {
            var p = layout.Params[k];
            if (k > 0) sb.Append(',');
            First(sb, "name", p.Name);
            Str(sb, "type", p.Type);
            Int(sb, "offset", p.Offset);
            Int(sb, "size", p.Size);
            Int(sb, "array_dim", p.ArrayDim);   // size is one element's: the hex holds size x array_dim bytes
            Str(sb, "kind", p.Kind);
            sb.Append('}');
        }
        sb.Append("]}\n");
    }

    private static void AppendSnapshot(StringBuilder sb, CallTrace t, int i)
    {
        var snaps = t.Snapshots;
        SnapSlot? entry = snaps?.EntryOf(i), after = snaps?.AfterOf(i);
        var any = entry ?? after;
        Str(sb, "snapshot", SnapshotState(t, i, any != null));
        AppendAlone(sb, t.Flags[i]);
        if (any == null) return;
        Int(sb, "arm", any.Arm);
        if (snaps!.ArmOf(any)?.Layout is { } layout && (entry?.Values != null || after?.Values != null))
        {
            sb.Append(",\"params\":[");
            for (int k = 0; k < layout.Params.Count; k++)
            {
                var p = layout.Params[k];
                if (k > 0) sb.Append(',');
                First(sb, "name", p.Name);
                if (ValueAt(entry, k) is { } at)
                {
                    Str(sb, "text", at.Text);
                    Str(sb, "mark", MarkName(at.Mark));
                }
                if (CarriedAfter(p) && ValueAt(after, k) is { } aft)
                {
                    Str(sb, "after_text", aft.Text);
                    Str(sb, "after_mark", MarkName(aft.Mark));
                }
                sb.Append('}');
            }
            sb.Append(']');
        }
        if (entry != null) Str(sb, "params_hex_in", Convert.ToHexString(entry.Data));
        if (after != null) Str(sb, "params_hex_out", Convert.ToHexString(after.Data));
    }

    // Recorded outside every ticked scope, or for a per-frame function the trace leaves out: for what it was chosen for.
    private static void AppendAlone(StringBuilder sb, uint flags)
    {
        Bool(sb, "lone", (flags & SnapLoneFlag) != 0);
        Bool(sb, "excluded", (flags & SnapExcludedFlag) != 0);
    }

    // From the flags, and whether the copy they claim is still among the loaded snapshots: a ring keeps only its last
    // calls, so an older call's copy is written over by a newer one's.
    private static string SnapshotState(CallTrace t, int i, bool hasCopy)
    {
        if ((t.Flags[i] & SnapBudgetFlag) != 0 && !hasCopy) return "over_budget";
        return hasCopy || t.Snapshots == null ? "taken" : "overwritten";
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

    /// <summary>[LIVEFUNCS-STEP2] One row per parameter of every call with a copy, in call order: its value at the call
    /// and, for what the copy after the call carries, its value after it. A call whose arm has no layout gets one row
    /// with its hex instead, so every copy the trace holds is in the file.</summary>
    internal static void WriteParamsCsv(CallTrace t, TextWriter w)
    {
        w.Write(string.Join(",", ParamsCsvColumns));
        w.Write("\r\n");
        if (t.Snapshots is not { } snaps) return;
        var cells = new string[ParamsCsvColumns.Length];
        for (int i = 0; i < t.Count; i++)
        {
            SnapSlot? entry = snaps.EntryOf(i), after = snaps.AfterOf(i);
            if ((entry ?? after) is not { } any) continue;
            cells[0] = t.Seq[i].ToString(CultureInfo.InvariantCulture);
            cells[1] = t.StartMs(i).ToString("R", CultureInfo.InvariantCulture);
            cells[2] = t.Tid[i].ToString(CultureInfo.InvariantCulture);
            cells[3] = Cell(t.ClassName(i));
            cells[4] = Cell(t.FuncName(i));
            cells[5] = any.Arm.ToString(CultureInfo.InvariantCulture);
            var layout = snaps.ArmOf(any)?.Layout;
            if (layout == null || (entry?.Values == null && after?.Values == null))
            {
                cells[6] = cells[7] = cells[8] = "";
                cells[9] = entry != null ? Cell(Convert.ToHexString(entry.Data)) : "";
                cells[10] = after != null ? Cell(Convert.ToHexString(after.Data)) : "";
                cells[11] = Cell(any.RawOnly.Length > 0 ? "raw:" + any.RawOnly : "raw");
                WriteRow(w, cells);
                continue;
            }
            for (int k = 0; k < layout.Params.Count; k++)
            {
                var p = layout.Params[k];
                var at = ValueAt(entry, k);
                var aft = CarriedAfter(p) ? ValueAt(after, k) : null;
                cells[6] = Cell(p.Name);
                cells[7] = Cell(p.Kind);
                cells[8] = Cell(p.Type);
                cells[9] = Cell(at?.Text ?? "");
                cells[10] = Cell(aft?.Text ?? "");
                cells[11] = Cell(at != null && aft != null ? "call:" + MarkName(at.Mark) + " after:" + MarkName(aft.Mark)
                                 : at != null ? "call:" + MarkName(at.Mark)
                                 : aft != null ? "after:" + MarkName(aft.Mark) : "");
                WriteRow(w, cells);
            }
        }
    }

    private static void WriteRow(TextWriter w, string[] cells)
    {
        w.Write(string.Join(",", cells));
        w.Write("\r\n");
    }

    // The copy after the call carries these alone; the DLL marks the rest Missing there (Ubel's DecodeParamSnapshot).
    private static bool CarriedAfter(SnapParam p) => p.Kind is "out" or "in_out" or "return";

    // Values line up with the layout's params by index; past a short list a value is absent, never another's.
    private static SnapValue? ValueAt(SnapSlot? slot, int k)
        => slot?.Values is { } v && k < v.Count ? v[k] : null;

    private static string MarkName(SnapMark m) => m switch
    {
        SnapMark.Exact   => "exact",
        SnapMark.Now     => "now",
        SnapMark.Gone    => "gone",
        SnapMark.Missing => "missing",
        SnapMark.Header  => "header",
        SnapMark.Raw     => "raw",
        _                => ((int)m).ToString(CultureInfo.InvariantCulture),   // a DLL newer than this UI
    };

    // A name comes from the game: quote it when it needs it, and keep a spreadsheet from reading it as a formula.
    private static string Cell(string s) => CoordCsvCodec.Field(CoordCsvCodec.Armour(s));

    private static string Hex(ulong v) => "0x" + v.ToString("X", CultureInfo.InvariantCulture);

    private static string Enc(string? value) => JsonEncodedText.Encode(value ?? "").ToString();

    // An object's first key inside an array: it opens the object, with no comma before it.
    private static void First(StringBuilder sb, string key, string value)
        => sb.Append("{\"").Append(key).Append("\":\"").Append(Enc(value)).Append('"');

    private static void Str(StringBuilder sb, string key, string value)
        => sb.Append(",\"").Append(key).Append("\":\"").Append(Enc(value)).Append('"');

    private static void Int(StringBuilder sb, string key, long value)
        => sb.Append(",\"").Append(key).Append("\":").Append(value.ToString(CultureInfo.InvariantCulture));

    private static void Bool(StringBuilder sb, string key, bool value)
        => sb.Append(",\"").Append(key).Append("\":").Append(value ? "true" : "false");

    private static void Dbl(StringBuilder sb, string key, double value)
        => sb.Append(",\"").Append(key).Append("\":")
             .Append(double.IsFinite(value) ? value.ToString("R", CultureInfo.InvariantCulture) : "null");
}
