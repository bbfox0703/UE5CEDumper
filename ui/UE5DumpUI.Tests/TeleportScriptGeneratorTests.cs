using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the per-action Teleport AA records (docs/teleport-spec.md §9.2B):
/// momentary records that fire the mailbox round-trip then auto-untick, with
/// [DISABLE] a nop. Plus the standard 7-row .CT batch.
/// </summary>
public class TeleportScriptGeneratorTests
{
    [Fact]
    public void Generate_is_lf_only()
    {
        var s = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.Save, 0);
        Assert.DoesNotContain("\r", s);
    }

    [Fact]
    public void Save_record_uses_op_1_and_slot()
    {
        var s = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.Save, 2);
        Assert.Contains("writeQword(mb + 0x18, 2)", s);   // slot
        Assert.Contains("writeQword(mb + 0x10, 1)", s);   // op SAVE
        Assert.Contains("writeInteger(mb + 0x00, 8)", s); // CMD_TELEPORT
    }

    [Fact]
    public void Recall_record_uses_op_2()
    {
        var s = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.Recall, 0);
        Assert.Contains("writeQword(mb + 0x10, 2)", s);
    }

    [Fact]
    public void RecallLast_record_uses_op_7()
    {
        var s = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.RecallLast);
        Assert.Contains("writeQword(mb + 0x10, 7)", s);   // op RECALL_LAST
        Assert.Contains("writeInteger(mb + 0x00, 8)", s); // CMD_TELEPORT
        Assert.Contains("Recall last", s);
    }

    [Fact]
    public void BugIt_record_uses_op_9_and_BugItGo_uses_op_10()
    {
        var bugit = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.BugIt);
        Assert.Contains("writeQword(mb + 0x10, 9)", bugit);    // op BUGIT_SAVE
        Assert.Contains("writeInteger(mb + 0x00, 8)", bugit);  // CMD_TELEPORT

        var bugitgo = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.BugItGo);
        Assert.Contains("writeQword(mb + 0x10, 10)", bugitgo); // op BUGIT_GO
    }

    [Fact]
    public void Cursor_record_uses_op_4_and_bakes_params()
    {
        var s = TeleportScriptGenerator.Generate(
            TeleportScriptGenerator.Action.Cursor, 0, zOffset: 150.0, channel: 1, fallbackCenter: true);
        Assert.Contains("writeQword(mb + 0x10, 4)", s);
        Assert.Contains("writeDouble(mb + 0x328, 150.0)", s);
        Assert.Contains("writeBytes(mb + 0x330, 1)", s);
        Assert.Contains("writeBytes(mb + 0x331, 1)", s);
    }

    [Fact]
    public void Record_auto_unticks_and_disable_is_nop()
    {
        var s = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.Save, 0);
        Assert.Contains("createTimer", s);
        Assert.Contains("memrec.Active = false", s);
        Assert.Contains("[ENABLE]", s);
        Assert.Contains("[DISABLE]", s);
        var disable = s.Substring(s.IndexOf("[DISABLE]", System.StringComparison.Ordinal));
        Assert.Contains("nothing to undo", disable);
    }

    [Fact]
    public void GetPov_record_uses_op_11_and_prints_camera()
    {
        var s = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.GetPov);
        Assert.Contains("writeQword(mb + 0x10, 11)", s);   // op GET_POV
        Assert.Contains("writeInteger(mb + 0x00, 8)", s);  // CMD_TELEPORT
        Assert.Contains("Get camera POV", s);
        Assert.Contains("readDouble(mb + 0x328)", s);      // reads the POV block back
        Assert.Contains("camera loc=", s);
    }

    [Fact]
    public void Relative_record_uses_op_12_and_bakes_distance_and_mode()
    {
        var horiz = TeleportScriptGenerator.Generate(
            TeleportScriptGenerator.Action.Relative, distance: 250.0, horizontal: true);
        Assert.Contains("writeQword(mb + 0x10, 12)", horiz);     // op RELATIVE
        Assert.Contains("writeDouble(mb + 0x328, 250.0)", horiz); // distance
        Assert.Contains("writeBytes(mb + 0x330, 0)", horiz);      // mode 0 = horizontal

        var d3 = TeleportScriptGenerator.Generate(
            TeleportScriptGenerator.Action.Relative, distance: -50.0, horizontal: false);
        Assert.Contains("writeBytes(mb + 0x330, 1)", d3);         // mode 1 = 3D
        Assert.Contains("writeDouble(mb + 0x328, -50.0)", d3);    // negative = backward
    }

    [Fact]
    public void Explicit_record_uses_op_13_and_bakes_coords()
    {
        var s = TeleportScriptGenerator.Generate(
            TeleportScriptGenerator.Action.Explicit, coordX: 10.0, coordY: 20.0, coordZ: 30.0,
            hasRot: true, pitch: 1.0, yaw: 2.0, roll: 3.0);
        Assert.Contains("writeQword(mb + 0x10, 13)", s);     // op EXPLICIT
        Assert.Contains("writeDouble(mb + 0x328, 10.0)", s); // X
        Assert.Contains("writeDouble(mb + 0x338, 30.0)", s); // Z
        Assert.Contains("writeBytes(mb + 0x358, 1)", s);     // hasRot
    }

    [Fact]
    public void Cursor_on_off_records_use_op_14_with_show_flag()
    {
        var on = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.CursorOn);
        Assert.Contains("writeQword(mb + 0x10, 14)", on);   // op SET_CURSOR
        Assert.Contains("writeQword(mb + 0x18, 1)", on);    // show = 1

        var off = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.CursorOff);
        Assert.Contains("writeQword(mb + 0x10, 14)", off);
        Assert.Contains("writeQword(mb + 0x18, 0)", off);   // show = 0
    }

    [Fact]
    public void GetPose_record_uses_op_0_and_prints_coords()
    {
        var s = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.GetPose);
        Assert.Contains("writeQword(mb + 0x10, 0)", s);    // op GET_POSE
        Assert.Contains("writeInteger(mb + 0x00, 8)", s);  // CMD_TELEPORT
        Assert.Contains("Get current coords", s);
        Assert.Contains("readDouble(mb + 0x328)", s);      // reads the pose block back
        Assert.Contains("coords loc=", s);
    }

    [Fact]
    public void BuildBatchRows_returns_seventeen_teleport_rows()
    {
        var rows = TeleportScriptGenerator.BuildBatchRows();
        Assert.Equal(17, rows.Count);
        Assert.All(rows, r => Assert.Equal("Teleport", r.Category));
        // 3 saves, 3 recalls, recall-last, BugIt, BugItGo, cursor, Get POV,
        // Get coords, TP facing dir, TP to coords, Cursor ON/OFF, clear-all.
        Assert.All(rows, r => Assert.IsType<CtScriptRow>(r));
        Assert.Contains(rows, r => r.Description == "Save marker 1");
        Assert.Contains(rows, r => r.Description == "Recall marker 3");
        Assert.Contains(rows, r => r.Description == "Recall last");
        Assert.Contains(rows, r => r.Description == "BugIt (store pose)");
        Assert.Contains(rows, r => r.Description == "BugItGo (go to stored)");
        Assert.Contains(rows, r => r.Description == "Teleport to cursor");
        Assert.Contains(rows, r => r.Description == "Get camera POV");
        Assert.Contains(rows, r => r.Description == "Get current coords");
        Assert.Contains(rows, r => r.Description == "TP facing direction");
        Assert.Contains(rows, r => r.Description == "TP to coordinates");
        Assert.Contains(rows, r => r.Description == "Cursor ON");
        Assert.Contains(rows, r => r.Description == "Cursor OFF");
        Assert.Contains(rows, r => r.Description == "Clear all markers");
    }

    [Fact]
    public void ClearAll_record_loops_slots_with_clear_op()
    {
        var s = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.ClearAll);
        Assert.DoesNotContain("\r", s);
        Assert.Contains("for slot = 0, 2 do", s);
        Assert.Contains("writeQword(mb + 0x10, 6)", s);   // op CLEAR_MARKER
        Assert.Contains("writeInteger(mb + 0x00, 8)", s); // CMD_TELEPORT
        Assert.Contains("createTimer", s);                // auto-untick
    }

    // ---- "Clear all markers" can raise three dialogs for one click (docs/todo.md, amended by [TRACKB-A4-2026-09-10]) ----
    //
    // Both waits leave only their OWN while-loop on failure, so the slot loop ran on: a wedged mailbox on slot 0 asked
    // again for slots 1 and 2 (three dialogs), and a busy idle wait fell through to its slot's write -- the DLL drains
    // the mailbox while CE sits in the modal, so slot 0 was cleared after all, under "markers not cleared". A flag at
    // the top of the loop cannot stop that same-iteration write; leaving the loop right after each wait does, and the
    // deferred untick after the loop still runs. The two-line form is deliberate: `then break end` is the greppable
    // shape of a SILENT break, which No_generator_breaks_a_mailbox_wait_silently forbids, and this one is not silent.

    private static string ClearAllEnable()
    {
        var s = TeleportScriptGenerator.Generate(TeleportScriptGenerator.Action.ClearAll);
        int from = s.IndexOf("{$lua}\n", StringComparison.Ordinal) + "{$lua}\n".Length;
        return s[from..s.IndexOf("{$asm}", from, StringComparison.Ordinal)];
    }

    [Fact]
    public void ClearAll_leaves_the_slot_loop_as_soon_as_either_wait_fails()
    {
        var lines = ClearAllEnable().Split('\n');

        int idle = Find(lines, 0, l => l.Trim() == "while _idleCmd ~= 0 do");
        int gate = Find(lines, idle, l => l.Trim() == $"if readInteger(mb + {CeMailboxLayout.OffCmd}) == 0 then");
        AssertLeavesTheLoopOnHadError(lines, EndOf(lines, idle), gate, "the idle wait");

        int wait = Find(lines, gate, l => l.TrimStart().StartsWith("while _st ~= ", StringComparison.Ordinal));
        int code = Find(lines, wait, l => l.Contains($"local code = readInteger(mb + {CeMailboxLayout.OffResult}",
                                                     StringComparison.Ordinal));
        AssertLeavesTheLoopOnHadError(lines, EndOf(lines, wait), code, "the mailbox wait");
    }

    private static int Find(string[] lines, int from, Func<string, bool> match)
    {
        for (int i = Math.Max(from, 0); i < lines.Length; i++)
            if (match(lines[i])) return i;
        Assert.Fail("the Clear all script no longer has the line this test anchors on");
        return -1;
    }

    /// <summary>The <c>end</c> that closes the block opened on <paramref name="open"/>: the next one at its indent.</summary>
    private static int EndOf(string[] lines, int open)
    {
        int indent = lines[open].Length - lines[open].TrimStart().Length;
        return Find(lines, open + 1, l => l.Trim() == "end" && l.Length - l.TrimStart().Length == indent);
    }

    private static void AssertLeavesTheLoopOnHadError(string[] lines, int loopEnd, int next, string what)
    {
        for (int i = loopEnd + 1; i + 1 < next; i++)
            if (lines[i].Trim() == "if hadError then" && lines[i + 1].Trim() == "break")
                return;
        Assert.Fail($"after {what} fails, the slot loop runs on: nothing between the wait and line {next + 1} "
                    + "('" + lines[next].Trim() + "') leaves it on hadError");
    }

    /// <summary>Run the Clear all record's [ENABLE] on CE's own Lua VM against a fake mailbox. <paramref name="dll"/>:
    /// <c>ok</c> finishes every command at once, <c>wedged</c> takes one and never finishes it.
    /// <paramref name="startCmd"/> is a command already in flight; <paramref name="drainsDuringDialog"/> lets the DLL
    /// finish it while a dialog is open, as it does on its own thread while CE sits in the modal.</summary>
    private static Dictionary<string, string> RunClearAll(string dll, int startCmd, bool drainsDuringDialog)
    {
        string lua = $$"""
            UE5_DEBUG = 0
            local MB, CV = 0x100000, 0x200000
            MEM = {}
            MEM[CV + {{CeMailboxLayout.OffContractMagic}}] = {{CeMailboxLayout.ContractMagic}}
            MEM[CV + {{CeMailboxLayout.OffContractCur}}] = {{CeMailboxLayout.ContractVersion}}
            MEM[CV + {{CeMailboxLayout.OffContractMin}}] = {{CeMailboxLayout.ContractVersion}}
            MEM[MB + {{CeMailboxLayout.OffCmd}}] = {{startCmd}}
            DIALOGS, CLEARS, CLOSED, TIMERS = 0, 0, false, {}
            local T = 0
            function getTickCount() T = T + 50 return T end
            function sleep() end
            function processMessages() end
            function processMessagesPaintOnly() end
            function reinitializeSymbolhandler() end
            function getAddressSafe(n)
              if n == 'g_invokeMailbox' then return MB end
              if n == '{{CeMailboxLayout.ContractSymbol}}' then return CV end
              return nil
            end
            function readInteger(a) return MEM[a] or 0 end
            function writeQword(a, v) MEM[a] = v end
            function writeInteger(a, v)
              MEM[a] = v
              if a == MB + {{CeMailboxLayout.OffCmd}} and v == {{CeMailboxLayout.CmdTeleport}} then
                if MEM[MB + {{CeMailboxLayout.OffInstanceAddr}}] == 6 then CLEARS = CLEARS + 1 end
                if '{{dll}}' == 'wedged' then
                  MEM[MB + {{CeMailboxLayout.OffStatus}}] = {{CeMailboxLayout.StatusProcessing}}
                else
                  MEM[MB + {{CeMailboxLayout.OffResult}}] = 0
                  MEM[MB + {{CeMailboxLayout.OffStatus}}] = {{CeMailboxLayout.StatusDone}}
                  MEM[MB + {{CeMailboxLayout.OffCmd}}] = 0
                end
              end
            end
            function showMessage(m)
              DIALOGS = DIALOGS + 1
              if {{(drainsDuringDialog ? "true" : "false")}} then MEM[MB + {{CeMailboxLayout.OffCmd}}] = 0 end
            end
            function createTimer() local t = { destroy = function() end } TIMERS[#TIMERS + 1] = t return t end
            function synchronize(f) f() end
            function getLuaEngine() return { Close = function() CLOSED = true end } end
            local memrec = { Active = true, ID = 1 }
            local chunk = assert(load('local syntaxcheck,memrec=...\n' .. [==[
            {{ClearAllEnable()}}
            ]==]))
            local ok, err = pcall(chunk, false, memrec)
            for _, t in ipairs(TIMERS) do if t.OnTimer then t.OnTimer(t) end end   -- the deferred untick fires
            print('OK=' .. tostring(ok) .. '|' .. tostring(err))
            print('DIALOGS=' .. DIALOGS)
            print('CLEARS=' .. CLEARS)
            print('ACTIVE=' .. tostring(memrec.Active))
            print('CLOSED=' .. tostring(CLOSED))
            """;
        var (exit, output) = CeLua53Host.Run(lua);
        Assert.True(exit == 0, output);
        return output.Replace("\r\n", "\n").Split('\n', StringSplitOptions.RemoveEmptyEntries)
                     .Where(l => l.Contains('='))
                     .ToDictionary(l => l[..l.IndexOf('=')], l => l[(l.IndexOf('=') + 1)..]);
    }

    [Theory]
    // A wedged command on slot 0: the old loop asked again for slots 1 and 2 -- three dialogs for one click.
    [InlineData("wedged", 0, false, 1)]
    // Busy before slot 0, drained while the dialog was up: the old loop then cleared all three anyway, under a
    // dialog that said they were not cleared.
    [InlineData("ok", CeMailboxLayout.CmdQueryPtr, true, 0)]
    public void ClearAll_says_its_failure_once_and_clears_nothing_after_it(string dll, int startCmd, bool drains,
                                                                          int clears)
    {
        var r = RunClearAll(dll, startCmd, drains);

        Assert.Equal("true|nil", r["OK"]);
        Assert.Equal("1", r["DIALOGS"]);
        Assert.Equal(clears.ToString(System.Globalization.CultureInfo.InvariantCulture), r["CLEARS"]);
        Assert.Equal("false", r["ACTIVE"]);   // the deferred untick still ran
        Assert.Equal("false", r["CLOSED"]);   // and the window stays open on the error
    }

    [Fact]
    public void ClearAll_on_an_idle_mailbox_clears_all_three_and_closes_quietly()
    {
        // The control, green both ways: the clean path the fix must not disturb.
        var r = RunClearAll("ok", 0, false);

        Assert.Equal("true|nil", r["OK"]);
        Assert.Equal("0", r["DIALOGS"]);
        Assert.Equal("3", r["CLEARS"]);
        Assert.Equal("false", r["ACTIVE"]);
        Assert.Equal("true", r["CLOSED"]);
    }

    [Fact]
    public void Batch_rows_build_into_a_valid_cheat_table()
    {
        var rows = TeleportScriptGenerator.BuildBatchRows();
        var ct = CheatTableBuilder.Build("Teleport", rows);
        Assert.Contains("<CheatTable", ct);
        Assert.Contains("Auto Assembler Script", ct);
    }
}
