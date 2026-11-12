using System.Text;

namespace UE5DumpUI.Services;

/// <summary>
/// The one emitter for the "wait until the injected DLL is actually up" Lua.
///
/// <para>Two call sites need it — <see cref="CeInjectScriptGenerator"/> (an
/// [ENABLE]/[DISABLE] memory record pushed into the user's open table) and
/// <see cref="CeAutorunScriptGenerator"/> (a standalone file for CE's autorun
/// folder) — and a third lives in <c>scripts/UE5CEDumper.CT</c>. The loop is the
/// fiddly part, so it is emitted from here rather than copied: the offsets, the
/// timeouts, and above all the two non-obvious properties below must stay
/// identical everywhere.</para>
///
/// <list type="number">
///   <item><b>Pure memory read, never <c>executeCodeEx</c>.</b> Reading
///     <c>g_invokeMailbox</c> touches only the target's memory. <c>executeCodeEx</c>
///     needs <c>CreateRemoteThread</c>, which games block during start-up — which is
///     exactly when this loop runs.</item>
///   <item><b>The symbol is resolved INSIDE the loop.</b> Cheat Engine's symbol
///     handler may not have picked up the just-injected module on the first try; a
///     single failed <c>getAddress</c> up-front would abort a perfectly healthy
///     start-up.</item>
/// </list>
///
/// <para>Callers own the verdict. On exit the emitted Lua leaves three locals in
/// scope: <c>mb</c> (mailbox address, or <c>nil</c> if the symbol never appeared),
/// <c>state</c> (a <see cref="CeMailboxLayout"/> <c>Init*</c> value) and
/// <c>waited</c> (elapsed ms). <c>INIT_READY</c> / <c>INIT_FAILED</c> /
/// <c>INIT_SKIPPED</c> are also in scope for the verdict to compare against.</para>
/// </summary>
public static class CeReadinessLua
{
    /// <summary>Mailbox poll interval while waiting for start-up to finish.</summary>
    public const int PollIntervalMs = 250;

    /// <summary>Upper bound on the readiness wait. Higher than the standalone
    /// <c>.CT</c>'s historical flat 15 s budget because this loop only ever
    /// *reaches* the bound when something is genuinely wedged — the normal path
    /// exits the moment the DLL publishes READY.</summary>
    public const int ReadyTimeoutMs = 25000;

    /// <summary>How long to keep retrying <c>getAddress('g_invokeMailbox')</c>
    /// before concluding the symbol will never appear (a DLL build predating the
    /// readiness flag, or a symbol handler that is not cooperating).</summary>
    public const int SymbolGraceMs = 5000;

    /// <summary>
    /// Emit the shared constants and the poll loop. See the class remarks for the
    /// locals left in scope afterwards.
    /// </summary>
    public static void AppendPollLoop(StringBuilder sb, string indent = "")
    {
        Line(sb, indent, "-- Wait by POLLING the DLL's initState, not by sleeping a fixed budget.");
        Line(sb, indent, "-- This is a pure memory read via the exported g_invokeMailbox symbol --");
        Line(sb, indent, "-- NOT executeCodeEx, which needs CreateRemoteThread and is exactly what");
        Line(sb, indent, "-- games block during start-up.");
        Line(sb, indent, $"local INIT_READY, INIT_FAILED, INIT_SKIPPED = {CeMailboxLayout.InitReady}, " +
                         $"{CeMailboxLayout.InitFailed}, {CeMailboxLayout.InitSkipped}");
        Line(sb, indent, $"local mb, waited, state = nil, 0, {CeMailboxLayout.InitIdle}");
        Line(sb, indent, $"while waited < {ReadyTimeoutMs} do");
        Line(sb, indent, "  if mb == nil then");
        // Resolve inside the loop — CE's symbol handler may not see the fresh module yet.
        // BOTH spellings: depending on how CE picked up the module, an export is reachable
        // bare or only as "<module>.<name>", and which one works is not predictable from
        // here. lessons-learned.md mandates both; a single-spelling miss silently degrades
        // this poll back to the blind fixed wait it exists to replace. (B33)
        Line(sb, indent, "    for _, sym in ipairs({'g_invokeMailbox', 'UE5Dumper.g_invokeMailbox'}) do");
        Line(sb, indent, "      local okSym, addr = pcall(getAddress, sym)");
        Line(sb, indent, "      if okSym and addr and addr ~= 0 then mb = addr break end");
        Line(sb, indent, "    end");
        Line(sb, indent, $"    if mb == nil and waited >= {SymbolGraceMs} then");
        Line(sb, indent, "      break   -- symbol never appeared");
        Line(sb, indent, "    end");
        Line(sb, indent, "  end");
        Line(sb, indent, "  if mb ~= nil then");
        Line(sb, indent, $"    local okRead, v = pcall(readInteger, mb + {CeMailboxLayout.OffInitState})");
        Line(sb, indent, $"    state = (okRead and v) or {CeMailboxLayout.InitIdle}");
        Line(sb, indent, "    if state == INIT_READY or state == INIT_FAILED or state == INIT_SKIPPED then");
        Line(sb, indent, "      break");
        Line(sb, indent, "    end");
        Line(sb, indent, "  end");
        Line(sb, indent, $"  sleep({PollIntervalMs})");
        Line(sb, indent, $"  waited = waited + {PollIntervalMs}");
        Line(sb, indent, "end");
    }

    /// <summary>The three failure messages, shared so both routes report the same
    /// diagnosis for the same state. Returned as Lua string expressions (already
    /// quoted and concatenated), ready to hand to <c>showMessage</c> or
    /// <c>print</c>.</summary>
    public static string SymbolNeverAppearedMessage =>
        "'[UE5CEDumper] The DLL loaded but never published its state.\\n\\n' ..\n" +
        "    'This build may predate the readiness flag. Check the DLL log:\\n' ..\n" +
        "    '  %LOCALAPPDATA%\\\\UE5CEDumper\\\\Logs'";

    /// <summary>INIT_FAILED: the scan was stopped part-way (UE5_Init aborted; the pipe server may still have started),
    /// or the pipe server refused to start while the previous stop was still finishing. The script cannot tell which,
    /// so the headline names both ([CT-INITFAILED-HEADLINE]: it used to claim init finished, the opposite of the
    /// aborted case). A second copy of UE5Dumper never causes it: that publishes READY or SKIPPED.</summary>
    public static string InitFailedMessage =>
        "'[UE5CEDumper] The DLL loaded, but its start-up did not finish: the scan was cut short, or the Pipe Server did not start.\\n\\n' ..\n" +
        "    'Most likely the scan was stopped part-way (the record was unticked while it ran), or the record was ticked again before the previous stop had finished.\\n' ..\n" +
        "    'Check the DLL log:\\n  %LOCALAPPDATA%\\\\UE5CEDumper\\\\Logs'";

    /// <summary>Ran out of time still IDLE/RUNNING — a timeout is an ERROR, never a
    /// shrug: the historical <c>.CT</c> printed "complete (or failed)" and taught
    /// users to ignore it.</summary>
    public static string TimedOutMessage =>
        "'[UE5CEDumper] Timed out waiting for the DLL to finish starting up.\\n\\n' ..\n" +
        "    'The AOB scan may be wedged, or the game may be blocking our thread.\\n' ..\n" +
        "    'Check the DLL log:\\n  %LOCALAPPDATA%\\\\UE5CEDumper\\\\Logs'";

    private static void Line(StringBuilder sb, string indent, string text)
    {
        sb.Append(indent).Append(text).Append('\n');
    }
}
