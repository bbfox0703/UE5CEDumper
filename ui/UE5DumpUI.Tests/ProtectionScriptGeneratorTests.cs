using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the GodMode (Solitar) memory-record AA Script shape: a STATEFUL toggle
/// driving the Mimic mailbox CMD_PROTECT(9) op SET_GODMODE(0) — tick writes
/// value 1 (force bCanBeDamaged FALSE), untick writes value 0 (restore). The op
/// rides in instanceAddr (0x10), the value in ufuncAddr (0x18), and the command
/// is written LAST to 0x00. Self-contained (no helper-file dependency).
/// </summary>
public class ProtectionScriptGeneratorTests
{
    [Fact]
    public void Generate_emits_enable_and_disable_blocks()
    {
        var s = ProtectionScriptGenerator.Generate();
        Assert.Contains("[ENABLE]", s);
        Assert.Contains("[DISABLE]", s);
    }

    [Fact]
    public void Generate_enables_on_tick_and_disables_on_untick()
    {
        var s = ProtectionScriptGenerator.Generate();
        Assert.Contains("writeQword(mb + 0x18, 1)", s);   // [ENABLE] value = ON
        Assert.Contains("writeQword(mb + 0x18, 0)", s);   // [DISABLE] value = OFF
    }

    [Fact]
    public void Both_blocks_select_the_set_godmode_op()
    {
        var s = ProtectionScriptGenerator.Generate();
        var ops = s.Split("writeQword(mb + 0x10, 0)").Length - 1;
        Assert.Equal(2, ops);   // PROTECT_OP_SET_GODMODE=0, one per block
    }

    [Fact]
    public void Both_blocks_trigger_the_protect_mailbox_command()
    {
        var s = ProtectionScriptGenerator.Generate();
        var triggers = s.Split("writeInteger(mb + 0x00, 9)").Length - 1;
        Assert.Equal(2, triggers);   // CMD_PROTECT=9, one per block
    }

    [Fact]
    public void Disable_block_is_not_a_nop()
    {
        var s = ProtectionScriptGenerator.Generate();
        var disableIdx = s.IndexOf("[DISABLE]", System.StringComparison.Ordinal);
        Assert.True(disableIdx > 0);
        var disableBlock = s.Substring(disableIdx);
        Assert.Contains("writeQword(mb + 0x18, 0)", disableBlock);
        Assert.DoesNotContain("-- nop", disableBlock);
    }

    [Fact]
    public void Is_self_contained_no_helper_file_dependency()
    {
        var s = ProtectionScriptGenerator.Generate();
        Assert.DoesNotContain("findTableFile", s);
        Assert.Contains("g_invokeMailbox", s);
    }

    [Fact]
    public void Generate_is_lf_only_for_ce_compatibility()
    {
        Assert.DoesNotContain("\r", ProtectionScriptGenerator.Generate());
    }

    // [W2-CEGEN-MODAL] Unticking must not put a modal over a fullscreen game (MailboxTimeout.SilentReturn's own doc).
    // Both blocks came from one emitter, so every [ENABLE] bail -- the missing mailbox, the contract check (two
    // messages), the idle wait, the timeout, the result check -- was a showMessage in [DISABLE] too.
    [Fact]
    public void Disable_block_never_pops_a_modal()
    {
        var s = ProtectionScriptGenerator.Generate();
        var disable = s[s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)..];
        Assert.DoesNotContain("showMessage", disable);
        Assert.Contains("dbg('[GodMode]", disable);   // a DEBUG session still sees why it gave up
    }

    // GODMODE-FAILED-TICK-LATCHES-WANT (review of [DBGCAM-ENABLE-BLOCKED-UNTICK-REENTERS-DISABLE]): Solitar::SetGodMode
    // stores the want BEFORE it applies anything and keeps the re-assert worker running whatever it answered, so a
    // negative answer to a tick is armed, not nothing -- the hold engages as soon as a pawn resolves, which the UI card
    // calls "ON (pending)". Unticking that row without running [DISABLE] left a live hold under an OFF row. The row
    // stays ticked and says how to cancel. Negative control: put any untick back in this branch and this fails.
    [Fact]
    public void A_tick_answered_negative_stays_ticked_because_the_hold_is_armed()
    {
        var s = ProtectionScriptGenerator.Generate();
        var enable = s[..s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)];
        int b = enable.IndexOf("if state < 0 then", System.StringComparison.Ordinal);
        Assert.True(b >= 0, "no state < 0 branch");
        var branch = enable[b..enable.IndexOf("\nelseif", b, System.StringComparison.Ordinal)];
        Assert.DoesNotContain("Active = false", branch);
        Assert.DoesNotContain("disableWithoutExecute", branch);
        Assert.Contains("armed", branch);
        Assert.Contains("untick", branch);
    }

    // The control: ticking keeps every announced, unticking bail -- the fix is [DISABLE]-only.
    [Fact]
    public void Enable_block_still_announces_and_unticks_its_bails()
    {
        var s = ProtectionScriptGenerator.Generate();
        var enable = s[..s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)];
        Assert.Contains("showMessage('[GodMode] g_invokeMailbox not found", enable);
        Assert.Contains("memrec.Active = false", enable);
    }
}
