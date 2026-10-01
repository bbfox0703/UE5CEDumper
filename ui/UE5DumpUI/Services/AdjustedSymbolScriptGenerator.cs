using System.Globalization;
using System.Text;

namespace UE5DumpUI.Services;

/// <summary>
/// [AOBM-GWORLD-GENAOB] A CE symbol script for a pointer whose signature ADJUSTS the RIP target (Himmel's
/// <c>adjustment</c>, e.g. GOBJ_AV1's -0x10 on Avowed). The plugin's <c>CreateSymbolScript</c> registers the raw RIP
/// target and cannot add a constant (AOBMaker R17, unanswered), so this does what it does and then adds the adjustment:
/// scan the module for the AOB, read the instruction's disp32, register <c>hit + aobLen + disp + adjustment</c>. It
/// rescans on every enable, so it survives restarts, as the plugin's script does.
/// <para>The scan is Lua (<c>AOBScanModuleUnique</c>, CE 7.5 and 7.7), not an AA <c>aobscanmodule</c>: CE runs a
/// script's <c>{$lua}</c> blocks before it assembles the AA lines, so Lua could not read an AA scan's result.</para>
/// <para>The record is enabled THROUGH the plugin, so a failure raises (<c>error</c>) instead of a <c>showMessage</c>:
/// a modal there holds the plugin's pipe ([AOBM-TRAINER-SETUP-MODAL]). CE refuses the activation and the plugin reports
/// the reason; an enable that applied nothing is not left ticked.</para>
/// </summary>
public static class AdjustedSymbolScriptGenerator
{
    public static string Generate(string symbolName, string module, string aob, int pos, int aobLen, int adjustment)
    {
        string sym = CeLuaHygiene.EscapeLuaString(symbolName);
        string mod = CeLuaHygiene.EscapeLuaString(module);
        string adj = adjustment < 0
            ? "-0x" + (-(long)adjustment).ToString("X", CultureInfo.InvariantCulture)
            : "0x" + adjustment.ToString("X", CultureInfo.InvariantCulture);
        var sb = new StringBuilder(2048);
        Line(sb, "[ENABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        CeLuaHygiene.AppendDebugPreamble(sb);
        CeLuaHygiene.AppendAttribution(sb);
        Line(sb, $"-- Registers {symbolName}: the address the instruction this AOB matches reads RIP-relatively, {adj}, as");
        Line(sb, "-- the signature UE5CEDumper found it with adjusts it. Rescans on every enable, so it survives restarts.");
        Line(sb, $"local hit = AOBScanModuleUnique('{mod}', '{CeLuaHygiene.EscapeLuaString(aob)}', '+X')");
        Line(sb, "if not hit then");
        Line(sb, $"  error('[UE5CEDumper] {sym}: the AOB was not found in {mod}', 0)");
        Line(sb, "end");
        Line(sb, $"local disp = readInteger(hit + {pos.ToString(CultureInfo.InvariantCulture)}, true)");
        Line(sb, "if not disp then");
        Line(sb, $"  error(string.format('[UE5CEDumper] {sym}: could not read the instruction at %X', hit), 0)");
        Line(sb, "end");
        Line(sb, $"local addr = hit + {aobLen.ToString(CultureInfo.InvariantCulture)} + disp + ({adj})");
        Line(sb, $"unregisterSymbol('{sym}')");
        Line(sb, $"registerSymbol('{sym}', addr)");
        Line(sb, $"dbg(string.format('[UE5CEDumper] {sym} = %X', addr))");
        CeLuaHygiene.AppendCloseOnSuccess(sb);
        Line(sb, "{$asm}");
        Line(sb, "[DISABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        CeLuaHygiene.AppendDebugPreamble(sb);
        Line(sb, $"unregisterSymbol('{sym}')");
        CeLuaHygiene.AppendCloseOnSuccess(sb);
        Line(sb, "{$asm}");
        return sb.ToString();
    }

    private static void Line(StringBuilder sb, string text) => sb.Append(text).Append('\n');
}
