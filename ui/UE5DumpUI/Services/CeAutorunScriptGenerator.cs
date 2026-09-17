using System.Text;

namespace UE5DumpUI.Services;

/// <summary>
/// Generates a standalone Lua file for Cheat Engine's <c>autorun\</c> folder.
/// CE executes everything in that folder at start-up, so the globals defined here
/// exist in <b>every</b> table, permanently — the only delivery route that needs
/// neither the standalone <c>UE5CEDumper.CT</c> nor the AOBMaker plugin.
///
/// <para><b>The safety property that shapes this file: it only DEFINES things at
/// load time.</b> Autorun runs before any process is attached, so anything
/// process-dependent (<c>injectDLL</c>, <c>getOpenedProcessID</c>,
/// <c>readInteger</c>) must sit inside a function the user invokes later, never at
/// file scope. Doing otherwise would fire on every CE launch against no
/// process.</para>
///
/// <para><b>The menu item is best-effort.</b> Adding to <c>getMainForm().Menu</c>
/// is the documented CE pattern (and is what the vendored <c>UE4 Dumper.CT</c>
/// does), but it runs earlier here than in any table script. It is therefore
/// wrapped in <c>pcall</c>: if the form isn't ready, the menu is simply absent and
/// <c>ue5_inject()</c> still works from the Lua console. A cosmetic extra must
/// never be able to break the user's CE start-up.</para>
///
/// <para>Emitted quiet by default per the project's CE-Lua hygiene rule — but with
/// NO auto-close: this file has no window of its own, and closing the Lua Engine
/// out from under someone who opened it deliberately would be hostile.</para>
/// </summary>
public static class CeAutorunScriptGenerator
{
    /// <summary>File name to write into <c>&lt;CheatEngine&gt;\autorun\</c>.</summary>
    public const string DefaultFileName = "ue5_autorun.lua";

    /// <summary>Sub-folder of the Cheat Engine install that CE scans at start-up.</summary>
    public const string AutorunFolderName = "autorun";

    /// <summary>Caption of the CE main-menu item this file tries to add.</summary>
    public const string MenuCaption = "UE5CEDumper: Inject DLL";

    /// <summary>
    /// Build the autorun Lua.
    /// </summary>
    /// <param name="dllPath">Absolute path to <c>UE5Dumper.dll</c>, baked in — CE
    /// has no way to find our install on its own, and a run-time directory search
    /// from autorun would be both slow and fragile.</param>
    public static string Generate(string dllPath)
    {
        var sb = new StringBuilder(6144);

        Line(sb, "-- ================================================================");
        Line(sb, $"-- {CeLuaHygiene.Attribution}");
        Line(sb, "--");
        Line(sb, "-- Cheat Engine autorun helper. Drop this in <CheatEngine>\\autorun\\ and");
        Line(sb, "-- CE runs it at start-up, so ue5_inject() / ue5_shutdown() exist in");
        Line(sb, "-- EVERY table -- no need to open UE5CEDumper.CT, no AOBMaker plugin.");
        Line(sb, "--");
        Line(sb, "-- Usage: attach CE to the game, then either click");
        Line(sb, $"--   \"{MenuCaption}\" in the main menu, or run ue5_inject() in the");
        Line(sb, "--   Lua console (Ctrl+Alt+L).");
        Line(sb, "--");
        Line(sb, "-- Set UE5_DEBUG = 1 in the Lua console for verbose progress output.");
        Line(sb, "--");
        Line(sb, "-- This file DEFINES functions only. Nothing here touches a process at");
        Line(sb, "-- load time -- autorun runs before any process is attached.");
        Line(sb, "-- ================================================================");
        Line(sb);
        // LATE-BOUND on purpose. This file is loaded by CE's autorun at start-up, so the
        // eager preamble would bind DEBUG to whatever UE5_DEBUG was before the Lua console
        // even existed — making line 61's own instruction ("set UE5_DEBUG = 1 in the Lua
        // console") impossible to follow. (B23)
        CeLuaHygiene.AppendLateBoundDebugPreamble(sb);
        Line(sb);
        Line(sb, $"local DLL_PATH = '{CeLuaHygiene.EscapeLuaString(dllPath)}'");
        Line(sb);

        // Emitted INSIDE each function, not hoisted to file scope. The helper's body
        // names executeCodeEx, and this file's load-time invariant is checked
        // textually ("nothing process-dependent at top level"): a definition does not
        // run, but it does read as top-level work, and that invariant is worth more
        // than the handful of duplicated lines it costs.
        EmitInject(sb);
        Line(sb);
        EmitShutdown(sb);
        Line(sb);
        EmitMenu(sb);
        Line(sb);

        // Syntax highlighting is cosmetic — never let it break start-up.
        Line(sb, "pcall(registerLuaFunctionHighlight, 'ue5_inject')");
        Line(sb, "pcall(registerLuaFunctionHighlight, 'ue5_shutdown')");
        Line(sb, "dbg('[UE5CEDumper] autorun loaded -- ue5_inject() is available in every table')");

        return sb.ToString();
    }

