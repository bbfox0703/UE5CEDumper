using System.Text;

namespace UE5DumpUI.Services;

/// <summary>
/// Generates the bootstrap AA Script that injects <c>UE5Dumper.dll</c> and waits
/// for it to come up — the same job as the <c>[ENABLE]</c> block of the
/// standalone <c>scripts/UE5CEDumper.CT</c>, but as a memory record that can be
/// pushed into the table the user ALREADY has open (via the AOBMaker plugin's
/// <c>CreateAAScript</c>, or pasted as CE record XML).
///
/// <para><b>Why this exists.</b> Cheat Engine holds one table at a time, so the
/// standalone <c>.CT</c> forces a two-stage load: open ours to inject, then open
/// the game's own table — at which point the injection entry is gone. Pushing the
/// same logic into the user's table removes that entirely. The standalone
/// <c>.CT</c> stays shipped and supported as the developer / no-AOBMaker path.</para>
///
/// <para><b>Readiness, not a fixed sleep.</b> <c>[ENABLE]</c> polls the DLL's
/// <c>initState</c> in the Mimic mailbox (<see cref="CeMailboxLayout.OffInitState"/>)
/// every <see cref="CeReadinessLua.PollIntervalMs"/> ms. That is a pure memory read through the
/// exported <c>g_invokeMailbox</c> symbol — deliberately NOT <c>executeCodeEx</c>,
/// which needs <c>CreateRemoteThread</c> and is exactly what games block during
/// start-up. A timeout is an ERROR (the window stays open), never a silent
/// "probably fine".</para>
///
/// <para><b>Symbol resolution runs inside the poll loop</b>, not once up-front:
/// CE's symbol handler may not have picked up the just-injected module yet, and a
/// single failed <c>getAddress</c> would otherwise abort a perfectly healthy
/// start-up.</para>
///
/// <para><c>[DISABLE]</c> shuts the DLL down via <c>executeCodeEx</c>, which is
/// safe there: by then the game is running normally, so remote threads work.</para>
/// </summary>
public static class CeInjectScriptGenerator
{
    /// <summary>Description used for the CE address-list record.</summary>
    public const string RecordDescription = "UE5CEDumper: Inject DLL + Start Pipe Server";

    /// <summary>Group folder the record is nested under in CE's address list, so a
    /// pushed bootstrap doesn't litter the user's own table root.</summary>
    public const string RecordGroup = "UE5CEDumper (DLL)";

    /// <summary>Description of the inert reminder row pushed beside the bootstrap record.
    /// Short enough to read in CE's list without widening the column; the detail lives in
    /// the script, which shows it if the row is ticked.</summary>
    public const string ReminderDescription =
        "* Click back into the GAME window before ticking these -- commands run on the game thread";

