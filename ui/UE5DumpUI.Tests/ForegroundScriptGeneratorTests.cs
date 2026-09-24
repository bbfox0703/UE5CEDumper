using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the Keep-Foreground (Grausam) memory-record AA Script shape: a STATEFUL
/// toggle driving the Mimic mailbox CMD_FOREGROUND(12) op FG_OP_SET(0) — tick
/// writes value 1 (game always foreground), untick writes value 0. The op rides
/// in instanceAddr (0x10), the value in ufuncAddr (0x18), and the command is
/// written LAST to 0x00. Self-contained (no helper-file dependency).
/// </summary>
public class ForegroundScriptGeneratorTests
{
    [Fact]
    public void Generate_emits_enable_and_disable_blocks()
    {
        var s = ForegroundScriptGenerator.Generate();
        Assert.Contains("[ENABLE]", s);
        Assert.Contains("[DISABLE]", s);
    }

    [Fact]
    public void Generate_enables_on_tick_and_disables_on_untick()
    {
        var s = ForegroundScriptGenerator.Generate();
        Assert.Contains("writeQword(mb + 0x18, 1)", s);   // [ENABLE] value = ON
        Assert.Contains("writeQword(mb + 0x18, 0)", s);   // [DISABLE] value = OFF
    }

    [Fact]
    public void Both_blocks_select_the_set_op()
    {
        var s = ForegroundScriptGenerator.Generate();
        var ops = s.Split("writeQword(mb + 0x10, 0)").Length - 1;
        Assert.Equal(2, ops);   // FG_OP_SET=0, one per block
    }

    [Fact]
    public void Both_blocks_trigger_the_foreground_mailbox_command()
    {
        var s = ForegroundScriptGenerator.Generate();
        var triggers = s.Split("writeInteger(mb + 0x00, 12)").Length - 1;
        Assert.Equal(2, triggers);   // CMD_FOREGROUND=12, one per block
    }

    [Fact]
    public void Disable_block_is_not_a_nop()
    {
        var s = ForegroundScriptGenerator.Generate();
        var disableIdx = s.IndexOf("[DISABLE]", System.StringComparison.Ordinal);
        Assert.True(disableIdx > 0);
        var disableBlock = s.Substring(disableIdx);
        Assert.Contains("writeQword(mb + 0x18, 0)", disableBlock);
        Assert.DoesNotContain("-- nop", disableBlock);
    }

    [Fact]
    public void Is_self_contained_no_helper_file_dependency()
    {
        var s = ForegroundScriptGenerator.Generate();
        Assert.DoesNotContain("findTableFile", s);
        Assert.Contains("g_invokeMailbox", s);
    }

    [Fact]
    public void Generate_is_lf_only_for_ce_compatibility()
    {
        Assert.DoesNotContain("\r", ForegroundScriptGenerator.Generate());
    }

    // [R7-C-04] GodMode's [W2-CEGEN-MODAL] shape: an untick never puts a modal over the game, and a failed tick's
    // deferred untick (which runs [DISABLE]) no longer shows a SECOND dialog.
    [Fact]
    public void Disable_block_never_pops_a_modal()
    {
        var s = ForegroundScriptGenerator.Generate();
        var disable = s[s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)..];
        Assert.DoesNotContain("showMessage", disable);
        Assert.Contains("dbg('[KeepForeground", disable);   // a DEBUG session still sees why it gave up
    }

    // The control: ticking still announces and unticks its bails -- the fix is [DISABLE]-only.
    [Fact]
    public void Enable_block_still_announces_and_unticks_its_bails()
    {
        var s = ForegroundScriptGenerator.Generate();
        var enable = s[..s.IndexOf("[DISABLE]", System.StringComparison.Ordinal)];
        Assert.Contains("g_invokeMailbox not found", enable);
        Assert.Contains("showMessage", enable);
        Assert.Contains("memrec.Active = false", enable);
    }

    // [R7-C-04] The same shape in ue5_invoke_helper.lua's Debug Camera example, which users copy into a table.
    [Fact]
    public void Invoke_helper_debug_camera_example_disable_is_quiet()
    {
        var src = File.ReadAllText(NumericInputCoercionTests.RepoFile("scripts/ue5_invoke_helper.lua")).Replace("\r\n", "\n");
        int at = src.IndexOf("pcall(setDebugCamera, 0)", StringComparison.Ordinal);
        Assert.True(at >= 0, "the Debug Camera example's [DISABLE] call is gone");
        var tail = src.Substring(at, src.IndexOf("{$asm}", at, StringComparison.Ordinal) - at);
        Assert.DoesNotContain("showMessage", tail);
        Assert.Contains("(UE5_DEBUG or 0) ~= 0", tail);
    }
}
