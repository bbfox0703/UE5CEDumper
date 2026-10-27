using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the Debug Camera memory-record AA Script shape: a STATEFUL toggle
/// (tick = setDebugCamera(1), untick = setDebugCamera(0)), both blocks loading
/// the embedded helper. The defining difference from BakedScriptGenerator is
/// that [DISABLE] is NOT a nop here — it actively turns the camera off.
/// </summary>
public class DebugCameraScriptGeneratorTests
{
    [Fact]
    public void Generate_emits_enable_and_disable_blocks()
    {
        var s = DebugCameraScriptGenerator.Generate();
        Assert.Contains("[ENABLE]", s);
        Assert.Contains("[DISABLE]", s);
    }

    [Fact]
    public void Generate_enables_on_tick_and_disables_on_untick()
    {
        var s = DebugCameraScriptGenerator.Generate();
        Assert.Contains("writeQword(mb + 0x10, 1)", s);   // [ENABLE] request = ON
        Assert.Contains("writeQword(mb + 0x10, 0)", s);   // [DISABLE] request = OFF
    }

    [Fact]
    public void Both_blocks_trigger_the_set_debug_camera_mailbox_command()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var triggers = s.Split("writeInteger(mb + 0x00, 7)").Length - 1;
        Assert.Equal(2, triggers);   // CMD_SET_DEBUG_CAMERA=7, one per block
    }

    [Fact]
    public void Disable_block_is_not_a_nop()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var disableIdx = s.IndexOf("[DISABLE]", System.StringComparison.Ordinal);
        Assert.True(disableIdx > 0);
        var disableBlock = s.Substring(disableIdx);
        // The whole point: unticking the record forces the camera OFF.
        Assert.Contains("writeQword(mb + 0x10, 0)", disableBlock);
        Assert.DoesNotContain("-- nop", disableBlock);
    }

    [Fact]
    public void Is_self_contained_no_helper_file_dependency()
    {
        // The fragile part of the old design was depending on the embedded
        // ue5_invoke_helper.lua (which could be stale). The mailbox round-trip
        // is inlined so the record works with just the DLL injected.
        var s = DebugCameraScriptGenerator.Generate();
        Assert.DoesNotContain("findTableFile", s);
        Assert.Contains("g_invokeMailbox", s);
    }

    [Fact]
    public void Generate_is_lf_only_for_ce_compatibility()
    {
        Assert.DoesNotContain("\r", DebugCameraScriptGenerator.Generate());
    }

    // [W2-CEGEN-MODAL] Unticking must not put a modal over a fullscreen game (MailboxTimeout.SilentReturn's own doc).
    // Both blocks came from one emitter, so every [ENABLE] bail -- the missing mailbox, the contract check (two
    // messages), the idle wait, the timeout, the result check -- was a showMessage in [DISABLE] too.
    [Fact]
    public void Disable_block_never_pops_a_modal()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var disable = s[s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)..];
        Assert.DoesNotContain("showMessage", disable);
        Assert.Contains("dbg('[DebugCamera]", disable);   // a DEBUG session still sees why it gave up
    }

    // The control: ticking keeps every announced, unticking bail -- the fix is [DISABLE]-only.
    [Fact]
    public void Enable_block_still_announces_and_unticks_its_bails()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var enable = s[..s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)];
        Assert.Contains("showMessage('[DebugCamera] g_invokeMailbox not found", enable);
        Assert.Contains("memrec.Active = false", enable);
    }

    // [W3-DEBUGCAM-QUEUED] -5: the toggle STAYS QUEUED and will run. Read as "state ~= 1", [ENABLE] said the game refused
    // and UNTICKED -- and a second tick queued a second toggle, the two draining ON then OFF. EveryEnableBailout's 8-line
    // window cannot tell: the failure branch's untick sits right below the queued message. This pins the branch itself.
    [Fact]
    public void A_queued_toggle_is_reported_and_never_unticks()
    {
        var s = DebugCameraScriptGenerator.Generate();
        int d = s.IndexOf("[DISABLE]", System.StringComparison.Ordinal);
        var enable = s[..d];
        int q = enable.IndexOf("if state == -5 then", System.StringComparison.Ordinal);
        Assert.True(q >= 0, "no queued branch in [ENABLE]");
        int f = enable.IndexOf("elseif state ~= 1 then", q, System.StringComparison.Ordinal);
        Assert.True(f > q, "the queued test must come BEFORE the failure test, or -5 reads as a failure");
        var queued = enable[q..f];
        Assert.Contains("showMessage('[DebugCamera] ON queued", queued);
        Assert.DoesNotContain("memrec.Active = false", queued);    // nothing to untick: the toggle WILL run
        Assert.DoesNotContain(CeLuaHygiene.CloseCall, queued);     // not a clean success either

        var disable = s[d..];
        Assert.Contains("if state == -5 then", disable);
    }

    // [DEBUGCAM-QUEUED-OFF-NOESCALATE] (c): a queued OFF drains as a toggle on the original controller's CheatManager,
    // which on stock UE 4.18-5.7 can only enable, so the camera stays ON. The untick used to dbg() "the toggle will run"
    // -- silent by default -- and end UNTICKED over a camera still on. It now says so ungated, and RAISES: CE's
    // TMemoryRecord.setActive sets fActive only when the script succeeds, so a raised [DISABLE] leaves the record ticked.
    [Fact]
    public void A_queued_OFF_says_so_and_leaves_the_record_ticked()
    {
        var s = DebugCameraScriptGenerator.Generate();
        var disable = s[s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)..];
        int q = disable.IndexOf("if state == -5 then", System.StringComparison.Ordinal);
        Assert.True(q >= 0, "no queued branch in [DISABLE]");
        int f = disable.IndexOf("elseif state ~= 0 then", q, System.StringComparison.Ordinal);
        Assert.True(f > q, "the queued test must come BEFORE the failure test");
        var queued = disable[q..f];
        Assert.Contains("print('[DebugCamera] OFF did not complete", queued);   // ungated: a real problem
        Assert.DoesNotContain("dbg(", queued);
        Assert.DoesNotContain("will run", queued);
        Assert.Contains("error('[DebugCamera]", queued);                        // keeps the record ticked
        Assert.DoesNotContain("showMessage(", queued);                         // no modal on an untick
        Assert.DoesNotContain(CeLuaHygiene.CloseCall, queued);
    }

    // [DEBUGCAM-QUEUED-OFF-NOESCALATE] (review): ue5_invoke_helper.lua's copy-me Debug Camera example kept the old
    // [DISABLE] -- pcall(setDebugCamera, 0), reporting only a failed pcall -- so a queued OFF unticked silently over a
    // camera that stays on. scripts/tests/invoke_helper_test.lua RUNS the block; this pins its shape where no CE Lua
    // host is built.
    [Fact]
    public void Invoke_helper_example_disable_says_a_queued_OFF_and_keeps_the_record_ticked()
    {
        var src = System.IO.File.ReadAllText(NumericInputCoercionTests.RepoFile("scripts/ue5_invoke_helper.lua"))
                                .Replace("\r\n", "\n");
        int at = src.IndexOf("pcall(setDebugCamera, 0)", System.StringComparison.Ordinal);
        Assert.True(at >= 0, "the Debug Camera example's [DISABLE] call is gone");
        var block = src.Substring(at, src.IndexOf("{$asm}", at, System.StringComparison.Ordinal) - at);
        int q = block.IndexOf("state == -5", System.StringComparison.Ordinal);
        Assert.True(q >= 0, "no queued branch in the example's [DISABLE]");
        int end = block.IndexOf("\n    end", q, System.StringComparison.Ordinal);
        Assert.True(end > q, "the queued branch has no end");
        var queued = block[q..end];
        Assert.Contains("print('[Debug Camera] OFF did not complete", queued);
        Assert.DoesNotContain("UE5_DEBUG", queued);                            // ungated: a real problem
        Assert.Contains("error('[Debug Camera]", queued);                      // keeps the record ticked
        Assert.DoesNotContain("showMessage(", queued);                         // no modal on an untick
    }
}
