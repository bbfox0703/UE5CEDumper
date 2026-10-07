using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-TIMELINE-2026-10-04] What the Call Trace tab does with a built trace: which calls are visible as the
/// user expands and collapses, the keyword filter (space = AND, the shared matcher), and the exports.
/// </summary>
public class CallTraceTreeTests
{
    private const ulong R = TraceRecord.ReturnBit;
    private static TraceRecord E(ulong seq, ulong ticks, ulong func, ulong obj = 0, uint tid = 1, uint flags = 0)
        => new(seq, ticks, func, obj, tid, flags);
    private static TraceRecord Ret(ulong seq, ulong ticks, ulong entrySeq, uint tid = 1) => new(seq | R, ticks, entrySeq, 0, tid, 0);

    // A(0) > [B(1) > C(2)], D(3) ; then E(4) on its own. Ticks at 1 MHz.
    private static CallTrace Sample() => CallTraceBuilder.Build(new[]
    {
        E(0, 1000, 0xA, 0x10), E(1, 1010, 0xB, 0x20), E(2, 1020, 0xC, 0x30), Ret(3, 1030, 2), Ret(4, 1040, 1),
        E(5, 1050, 0xD, 0x10), Ret(6, 1060, 5), Ret(7, 2000, 0),
        E(8, 3000, 0xE, 0, flags: TraceRecord.ScopeRootFlag),
    }, new TraceInfo { QpcFreq = 1_000_000, FirstValid = 0, Written = 9, Bytes = 32 << 20, Ticked = 1 },
    new[]
    {
        new TraceFuncName { Addr = 0xA, Live = true, ClassName = "PlayerController", FuncName = "InputJump" },
        new TraceFuncName { Addr = 0xB, Live = true, ClassName = "Character", FuncName = "Jump" },
        new TraceFuncName { Addr = 0xC, Live = true, ClassName = "AnimInstance", FuncName = "BlueprintUpdateAnimation" },
        new TraceFuncName { Addr = 0xD, Live = true, ClassName = "Character", FuncName = "OnJumped" },
        new TraceFuncName { Addr = 0xE, Live = true, ClassName = "GameMode", FuncName = "=Tick,\"x\"" },
    },
    new[]
    {
        new TraceObjName { Addr = 0x10, Live = true, Name = "PC_Hero_0", ClassName = "PC_Hero_C" },
        new TraceObjName { Addr = 0x20, Live = true, Name = "BP_Hero_0", ClassName = "BP_Hero_C" },
        new TraceObjName { Addr = 0x30, Live = false },
    });

    [Fact]
    public void Collapsed_shows_the_roots_in_call_order()
    {
        var tree = new CallTraceTree(Sample());
        Assert.Equal(new[] { 0, 4 }, tree.Visible);
    }

