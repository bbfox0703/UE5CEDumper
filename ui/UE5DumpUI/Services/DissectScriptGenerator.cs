using System.Text;

namespace UE5DumpUI.Services;

/// <summary>
/// [AOBM-DISSECT-INJECT] Generates the CE memory record that turns <c>ue5_dissect.lua</c>'s auto mode on and off:
/// while it is ticked, Cheat Engine's Structure Dissect fills itself from UE reflection for any UObject address.
/// <para>The record loads the script from the CE table (<c>findTableFile</c>), where Tools → "Add Auto Structure
/// Dissect" puts it through the AOBMaker plugin's <c>InjectTableFile</c>. It never reads the disk, so a table saved
/// with both carries the feature to another machine.</para>
/// <para><c>[ENABLE]</c> is a stateful toggle, so every bail-out that registered nothing unticks the record and keeps
/// the Lua Engine window open on its reason; only a clean enable closes it. <c>[DISABLE]</c> reports a failed
/// unregister ungated, because the callbacks would then still be live.</para>
/// </summary>
public static class DissectScriptGenerator
{
    /// <summary>The file's name inside the CE table; <see cref="DissectLuaResource.DefaultFileName"/> is this.</summary>
    public const string TableFileName = "ue5_dissect.lua";

    /// <summary>Description of the record in CE's address list.</summary>
    public const string RecordDescription = "UE5CEDumper: Auto Structure Dissect (UObjects)";

    /// <summary>The group the record is pushed into: the one the DLL bootstrap uses, so UE5CEDumper's own records
    /// stay together instead of landing among the user's.</summary>
    public const string RecordGroup = CeInjectScriptGenerator.RecordGroup;

    /// <summary>The Lua global the loaded module is kept in. <c>[DISABLE]</c> is a separate chunk, so a local
    /// could not reach it; it also lets the user drive the module from CE's Lua console.</summary>
    public const string ModuleGlobal = "UE5Dissect";

    /// <summary>The DLL export whose absence means "not injected". The module's first call on any dissect is to
    /// it, so it is the one to probe.</summary>
    private const string ProbeExport = "UE5_GetObjectClass";

    private const string SetupHint = "Setup: UE5DumpUI -> Tools -> Add Auto Structure Dissect to Current CE Table";

    public static string Generate()
    {
        var sb = new StringBuilder(4096);

        Line(sb, "[ENABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        CeLuaHygiene.AppendDebugPreamble(sb);
        CeLuaHygiene.AppendAttribution(sb);
        Line(sb, "-- ================================================================");
        Line(sb, "-- UE5 Auto Structure Dissect");
        Line(sb, "-- While this is ticked, Structure Dissect fills itself from UE");
        Line(sb, "-- reflection for any UObject address. Needs UE5Dumper.dll in the");
        Line(sb, $"-- game and {TableFileName} embedded in this table.");
        Line(sb, $"-- Lua console, while ticked: {ModuleGlobal}.createInteractive()");
        Line(sb, "-- ================================================================");
        Line(sb, $"local probe = getAddressSafe('{ProbeExport}')");
        Line(sb, "if not probe or probe == 0 then");
        CeLuaHygiene.AppendFailedEnable(sb,
            "'[UE5Dissect] UE5Dumper.dll is not loaded in this game.\\n\\n' ..\n" +
            "    'Inject it first, then tick this again.'", "  ");
        Line(sb, "end");
        Line(sb, $"local tf = findTableFile('{TableFileName}')");
        Line(sb, "if not tf then");
        CeLuaHygiene.AppendFailedEnable(sb,
            $"'[UE5Dissect] {TableFileName} not found in this table.\\n\\n' ..\n" +
            $"    '{SetupHint}'", "  ");
        Line(sb, "end");
        Line(sb, "local ss = createStringStream()");
        Line(sb, "ss.copyFrom(tf.Stream, tf.Stream.Size)");
        Line(sb, "local fn, err = load(ss.DataString)");
        Line(sb, "ss.destroy()");
        Line(sb, "if not fn then");
        CeLuaHygiene.AppendFailedEnable(sb,
            $"'[UE5Dissect] {TableFileName} load error:\\n' .. tostring(err)", "  ");
        Line(sb, "end");
        // The chunk RETURNS its API table; the invoke and freeze helpers define globals instead.
        Line(sb, "local ok, mod = pcall(fn)");
        Line(sb, "if not ok or type(mod) ~= 'table' or type(mod.enableAutoCallback) ~= 'function' then");
        CeLuaHygiene.AppendFailedEnable(sb,
            $"'[UE5Dissect] {TableFileName} did not load:\\n' .. tostring(mod)", "  ");
        Line(sb, "end");
        Line(sb, $"{ModuleGlobal} = mod");
        Line(sb, "local eok, eerr = pcall(mod.enableAutoCallback)");
        Line(sb, "if not eok then");
        CeLuaHygiene.AppendFailedEnable(sb,
            "'[UE5Dissect] enableAutoCallback failed:\\n' .. tostring(eerr)", "  ");
        Line(sb, "end");
        Line(sb, "dbg('[UE5Dissect] auto Structure Dissect on')");
        CeLuaHygiene.AppendCloseOnSuccess(sb);
        Line(sb, "{$asm}");
        Line(sb, "[DISABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        CeLuaHygiene.AppendDebugPreamble(sb);
        Line(sb, "local disOk, disErr = true, nil");
        Line(sb, $"if {ModuleGlobal} and type({ModuleGlobal}.disableAutoCallback) == 'function' then");
        Line(sb, $"  disOk, disErr = pcall({ModuleGlobal}.disableAutoCallback)");
        Line(sb, "end");
        Line(sb, "if not disOk then");
        Line(sb, "  print('[UE5Dissect] the auto-dissect callbacks are still registered: ' .. tostring(disErr))");
        Line(sb, "end");
        Line(sb, "dbg('[UE5Dissect] auto Structure Dissect off')");
        CeLuaHygiene.AppendCloseOnSuccess(sb, "disOk");
        Line(sb, "{$asm}");

        return sb.ToString();
    }

    private static void Line(StringBuilder sb, string text = "") => sb.Append(text).Append('\n');
}