    private static void EmitInject(StringBuilder sb)
    {
        Line(sb, "-- Inject UE5Dumper.dll and wait until its pipe server is actually up.");
        Line(sb, "-- Returns true on success, false (after showing why) on failure.");
        Line(sb, "function ue5_inject()");
        CeLuaHygiene.AppendCallDllHelper(sb, indent: "  ");
        Line(sb);
        Line(sb, "  if getOpenedProcessID() == 0 then");
        Line(sb, "    showMessage('[UE5CEDumper] No game process is attached.\\n\\n' ..");
        Line(sb, "      'Attach Cheat Engine to the running game first (File > Open Process).')");
        Line(sb, "    return false");
        Line(sb, "  end");
        Line(sb);
        // Re-injecting would double-map us and fight over the pipe. But "already
        // loaded" splits two ways, and initState is what separates them — see the
        // pushed-record twin in CeInjectScriptGenerator for the full reasoning.
        Line(sb, "  local okGet, probe = pcall(getAddress, 'UE5_Init')");
        Line(sb, "  local alreadyLoaded = okGet and probe and probe ~= 0");
        Line(sb, "  if alreadyLoaded then");
        Line(sb, $"    local READY, SKIPPED = {CeMailboxLayout.InitReady}, {CeMailboxLayout.InitSkipped}");
        // BOTH spellings — see the twin in CeInjectScriptGenerator. These two sites
        // were the last holdouts of the B33 rule; a single-spelling miss leaves `pre`
        // nil, so a serving DLL is misread as "parked" and UE5_AutoStart is fired at
        // a pipe that is already up.
        Line(sb, "    local mbNow = getAddressSafe('g_invokeMailbox')");
        Line(sb, "    if not mbNow or mbNow == 0 then mbNow = getAddressSafe('UE5Dumper.g_invokeMailbox') end");
        Line(sb, "    local pre = nil");
        Line(sb, "    if mbNow and mbNow ~= 0 then");
        Line(sb, $"      local okRead, v = pcall(readInteger, mbNow + {CeMailboxLayout.OffInitState})");
        Line(sb, "      pre = okRead and v or nil");
        Line(sb, "    end");
        Line(sb, "    if pre == READY or pre == SKIPPED then");
        Line(sb, "      showMessage('[UE5CEDumper] Already loaded and serving in this process.\\n\\n' ..");
        Line(sb, "        'No injection needed -- just launch UE5DumpUI.exe and click Connect.')");
        Line(sb, "      return true");
        Line(sb, "    end");
        // Parked by a previous ue5_shutdown(): revive in place rather than re-injecting.
        Line(sb, "    dbg('[UE5CEDumper] loaded but parked -- restarting via UE5_AutoStart')");
        Line(sb, "    if not callDLL('UE5_AutoStart') then");
        Line(sb, "      showMessage('[UE5CEDumper] The DLL is loaded but could not be restarted.\\n\\n' ..");
        Line(sb, "        'UE5_AutoStart did not run -- the game may be blocking remote threads.\\n' ..");
        Line(sb, "        'Restart the game to get a clean state.')");
        Line(sb, "      return false");
        Line(sb, "    end");
        Line(sb, "  end");
        Line(sb);
        Line(sb, "  if not alreadyLoaded then");
        Line(sb, "    dbg('[UE5CEDumper] injecting ' .. DLL_PATH)");
        Line(sb, "    if not injectDLL(DLL_PATH) then");
        Line(sb, "      showMessage('[UE5CEDumper] injectDLL failed.\\n\\n' ..");
        Line(sb, "        'Possible causes:\\n' ..");
        Line(sb, "        '  1. The DLL was moved -- expected at:\\n     ' .. DLL_PATH .. '\\n' ..");
        Line(sb, "        '  2. Anti-cheat is blocking injection\\n' ..");
        Line(sb, "        '  3. Cheat Engine needs to run as administrator')");
        Line(sb, "      return false");
        Line(sb, "    end");
        Line(sb, "  end");
        Line(sb);
        CeReadinessLua.AppendPollLoop(sb, indent: "  ");
        Line(sb);
        Line(sb, "  if mb == nil then");
        Line(sb, $"    showMessage({Indent(CeReadinessLua.SymbolNeverAppearedMessage)})");
        Line(sb, "    return false");
        Line(sb, "  elseif state == INIT_FAILED then");
        Line(sb, $"    showMessage({Indent(CeReadinessLua.PipeFailedMessage)})");
        Line(sb, "    return false");
        Line(sb, "  elseif state ~= INIT_READY and state ~= INIT_SKIPPED then");
        Line(sb, $"    showMessage({Indent(CeReadinessLua.TimedOutMessage)})");
        Line(sb, "    return false");
        Line(sb, "  end");
        Line(sb);
        Line(sb, "  if state == INIT_SKIPPED then");
        // SKIPPED is not an error: a pipe server IS up, owned by another instance.
        Line(sb, "    dbg('[UE5CEDumper] another instance already owns the pipe -- proceeding')");
        Line(sb, "  else");
        Line(sb, "    dbg(string.format('[UE5CEDumper] ready in %.1f sec', waited / 1000))");
        Line(sb, "  end");
        Line(sb, "  dbg('[UE5CEDumper] pipe: \\\\\\\\.\\\\pipe\\\\UE5DumpBfx -- launch UE5DumpUI.exe and click Connect')");
        Line(sb, "  return true");
        Line(sb, "end");
    }

