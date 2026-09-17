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
        Assert.Contains("dbg('[DebugCamera] OFF queued", disable);
    }
}