    /// <summary>
    /// An inert row whose only job is to be READ. Every mailbox command in this table is
    /// dispatched on the game thread, so while the game is paused, alt-tabbed or sitting on
    /// a breakpoint nothing runs and the script eventually times out. That is invisible from
    /// inside Cheat Engine, and it cost a real debugging session before it was understood.
    ///
    /// <para>Two things this warns about, and the second is the one that surprises people:
    /// a timed-out command is NOT cancelled. The DLL still holds it and runs it as soon as
    /// the game ticks again -- measured on DumperTest 2026-08-07, a teleport completed 35 s
    /// after it was sent and 25 s after the script had given up on it. Clicking again after
    /// a timeout therefore QUEUES a second one.</para>
    ///
    /// <para>It applies itself to nothing, so per the table-wide rule it unticks itself
    /// rather than sitting ticked and claiming to be active.</para>
    /// </summary>
    public static string GenerateReminder()
    {
        var sb = new StringBuilder(1024);
        Line(sb, "[ENABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        Line(sb, $"-- {CeLuaHygiene.Attribution}");
        // Paragraph breaks come from string.char(10), not from a "\n" escape. The escape
        // has to survive C# source -> emitted Lua -> CE's XML-encoded <AssemblerScript>,
        // and it is the kind of thing that silently arrives as a literal backslash-n in
        // the dialog. string.char(10) has no escaping to lose.
        Line(sb, "local NL = string.char(10)");
        Line(sb, "showMessage(");
        Line(sb, "  'Mailbox commands are dispatched on the GAME THREAD.' .. NL .. NL ..");
        Line(sb, "  'While the game is paused, alt-tabbed or stopped on a breakpoint the game ' ..");
        Line(sb, "  'thread does not tick, so nothing runs and the script times out. Click back ' ..");
        Line(sb, "  'into the game window first.' .. NL .. NL ..");
        Line(sb, "  'A timed-out command is NOT cancelled: the DLL still holds it and runs it as ' ..");
        Line(sb, "  'soon as the game ticks again -- one teleport landed 35 seconds late. So do ' ..");
        Line(sb, "  'NOT click again after a timeout, or you will queue a second one.' .. NL .. NL ..");
        Line(sb, "  'Memory scans and the value list are unaffected -- they read memory directly.')");
        // Applies nothing, so it must not leave the row ticked.
        Line(sb, CeLuaHygiene.DeferredUntickLua(""));
        Line(sb, "{$asm}");
        Line(sb, "[DISABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        Line(sb, "{$asm}");
        return sb.ToString();
    }

    /// <summary>
    /// Build the [ENABLE]/[DISABLE] memory-record script.
    /// </summary>
    /// <param name="dllPath">Absolute path to <c>UE5Dumper.dll</c>. Baked into the
    /// script, so — unlike the standalone <c>.CT</c> — no directory search is
    /// needed at run time: the UI already knows where its own DLL lives.</param>
    public static string Generate(string dllPath)
    {
        var sb = new StringBuilder(4096);
        EmitEnable(sb, dllPath);
        EmitDisable(sb);
        return sb.ToString();
    }

    private static void EmitEnable(StringBuilder sb, string dllPath)
    {
        Line(sb, "[ENABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        CeLuaHygiene.AppendDebugPreamble(sb);
        Line(sb, "-- ================================================================");
        Line(sb, $"-- Inject UE5Dumper.dll + start the pipe server -- {CeLuaHygiene.Attribution}");
        Line(sb, "-- Tick to inject; untick to shut the DLL down again.");
        Line(sb, "-- Pushed into YOUR table, so you do not have to open the standalone");
        Line(sb, "-- UE5CEDumper.CT first (Cheat Engine holds one table at a time).");
        Line(sb, "-- After it turns green: launch UE5DumpUI.exe and click Connect.");
        Line(sb, "-- ================================================================");
        Line(sb);

        // ── 0. CE must be attached to a process ──
        Line(sb, "if getOpenedProcessID() == 0 then");
        Line(sb, "  showMessage('[UE5CEDumper] No game process is attached.\\n\\n' ..");
        Line(sb, "    'Attach Cheat Engine to the running game first (File > Open Process).')");
        Line(sb, CeLuaHygiene.DeferredUntickLua("  "));
        Line(sb, "  return");
        Line(sb, "end");
        Line(sb);

        CeLuaHygiene.AppendCallDllHelper(sb);
        Line(sb);

        // ── 0.5. Already loaded? Two opposite reasons, and they need opposite handling ──
        Line(sb, "-- Already present? Injecting again would double-map us and fight over the");
        Line(sb, "-- pipe -- but 'already present' covers two cases that must NOT be treated");
        Line(sb, "-- alike, and the mailbox's initState is what tells them apart:");
        // NB: never write the literal "[DISABLE]" in an ENABLE-block comment — the
        // tests (and CE) slice the script on that marker, so it would truncate the
        // block here.
        Line(sb, "--   * SERVING (READY/SKIPPED): a proxy DLL deployed it, or another instance");
        Line(sb, "--     owns the pipe. Not ours. Untick, so the disable block cannot tear down");
        Line(sb, "--     a pipe this record never started.");
        Line(sb, "--   * PARKED (anything else): this record was ticked, unticked -- which runs");
        Line(sb, "--     UE5_Shutdown and leaves initState at IDLE -- and is now being re-ticked.");
        Line(sb, "--     Revive it in place: the DLL is still mapped, so re-injecting is wrong.");
        Line(sb, "local okGet, probe = pcall(getAddress, 'UE5_Init')");
        Line(sb, "local alreadyLoaded = okGet and probe and probe ~= 0");
        Line(sb, "if alreadyLoaded then");
        Line(sb, $"  local INIT_READY, INIT_SKIPPED = {CeMailboxLayout.InitReady}, {CeMailboxLayout.InitSkipped}");
        Line(sb, "  local okSym, mbNow = pcall(getAddress, 'g_invokeMailbox')");
        Line(sb, "  local pre = nil");
        Line(sb, "  if okSym and mbNow and mbNow ~= 0 then");
        Line(sb, $"    local okRead, v = pcall(readInteger, mbNow + {CeMailboxLayout.OffInitState})");
        Line(sb, "    pre = okRead and v or nil");
        Line(sb, "  end");
        Line(sb, "  if pre == INIT_READY or pre == INIT_SKIPPED then");
        Line(sb, "    dbg('[UE5CEDumper] already loaded AND serving -- not ours to manage')");
        Line(sb, "    showMessage('[UE5CEDumper] Already loaded and serving in this process.\\n\\n' ..");
        Line(sb, "      'No injection needed -- just launch UE5DumpUI.exe and click Connect.')");
        // Untick: this record did not start that pipe, so its [DISABLE] must never
        // be allowed to run UE5_Shutdown against it (audit #4 B30).
        Line(sb, CeLuaHygiene.DeferredUntickLua("    "));
        Line(sb, "    return");
        Line(sb, "  end");
        Line(sb, "  dbg('[UE5CEDumper] loaded but parked -- restarting via UE5_AutoStart')");
        Line(sb, "  if not callDLL('UE5_AutoStart') then");
        Line(sb, "    showMessage('[UE5CEDumper] The DLL is loaded but could not be restarted.\\n\\n' ..");
        Line(sb, "      'UE5_AutoStart did not run -- the game may be blocking remote threads.\\n' ..");
        Line(sb, "      'Restart the game to get a clean state.')");
        Line(sb, CeLuaHygiene.DeferredUntickLua("    "));
        Line(sb, "    return");
        Line(sb, "  end");
        Line(sb, "end");
        Line(sb);

        // ── 1. Inject (only when it is not already mapped) ──
        Line(sb, $"local DLL_PATH = '{CeLuaHygiene.EscapeLuaString(dllPath)}'");
        Line(sb, "if not alreadyLoaded then");
        Line(sb, "  dbg('[UE5CEDumper] injecting ' .. DLL_PATH)");
        Line(sb, "  if not injectDLL(DLL_PATH) then");
        Line(sb, "    showMessage('[UE5CEDumper] injectDLL failed.\\n\\n' ..");
        Line(sb, "      'Possible causes:\\n' ..");
        Line(sb, "      '  1. The DLL was moved -- expected at:\\n     ' .. DLL_PATH .. '\\n' ..");
        Line(sb, "      '  2. Anti-cheat is blocking injection\\n' ..");
        Line(sb, "      '  3. Cheat Engine needs to run as administrator')");
        Line(sb, CeLuaHygiene.DeferredUntickLua("    "));
        Line(sb, "    return");
        Line(sb, "  end");
        Line(sb, "end");
        Line(sb);

        // ── 2. Poll for readiness (shared emitter — see CeReadinessLua) ──
        CeReadinessLua.AppendPollLoop(sb);
        Line(sb);

        // ── 3. Report. Every failure path returns BEFORE the success-close. ──
        Line(sb, "if mb == nil then");
        Line(sb, $"  showMessage({CeReadinessLua.SymbolNeverAppearedMessage})");
        Line(sb, "  return");
        Line(sb, "elseif state == INIT_FAILED then");
        Line(sb, $"  showMessage({CeReadinessLua.PipeFailedMessage})");
        Line(sb, "  return");
        Line(sb, "elseif state ~= INIT_READY and state ~= INIT_SKIPPED then");
        Line(sb, $"  showMessage({CeReadinessLua.TimedOutMessage})");
        Line(sb, "  return");
        Line(sb, "end");
        Line(sb);
        Line(sb, "if state == INIT_SKIPPED then");
        // SKIPPED is not an error: a pipe server IS up, owned by another instance.
        Line(sb, "  dbg('[UE5CEDumper] another instance already owns the pipe -- proceeding')");
        Line(sb, "else");
        Line(sb, "  dbg(string.format('[UE5CEDumper] ready in %.1f sec', waited / 1000))");
        Line(sb, "end");
        Line(sb, "dbg('[UE5CEDumper] pipe: \\\\\\\\.\\\\pipe\\\\UE5DumpBfx -- launch UE5DumpUI.exe and click Connect')");
        CeLuaHygiene.AppendCloseOnSuccess(sb);
        Line(sb, "{$asm}");
    }

    private static void EmitDisable(StringBuilder sb)
    {
        Line(sb, "[DISABLE]");
        Line(sb, "{$lua}");
        Line(sb, "if syntaxcheck then return end");
        CeLuaHygiene.AppendDebugPreamble(sb);
        Line(sb, "-- Stop the pipe server and tear the DLL down. executeCodeEx is fine");
        Line(sb, "-- here (unlike during start-up): by now the game is running normally,");
        Line(sb, "-- so CreateRemoteThread works.");
        Line(sb);
        // [ENABLE]'s early bail-outs set memrec.Active = false, which makes CE run
        // THIS block against a DLL that was never loaded. That is a no-op, not a
        // failure — don't shout about it.
        Line(sb, "-- [ENABLE]'s early bail-outs untick the record, which makes CE run this");
        Line(sb, "-- block even though nothing was ever injected. Detect that and stay quiet:");
        Line(sb, "-- 'nothing to shut down' is not a failure.");
        Line(sb, "local okProbe, probe = pcall(getAddress, 'UE5_StopPipeServer')");
        Line(sb, "if not (okProbe and probe and probe ~= 0) then");
        Line(sb, "  dbg('[UE5CEDumper] nothing loaded -- nothing to shut down')");
        CeLuaHygiene.AppendCloseOnSuccess(sb, indent: "  ");
        Line(sb, "  return");
        Line(sb, "end");
        Line(sb);
        CeLuaHygiene.AppendCallDllHelper(sb);
        Line(sb);
        // UE5_Shutdown ALONE. It is `s_pipeServer.Stop()` plus everything else, and
        // it runs that Stop deliberately AFTER Stark::Shutdown so a pipe thread
        // blocked in EnqueueInvoke gets its -7 and unwinds. Calling
        // UE5_StopPipeServer first inverted that ordering, and — because the CE
        // call times out while the remote thread keeps running — put a second
        // teardown into the process concurrently with the first.
        Line(sb, "local b = callDLL('UE5_Shutdown')");
        Line(sb, "dbg('[UE5CEDumper] shutdown: ' .. tostring(b))");
        // The DLL stays mapped: FreeLibrary on an injected DLL mid-game isn't worth
        // the risk. Re-ticking is a real restart now, not a shrug: UE5_Shutdown parks
        // initState at IDLE, [ENABLE] reads that as "parked" and calls UE5_AutoStart,
        // which re-arms Tot's shutdown latch and Mimic's poller (audit #4 B1(b) —
        // before that fix the mailbox thread could only ever be started from DllMain,
        // so a Disable was unrecoverable without restarting the game).
        Line(sb, "if not b then");
        Line(sb, "  print('[UE5CEDumper] shutdown did not complete cleanly -- check the DLL log.')");
        Line(sb, "else");
        CeLuaHygiene.AppendCloseOnSuccess(sb, indent: "  ");
        Line(sb, "end");
        Line(sb, "{$asm}");
    }

    /// <summary>Append a line with LF-only ending (no CR) for CE compatibility.</summary>
    private static void Line(StringBuilder sb, string text = "")
    {
        sb.Append(text);
        sb.Append('\n');
    }
}