    [Fact]
    public void Expanding_shows_the_children_under_their_caller_and_collapsing_hides_them()
    {
        var tree = new CallTraceTree(Sample());
        tree.Toggle(0);
        Assert.Equal(new[] { 0, 1, 3, 4 }, tree.Visible);
        tree.Toggle(1);
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, tree.Visible);
        tree.Toggle(0);
        Assert.Equal(new[] { 0, 4 }, tree.Visible);
        tree.Toggle(0);   // B stays expanded underneath
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, tree.Visible);
    }

    [Fact]
    public void A_call_without_children_does_not_toggle()
    {
        var tree = new CallTraceTree(Sample());
        tree.Toggle(4);
        Assert.False(tree.IsExpanded(4));
        Assert.Equal(new[] { 0, 4 }, tree.Visible);
    }

    [Fact]
    public void Expand_all_and_collapse_all()
    {
        var tree = new CallTraceTree(Sample());
        tree.ExpandAll();
        Assert.Equal(new[] { 0, 1, 2, 3, 4 }, tree.Visible);
        tree.CollapseAll();
        Assert.Equal(new[] { 0, 4 }, tree.Visible);
    }

    [Fact]
    public void Reveal_expands_the_ancestors_and_gives_the_row()
    {
        var tree = new CallTraceTree(Sample());
        Assert.Equal(2, tree.Reveal(2));
        Assert.True(tree.IsExpanded(0) && tree.IsExpanded(1));
        Assert.Equal(0, tree.Reveal(0));
    }

    [Fact]
    public void Match_is_space_AND_over_the_function_and_the_object_in_call_order()
    {
        var t = Sample();
        Assert.Equal(new[] { 1, 3 }, CallTraceTree.Match(t, "character", 100, out _));
        Assert.Equal(new[] { 1 }, CallTraceTree.Match(t, "jump bp_hero", 100, out _));   // one term each side
        Assert.Equal(new[] { 0, 3 }, CallTraceTree.Match(t, "JUMP pc_hero", 100, out _));   // case-insensitive
        Assert.Empty(CallTraceTree.Match(t, "jump nothing", 100, out _));
        Assert.Empty(CallTraceTree.Match(t, "   ", 100, out _));
    }

    [Fact]
    public void Match_stops_at_the_cap_and_says_so()
    {
        var hits = CallTraceTree.Match(Sample(), "a", 2, out bool capped);
        Assert.Equal(2, hits.Length);
        Assert.True(capped);
        CallTraceTree.Match(Sample(), "OnJumped", 2, out capped);
        Assert.False(capped);
    }

    [Fact]
    public void A_stale_object_is_not_matched_by_its_old_name()
    {
        // C's object (0x30) is no longer live: its row shows the address, which is what the filter sees.
        Assert.Equal(new[] { 2 }, CallTraceTree.Match(Sample(), "0x30", 100, out _));
    }

    [Fact]
    public void Jsonl_has_a_summary_then_every_call_with_its_caller()
    {
        var w = new StringWriter();
        CallTraceExport.WriteJsonl(Sample(), w, new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc));
        var lines = w.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, lines.Length);
        Assert.StartsWith("{\"kind\":\"call_trace\",\"calls\":5,", lines[0]);
        Assert.Contains("\"ticked\":1", lines[0]);
        Assert.Contains("\"saved_at\":\"2026-10-07T00:00:00.0000000Z\"", lines[0]);
        Assert.Equal("{\"kind\":\"call\",\"seq\":2,\"t_ms\":0.02,\"dur_us\":10,\"tid\":1,\"depth\":2,\"parent_seq\":1,"
                     + "\"class\":\"AnimInstance\",\"func\":\"BlueprintUpdateAnimation\",\"func_addr\":\"0xC\","
                     + "\"object\":\"\",\"object_class\":\"\",\"object_addr\":\"0x30\",\"object_live\":false,\"scope_root\":false}",
                     lines[3]);
        Assert.Contains("\"seq\":8,", lines[5]);
        Assert.Contains("\"dur_us\":null", lines[5]);       // never returned in the window
        Assert.Contains("\"parent_seq\":null", lines[5]);
        Assert.Contains("\"scope_root\":true", lines[5]);
        Assert.DoesNotContain("\"object\"", lines[5]);       // a call with no object has no object keys
    }

    [Fact]
    public void Csv_quotes_and_armours_game_names_and_keeps_the_column_count()
    {
        var w = new StringWriter();
        CallTraceExport.WriteCsv(Sample(), w);
        var lines = w.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(6, lines.Length);
        Assert.Equal(string.Join(",", CallTraceExport.CsvColumns), lines[0]);
        Assert.StartsWith("1,0.01,30,true,1,1,0,Character,Jump,0xB,BP_Hero_0,BP_Hero_C,0x20,true,false", lines[2]);
        // "=Tick,"x"": armoured against a formula, then quoted for the comma and the quotes.
        Assert.Contains(",GameMode,\"'=Tick,\"\"x\"\"\",0xE,", lines[5]);
        Assert.EndsWith(",,,,,true,false,false", lines[5]);   // no object: four empty cells, the scope root, not unloaded or reused
    }

    // [TRACE-UNLOADED-NAMES] An unloaded function's calls say so in both formats; a live one's JSONL line is unchanged.
    private static CallTrace UnloadedSample() => CallTraceBuilder.Build(new[] { E(0, 1000, 0xA), E(1, 1010, 0xB) },
        new TraceInfo { QpcFreq = 1_000_000, Written = 2 },
        new[]
        {
            new TraceFuncName { Addr = 0xA, Live = true, ClassName = "Pawn", FuncName = "Jump" },
            new TraceFuncName { Addr = 0xB, Live = false, Unloaded = true, ClassName = "WBP_Inventory_C", FuncName = "OnOpen" },
        });

    [Fact]
    public void Export_marks_an_unloaded_functions_calls()
    {
        var j = new StringWriter();
        CallTraceExport.WriteJsonl(UnloadedSample(), j, new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc));
        var jl = j.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.Contains("\"unloaded_funcs\":1", jl[0]);
        Assert.Contains("\"unnamed_funcs\":0", jl[0]);
        Assert.DoesNotContain("func_unloaded", jl[1]);
        Assert.Contains("\"func\":\"OnOpen\"", jl[2]);
        Assert.Contains("\"func_unloaded\":true", jl[2]);

        var c = new StringWriter();
        CallTraceExport.WriteCsv(UnloadedSample(), c);
        var cl = c.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal("func_unloaded", CallTraceExport.CsvColumns[^2]);
        Assert.Equal("func_reused", CallTraceExport.CsvColumns[^1]);
        Assert.EndsWith(",false,false", cl[1]);
        Assert.Contains(",WBP_Inventory_C,OnOpen,", cl[2]);
        Assert.EndsWith(",true,false", cl[2]);
    }

    [Fact]
    public void Export_marks_a_reused_address()
    {
        // Review DLL-3: the calls at an address that held two functions during the recording.
        var t = CallTraceBuilder.Build(new[] { E(0, 1000, 0xA) }, new TraceInfo { QpcFreq = 1_000_000, Written = 1 },
            new[] { new TraceFuncName { Addr = 0xA, Live = true, Reused = true, ClassName = "WBP_Map_C", FuncName = "OnTile" } });
        var j = new StringWriter();
        CallTraceExport.WriteJsonl(t, j, new DateTime(2026, 10, 7, 0, 0, 0, DateTimeKind.Utc));
        Assert.Contains("\"func_reused\":true", j.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries)[1]);
        var c = new StringWriter();
        CallTraceExport.WriteCsv(t, c);
        Assert.EndsWith(",false,true", c.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries)[1]);
    }

    // ---- [LIVEFUNCS-STEP2] U14: the parameter snapshots in the exports ----

    // The entry-record flags (pipe-protocol.md, "Entry-record flags"), spelled out so a test reads as the DLL wrote it.
    private const uint SnapTaken = 2, SnapLone = 4, SnapExcluded = 8, SnapBudget = 16;

    // Root A(0) > [TakeDamage taken (1), TakeDamage over the budget (3), TakeDamage overwritten (5), SetName excluded
    // but chosen (7)], then OnOpen lone (10), whose arm never got a layout. Arm 0 has an in, an out and a return value
    // and an after copy; arm 2 has a game string that starts like a formula.
    private static CallTrace SnapSample()
    {
        var t = CallTraceBuilder.Build(new[]
        {
            E(0, 1000, 0xA, 0x10, flags: TraceRecord.ScopeRootFlag),
            E(1, 1010, 0xB, 0x20, flags: SnapTaken), Ret(2, 1020, 1),
            E(3, 1030, 0xB, 0x20, flags: SnapBudget), Ret(4, 1040, 3),
            E(5, 1050, 0xB, 0x20, flags: SnapTaken), Ret(6, 1060, 5),
            E(7, 1070, 0xD, 0x20, flags: SnapTaken | SnapExcluded), Ret(8, 1080, 7),
            Ret(9, 2000, 0),
            E(10, 3000, 0xC, flags: SnapTaken | SnapLone),
        }, new TraceInfo
        {
            QpcFreq = 1_000_000, Written = 11, Ticked = 0, TickedNames = 1, Scoped = true,
            Snap = new SnapInfo { Allocated = true, Rings = 3, SkippedBudget = 1, DroppedBudget = 2 },
        },
        new[]
        {
            new TraceFuncName { Addr = 0xA, Live = true, ClassName = "PlayerController", FuncName = "InputJump" },
            new TraceFuncName { Addr = 0xB, Live = true, ClassName = "Character", FuncName = "TakeDamage" },
            new TraceFuncName { Addr = 0xC, Live = false, Unloaded = true, ClassName = "WBP_Inventory_C", FuncName = "OnOpen" },
            new TraceFuncName { Addr = 0xD, Live = true, ClassName = "Pawn", FuncName = "SetName" },
        },
        new[]
        {
            new TraceObjName { Addr = 0x10, Live = true, Name = "PC_Hero_0", ClassName = "PC_Hero_C" },
            new TraceObjName { Addr = 0x20, Live = true, Name = "BP_Hero_0", ClassName = "BP_Hero_C" },
        });

        var arms = new[]
        {
            new SnapArm
            {
                Index = 0, Ring = 0, Addr = 0xB, ClassName = "Character", FuncName = "TakeDamage", FunctionFlags = 0x00400000,
                ParmsSize = 12, State = "read",
                Layout = new SnapLayout
                {
                    ClassName = "Character", FuncName = "TakeDamage", ParmsSize = 12,
                    Params = new[]
                    {
                        new SnapParam { Name = "Amount", Type = "IntProperty", Offset = 0, Size = 4, Kind = "in" },
                        new SnapParam { Name = "Result", Type = "IntProperty", Offset = 4, Size = 4, Kind = "out" },
                        new SnapParam { Name = "ReturnValue", Type = "BoolProperty", Offset = 8, Size = 1, Kind = "return" },
                    },
                },
            },
            new SnapArm
            {
                Index = 1, Ring = 1, Addr = 0xC, ClassName = "WBP_Inventory_C", FuncName = "OnOpen", ParmsSize = 2,
                State = "unloaded_before_read", Why = "gone before its layout was read",
            },
            new SnapArm
            {
                Index = 2, Ring = 2, Addr = 0xD, ClassName = "Pawn", FuncName = "SetName", ParmsSize = 16, State = "read",
                Layout = new SnapLayout
                {
                    ClassName = "Pawn", FuncName = "SetName", ParmsSize = 16,
                    Params = new[] { new SnapParam { Name = "NewName", Type = "StrProperty", Offset = 0, Size = 16, Kind = "const_ref" } },
                },
            },
        };
        var slots = new[]
        {
            new SnapSlot
            {
                Index = 0, EntrySeq = 1, Arm = 0, Len = 12,
                Data = new byte[] { 0xFB, 0xFF, 0xFF, 0xFF, 0, 0, 0, 0, 0, 0, 0, 0 },
                Values = new[]
                {
                    new SnapValue { Text = "-5" }, new SnapValue { Text = "0" },
                    new SnapValue { Text = "—", Mark = SnapMark.Missing },
                },
            },
            new SnapSlot
            {
                Index = 1, EntrySeq = 1, Arm = 0, Len = 12, IsAfter = true,
                Data = new byte[] { 0xFB, 0xFF, 0xFF, 0xFF, 7, 0, 0, 0, 1, 0, 0, 0 },
                Values = new[]
                {
                    new SnapValue { Mark = SnapMark.Missing }, new SnapValue { Text = "7" }, new SnapValue { Text = "true" },
                },
            },
            new SnapSlot
            {
                Index = 0, EntrySeq = 7, Arm = 2, Len = 16, Data = new byte[16],
                Values = new[] { new SnapValue { Text = "=1+2,\"x\"", Mark = SnapMark.Header } },
            },
            new SnapSlot { Index = 0, EntrySeq = 10, Arm = 1, Len = 2, Data = new byte[] { 1, 2 }, RawOnly = "unloaded_before_read" },
        };
        t.Snapshots = CallTraceSnapshots.Join(t, arms, Array.Empty<SnapRingInfo>(), slots, 0);
        return t;
    }

    private static string[] JsonlLines(CallTrace t)
    {
        var w = new StringWriter();
        CallTraceExport.WriteJsonl(t, w, new DateTime(2026, 10, 8, 0, 0, 0, DateTimeKind.Utc));
        return w.ToString().Split('\n', StringSplitOptions.RemoveEmptyEntries);
    }

    [Fact]
    public void Jsonl_header_says_whether_the_trace_was_scoped_and_whether_it_kept_only_the_chosen_calls()
    {
        var head = JsonlLines(SnapSample())[0];
        Assert.Contains("\"ticked\":0,\"ticked_names\":1,\"scoped\":true,\"snapshots_only\":false,", head);   // scoped by name alone
        Assert.Contains("\"snapshot_skipped_budget\":1,\"snapshot_dropped_budget\":2,", head);

        // A DLL from before names sends no scoped: its ticks said it.
        Assert.Contains("\"scoped\":true,", JsonlLines(Sample())[0]);
        Assert.Contains("\"scoped\":false,", JsonlLines(UnloadedSample())[0]);
        Assert.DoesNotContain("snapshot_skipped_budget", JsonlLines(Sample())[0]);   // no buffer, no budget keys

        var only = CallTraceBuilder.Build(new[] { E(0, 1000, 0xA, flags: SnapTaken | SnapLone) },
            new TraceInfo { QpcFreq = 1_000_000, Written = 1, Scoped = true, SnapOnly = true });
        Assert.Contains("\"scoped\":true,\"snapshots_only\":true,", JsonlLines(only)[0]);
    }

    [Fact]
    public void Jsonl_has_one_snapshot_layout_line_per_arm_right_after_the_header()
    {
        var lines = JsonlLines(SnapSample());
        Assert.Equal(1 + 3 + 6, lines.Length);   // header, three arms, six calls
        Assert.Equal("{\"kind\":\"snapshot_layout\",\"arm\":0,\"ring\":0,\"class\":\"Character\",\"func\":\"TakeDamage\","
                     + "\"func_addr\":\"0xB\",\"function_flags\":\"0x400000\",\"parms_size\":12,\"state\":\"read\",\"why\":\"\","
                     + "\"params\":[{\"name\":\"Amount\",\"type\":\"IntProperty\",\"offset\":0,\"size\":4,\"array_dim\":1,\"kind\":\"in\"},"
                     + "{\"name\":\"Result\",\"type\":\"IntProperty\",\"offset\":4,\"size\":4,\"array_dim\":1,\"kind\":\"out\"},"
                     + "{\"name\":\"ReturnValue\",\"type\":\"BoolProperty\",\"offset\":8,\"size\":1,\"array_dim\":1,\"kind\":\"return\"}]}",
                     lines[1]);
        // No layout: the reader is told why, and that the hex is all there is.
        Assert.StartsWith("{\"kind\":\"snapshot_layout\",\"arm\":1,\"ring\":1,", lines[2]);
        Assert.EndsWith("\"state\":\"unloaded_before_read\",\"why\":\"gone before its layout was read\",\"params\":null}", lines[2]);
        Assert.StartsWith("{\"kind\":\"snapshot_layout\",\"arm\":2,", lines[3]);
        Assert.StartsWith("{\"kind\":\"call\",\"seq\":0,", lines[4]);

        // A trace without snapshots has no layout line.
        Assert.DoesNotContain(JsonlLines(Sample()), l => l.Contains("snapshot_layout"));
    }

    [Fact]
    public void Jsonl_snapshot_keys_are_on_the_chosen_calls_only()
    {
        var lines = JsonlLines(SnapSample());
        var calls = lines.Skip(4).Select(l => System.Text.Json.JsonDocument.Parse(l).RootElement).ToArray();
        static string? S(System.Text.Json.JsonElement e, string k) => e.TryGetProperty(k, out var v) ? v.ToString() : null;

        // The unchosen root's line is byte for byte what it was before snapshots.
        Assert.Equal("{\"kind\":\"call\",\"seq\":0,\"t_ms\":0,\"dur_us\":1000,\"tid\":1,\"depth\":0,\"parent_seq\":null,"
                     + "\"class\":\"PlayerController\",\"func\":\"InputJump\",\"func_addr\":\"0xA\","
                     + "\"object\":\"PC_Hero_0\",\"object_class\":\"PC_Hero_C\",\"object_addr\":\"0x10\",\"object_live\":true,"
                     + "\"scope_root\":true}", lines[4]);
        foreach (var k in new[] { "snapshot", "lone", "excluded", "arm", "params", "params_hex_in", "params_hex_out" })
            Assert.False(calls[0].TryGetProperty(k, out _), k);

        // Taken, with an after copy: the values at the call, and the out / return values after it.
        var taken = calls[1];
        Assert.Equal("taken", S(taken, "snapshot"));
        Assert.Equal("False", S(taken, "lone"));
        Assert.Equal("False", S(taken, "excluded"));
        Assert.Equal("0", S(taken, "arm"));
        Assert.Equal("FBFFFFFF0000000000000000", S(taken, "params_hex_in"));
        Assert.Equal("FBFFFFFF0700000001000000", S(taken, "params_hex_out"));
        var ps = taken.GetProperty("params").EnumerateArray().ToArray();
        Assert.Equal(3, ps.Length);
        Assert.Equal(new[] { "Amount", "-5", "exact" }, new[] { S(ps[0], "name"), S(ps[0], "text"), S(ps[0], "mark") });
        Assert.Null(S(ps[0], "after_text"));   // an In parameter: the copy after the call does not carry it
        Assert.Equal(new[] { "Result", "0", "exact", "7", "exact" },
                     new[] { S(ps[1], "name"), S(ps[1], "text"), S(ps[1], "mark"), S(ps[1], "after_text"), S(ps[1], "after_mark") });
        Assert.Equal(new[] { "ReturnValue", "—", "missing", "true", "exact" },
                     new[] { S(ps[2], "name"), S(ps[2], "text"), S(ps[2], "mark"), S(ps[2], "after_text"), S(ps[2], "after_mark") });
        // Every snapshot key comes after the keys a call always had.
        Assert.Contains("\"scope_root\":false,\"snapshot\":\"taken\",\"lone\":false,\"excluded\":false,\"arm\":0,\"params\":[", lines[5]);

        // Over the budget: chosen, no copy.
        Assert.Equal("over_budget", S(calls[2], "snapshot"));
        Assert.Null(S(calls[2], "params"));
        Assert.Null(S(calls[2], "params_hex_in"));
        Assert.Null(S(calls[2], "arm"));

        // Taken, but the ring has overwritten it since.
        Assert.Equal("overwritten", S(calls[3], "snapshot"));
        Assert.Null(S(calls[3], "params_hex_in"));

        // Excluded but chosen: no after copy, so no out hex.
        Assert.Equal("True", S(calls[4], "excluded"));
        Assert.Equal("False", S(calls[4], "lone"));
        Assert.Equal("=1+2,\"x\"", S(calls[4].GetProperty("params")[0], "text"));
        Assert.Equal("header", S(calls[4].GetProperty("params")[0], "mark"));
        Assert.Equal(new string('0', 32), S(calls[4], "params_hex_in"));
        Assert.Null(S(calls[4], "params_hex_out"));

        // Lone, its arm never read: the hex and no decoded values.
        Assert.Equal("taken", S(calls[5], "snapshot"));
        Assert.Equal("True", S(calls[5], "lone"));
        Assert.Equal("1", S(calls[5], "arm"));
        Assert.Equal("0102", S(calls[5], "params_hex_in"));
        Assert.Null(S(calls[5], "params"));
    }

    [Fact]
    public void Params_csv_has_a_row_per_parameter_of_every_call_with_a_copy_armoured()
    {
        Assert.Equal(new[]
        {
            "seq", "t_ms", "tid", "class", "func", "arm", "param", "kind", "type", "value_at_call", "value_after", "marks",
        }, CallTraceExport.ParamsCsvColumns);

        var w = new StringWriter();
        CallTraceExport.WriteParamsCsv(SnapSample(), w);
        var lines = w.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(new[]
        {
            string.Join(",", CallTraceExport.ParamsCsvColumns),
            "1,0.01,1,Character,TakeDamage,0,Amount,in,IntProperty,'-5,,call:exact",   // "-5" armoured like any game text
            "1,0.01,1,Character,TakeDamage,0,Result,out,IntProperty,0,7,call:exact after:exact",
            "1,0.01,1,Character,TakeDamage,0,ReturnValue,return,BoolProperty,—,true,call:missing after:exact",
            "7,0.07,1,Pawn,SetName,2,NewName,const_ref,StrProperty,\"'=1+2,\"\"x\"\"\",,call:header",
            "10,2,1,WBP_Inventory_C,OnOpen,1,,,,0102,,raw:unloaded_before_read",   // no layout: one row, its hex
        }, lines);

        // A trace without snapshots: the header alone.
        var none = new StringWriter();
        CallTraceExport.WriteParamsCsv(Sample(), none);
        Assert.Equal(string.Join(",", CallTraceExport.ParamsCsvColumns) + "\r\n", none.ToString());
    }

    [Fact]
    public void The_calls_csv_does_not_gain_the_parameter_columns()
    {
        Assert.Equal("func_unloaded", CallTraceExport.CsvColumns[^2]);
        Assert.Equal("func_reused", CallTraceExport.CsvColumns[^1]);
        Assert.Equal(17, CallTraceExport.CsvColumns.Length);
        var w = new StringWriter();
        CallTraceExport.WriteCsv(SnapSample(), w);
        var lines = w.ToString().Split("\r\n", StringSplitOptions.RemoveEmptyEntries);
        Assert.Equal(7, lines.Length);   // the header and the six calls, nothing per parameter
        Assert.All(lines, l => Assert.Equal(17, l.Split(',').Length));   // no name here holds a comma
    }
}
