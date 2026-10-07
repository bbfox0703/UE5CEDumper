using UE5DumpUI.Helpers;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [LIVEFUNCS-TIMELINE-2026-10-04] What the Call Trace tab does with a built trace: which calls are visible as the
/// user expands and collapses, the keyword filter (space = AND, the shared matcher), and the two exports.
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
}