    // ⛔⛔ DO NOT SET `UE5_StartedByThisRecord` ANYWHERE IN THIS GENERATOR, and do not
    // "align" it with CeInjectScriptGenerator. That flag is a CE Lua GLOBAL, so it is
    // shared by every chunk in the session — and this script is an autorun, not a
    // memory record. If autorun injected and set it, the pushed inject record's
    // enable would then find the DLL SERVING, untick itself, and its disable block
    // would read a flag set by somebody else and tear the pipe down. That is exactly
    // [B30-REOPEN-2026-09-10] reintroduced through the back door. The flag means
    // "the inject RECORD's own enable brought this up", and only that enable may set it.
    //
    // ⚠ This function is also NOT the B30 shape: it is reached from the CE menu, i.e.
    // the user explicitly asked to shut down, rather than from an untick that CE fired
    // on their behalf. Consent is the difference, so it keeps the plain symbol probe.
    private static void EmitShutdown(StringBuilder sb)
    {
        Line(sb, "-- Stop the pipe server and tear the DLL down. executeCodeEx is fine here");
        Line(sb, "-- (unlike during injection): by now the game is running normally, so");
        Line(sb, "-- CreateRemoteThread works.");
        Line(sb, "function ue5_shutdown()");
        CeLuaHygiene.AppendCallDllHelper(sb, indent: "  ");
        Line(sb);
        Line(sb, "  local okProbe, probe = pcall(getAddress, 'UE5_StopPipeServer')");
        Line(sb, "  if not (okProbe and probe and probe ~= 0) then");
        Line(sb, "    dbg('[UE5CEDumper] nothing loaded -- nothing to shut down')");
        Line(sb, "    return true");
        Line(sb, "  end");
        Line(sb);
        // UE5_Shutdown alone — see the twin in CeInjectScriptGenerator for why
        // calling UE5_StopPipeServer first was both redundant and harmful.
        Line(sb, "  local b = callDLL('UE5_Shutdown')");
        Line(sb, "  dbg('[UE5CEDumper] shutdown: ' .. tostring(b))");
        Line(sb, "  if not b then");
        Line(sb, "    print('[UE5CEDumper] shutdown did not complete cleanly -- check the DLL log.')");
        Line(sb, "    return false");
        Line(sb, "  end");
        Line(sb, "  return true");
        Line(sb, "end");
    }

    private static void EmitMenu(StringBuilder sb)
    {
        Line(sb, "-- Best-effort main-menu entry. Wrapped in pcall on purpose: this runs");
        Line(sb, "-- earlier than any table script, so if the main form is not ready the");
        Line(sb, "-- menu is simply absent and ue5_inject() still works from the Lua");
        Line(sb, "-- console. A cosmetic extra must not break CE start-up.");
        Line(sb, "-- The guard also makes a manual re-run of this file idempotent.");
        Line(sb, "if not ue5_menuAdded then");
        Line(sb, "  local ok, err = pcall(function()");
        Line(sb, "    local parent = getMainForm().Menu.Items");
        Line(sb, "    local item = createMenuItem(parent)");
        Line(sb, "    parent.add(item)");
        Line(sb, $"    item.Caption = '{CeLuaHygiene.EscapeLuaString(MenuCaption)}'");
        Line(sb, "    item.OnClick = function() ue5_inject() end");
        Line(sb, "    ue5_menuAdded = true");
        Line(sb, "  end)");
        Line(sb, "  if not ok then");
        Line(sb, "    dbg('[UE5CEDumper] menu item unavailable (' .. tostring(err) ..");
        Line(sb, "        ') -- use ue5_inject() from the Lua console')");
        Line(sb, "  end");
        Line(sb, "end");
    }

    /// <summary>Re-indent a shared multi-line message expression by one level, so a
    /// body nested inside a function still lines up.</summary>
    private static string Indent(string luaExpr) => luaExpr.Replace("\n    ", "\n      ");

    /// <summary>Append a line with LF-only ending (no CR) for CE compatibility.</summary>
    private static void Line(StringBuilder sb, string text = "")
    {
        sb.Append(text);
        sb.Append('\n');
    }
}
