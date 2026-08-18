using System.Text;

namespace UE5DumpUI.Services;

/// <summary>
/// Shared CE-Lua "hygiene" emit helpers so every generated AA Script follows one
/// rule set:
/// <list type="bullet">
///   <item><b>Quiet by default.</b> Diagnostic <c>print()</c> is replaced by
///     <c>dbg()</c>, which only prints when the <c>DEBUG</c> flag is set. This
///     stops the CE Lua Engine window from popping over Cheat Engine on every
///     enable/disable.</item>
///   <item><b>Errors always surface.</b> Real failures keep using bare
///     <c>print()</c> / <c>showMessage()</c> so the user still sees them, and the
///     success-close is skipped so the window stays readable.</item>
///   <item><b>Auto-close on clean success.</b> When <c>DEBUG == 0</c> and the
///     block finished without error, the Lua Engine window closes itself.</item>
/// </list>
///
/// The flag is a single global master switch with a per-script override:
/// <c>local DEBUG = UE5_DEBUG or 0</c>. Set <c>UE5_DEBUG=1</c> once in CE's Lua
/// console to make every script verbose (and keep every window open), or edit a
/// single script's line to debug just that one. Default (unset) = quiet.
///
/// The emitted <c>dbg</c> definition is byte-identical everywhere so the
/// convention can't drift between generators; each generator places its own
/// success-close (<see cref="AppendCloseOnSuccess"/>) where its lifecycle allows.
/// </summary>
/// <summary>
/// How a generated block ends when its mailbox round-trip times out. The modes are NOT
/// interchangeable — they exist because the repo has several script shapes:
/// <list type="bullet">
/// <item><b>Stateful toggle</b> (Movement / GodMode / Fly / SeeThrough / Foreground /
/// DebugCamera): the record stays ticked for as long as the cheat is on, so a bail-out
/// has to untick it or the row claims to be active.</item>
/// <item><b>Momentary action</b> (Teleport's rows): the record self-unticks from a
/// deferred timer that ALSO suppresses the success-close, so the bail-out must reach
/// that timer — it flags and leaves the loop rather than returning past it.</item>
/// <item><b>Helper function</b> (PointerQuery's <c>query</c>, CoordLibrary's
/// <c>call</c>, Invoke's <c>waitDone</c>): the wait sits inside a <c>local function</c>
/// that reports failure to a CALLER, not to the user. Neither of the shapes above fits
/// — a <c>return</c> would leave the helper, not the block, and the helper must not
/// untick, because the caller may be trying it speculatively (PointerQuery attempts the
/// &amp;GEngine slot and falls back to a snapshot on failure). These two modes carry
/// the diagnosis out by value or by raise instead.</item>
/// </list>
/// </summary>
public enum MailboxTimeout
{
    /// <summary>Stateful toggle: untick the record, then return.</summary>
    UntickAndReturn,
    /// <summary>Momentary action: set <c>hadError</c> and break, leaving the caller's
    /// deferred untick + close-suppression to run.</summary>
    FlagAndBreak,
    /// <summary>A <c>[DISABLE]</c> block: <c>dbg</c> the reason and return. There is no
    /// record to untick (it is already going false) and a dialog over the game on an
    /// untick is noise — but a DEBUG session still gets to see WHY it gave up.</summary>
    SilentReturn,
    /// <summary>A helper function that answers its caller by value: emits
    /// <c>return nil, _msg</c>, matching the <c>nil, reason</c> contract
    /// PointerQuery's <c>query</c> and CoordLibrary's <c>call</c> already have. No
    /// dialog and no untick — the caller owns both, and unticking here would kill a
    /// record on an attempt that is allowed to fail.
    ///
    /// <para>PointerQuery's GameEngine path is why this exists, and it is not a stylistic
    /// preference. A failed <c>&amp;GEngine</c>-SLOT query there is EXPECTED — a game whose
    /// GEngine AOB never validated is supposed to fall through to the <c>UEngine*</c>
    /// snapshot — so announcing the failure inside the wait would pop a dialog on what is
    /// actually the success path. CoordLibrary's would fire it with the picker form still
    /// open.</para>
    ///
    /// <para>The three status cases are still told apart; they are RETURNED rather than
    /// shown, which is the whole distinction from <see cref="SilentReturn"/> — that one
    /// discards the reason, this one delegates it. The reason is emitted UNTAGGED so the
    /// caller's own prefix does not double up into <c>[GWorld] [GWorld] …</c>.</para></summary>
    ReturnReason,
    /// <summary>A helper function that answers by unwinding: emits
    /// <c>error('[tag] ' .. _msg)</c>. Invoke's <c>waitDone</c> is called from two
    /// different frames — the <c>[ENABLE]</c> chunk itself and the picker form's
    /// <c>btnFire.OnClick</c> closure — and a raise is the only bail that aborts BOTH
    /// correctly; a <c>return</c> would resume the caller as though the wait had
    /// succeeded.</summary>
    RaiseError,
}

public static class CeLuaHygiene
{
    /// <summary>The exact Lua expression that closes the CE Lua Engine window.
    /// Kept as a shared constant so tests and every call site agree verbatim.</summary>
    public const string CloseCall = "synchronize(function() getLuaEngine().Close() end)";

    /// <summary>Project home — appended to every generated script's attribution so a
    /// shared .CT can be traced back. Single source of truth for the URL.</summary>
    public const string AttributionUrl = "https://github.com/bbfox0703/UE5CEDumper";

    /// <summary>The full attribution text (no comment prefix): the project name + URL.</summary>
    public const string Attribution = "Generated by UE5CEDumper | " + AttributionUrl;

    /// <summary>Emit the attribution as a Lua line comment. Place once near the top
    /// of a generated script (after the DEBUG preamble). LF-only.</summary>
    public static void AppendAttribution(StringBuilder sb, string indent = "")
    {
        sb.Append(indent).Append("-- ").Append(Attribution).Append('\n');
    }

    /// <summary>Emit the DEBUG preamble. Place it right after
    /// <c>if syntaxcheck then return end</c> in each <c>{$lua}</c> block that
    /// prints diagnostics or auto-closes. Every block is its own Lua chunk in CE,
    /// so locals don't cross blocks — emit this in each block that needs it.</summary>
    public static void AppendDebugPreamble(StringBuilder sb, string indent = "")
    {
        sb.Append(indent)
          .Append("local DEBUG = UE5_DEBUG or 0   -- 1 = show diagnostics + keep this window open\n");
        sb.Append(indent)
          .Append("local function dbg(...) if DEBUG ~= 0 then print(...) end end\n");
    }

    /// <summary>
    /// LATE-BOUND variant of <see cref="AppendDebugPreamble"/>, for a file that is
    /// loaded ONCE and whose functions run much later — CE's autorun folder being the
    /// case that motivated it.
    ///
    /// <para>The eager preamble captures <c>UE5_DEBUG</c> into a local at the point the
    /// chunk runs. In an autorun script that point is CE start-up, so the file's own
    /// instruction — "set UE5_DEBUG = 1 in the Lua console" — could never work: by the
    /// time the console exists, <c>DEBUG</c> has already been fixed at 0 and nothing
    /// re-reads it. Here <c>dbg</c> reads the global on every call instead. (B23)</para>
    ///
    /// <para>Emits no <c>DEBUG</c> local, deliberately: a stale local is exactly the
    /// bug. Anything needing the value at call time uses <c>ue5_debugOn()</c>.</para>
    /// </summary>
    public static void AppendLateBoundDebugPreamble(StringBuilder sb, string indent = "")
    {
        Line(sb, indent, "-- DEBUG is read at CALL time, not at load time: this file is loaded by CE's");
        Line(sb, indent, "-- autorun at start-up, long before the Lua console exists to set UE5_DEBUG.");
        Line(sb, indent, "local function ue5_debugOn() return (UE5_DEBUG or 0) ~= 0 end");
        Line(sb, indent, "local function dbg(...) if ue5_debugOn() then print(...) end end");
    }

    /// <summary>
    /// Emit the bounded "wait for the mailbox to go IDLE" preamble of a round-trip.
    ///
    /// <para>A SINGLE sample of <c>cmd</c> is wrong, and not for a rare-race reason:
    /// <c>SetDone</c>/<c>SetError</c> publish <c>status = DONE</c> <b>before</b> clearing
    /// <c>cmd</c> (deliberately — see Mimic.cpp), so a script that issues two round-trips
    /// back to back exits its status poll and can still observe the PREVIOUS command in
    /// <c>cmd</c>. One read then reports "busy" and abandons a query that would have
    /// worked 1 ms later. <see cref="CeMailboxLayout.MailboxIdleWaitMs"/> is far more
    /// than that window and still fails fast when CE really is mid-command.</para>
    ///
    /// <para><paramref name="onBusy"/> is the Lua statement to run on timeout — the
    /// callers differ (a <c>return nil, msg</c> inside a helper vs. setting
    /// <c>hadError</c> in a flat block), so the emitter does not assume one. (R3)</para>
    ///
    /// <para>The bound is a REAL <c>getTickCount()</c> deadline, for the same reason
    /// <see cref="AppendMailboxWait"/>'s is: this loop also counted <c>sleep(1)</c>
    /// iterations against a millisecond constant, and <c>sleep(1)</c> costs 15.47 ms in
    /// CE. Locals are <c>_idle</c>-prefixed because a generator emits this AND the
    /// status wait into the same Lua scope (TeleportScriptGenerator, CoordLibrary), so
    /// the two must not share names.</para>
    /// </summary>
    public static void AppendIdleWait(StringBuilder sb, string mbExpr, string onBusy,
                                      string indent = "", string? onUnreadable = null)
    {
        Line(sb, indent, "-- Bounded wait for IDLE, not a single sample: the DLL publishes status=DONE");
        Line(sb, indent, "-- BEFORE clearing cmd, so a second back-to-back command can still see the");
        Line(sb, indent, "-- previous one for an instant and would spuriously report 'busy'.");
        // Feature test, NOT a getCEVersion() gate. A version threshold is a proxy for the
        // thing we actually need, and it would have to be guessed: this is absent from the
        // whole 7.5 source tree and present in the 7.7 binary, so the introducing version
        // is unknown, and a fork or a backport would defeat it either way. Referencing an
        // undefined global in Lua yields nil rather than raising, so this is also what
        // keeps an older CE from erroring.
        Line(sb, indent, "local _idlePump = (type(processMessagesPaintOnly) == 'function') "
                         + "and processMessagesPaintOnly or processMessages");
        Line(sb, indent, "local _idleTick = (type(getTickCount) == 'function') and getTickCount or nil");
        Line(sb, indent, "local _idleT0, _idleIters = _idleTick and _idleTick() or 0, 0");
        Line(sb, indent, $"local _idleCmd = readInteger({mbExpr} + {CeMailboxLayout.OffCmd})");
        Line(sb, indent, "while _idleCmd ~= 0 do");
        // An UNREADABLE mailbox is not a busy one. readInteger returns nil when the target
        // process is gone, and `nil ~= 0` is TRUE in Lua, so without this the loop spins to
        // its deadline and then blames a mailbox that no longer exists — the same
        // guess-instead-of-read defect the status branches below were written to kill.
        // Observed on Elliot 2026-08-07: closing the game produced "the DLL mailbox is
        // busy -- try again in a moment", twice, 1.5 s each.
        Line(sb, indent, $"  if _idleCmd == nil then {onUnreadable ?? onBusy} end");
        // See AppendMailboxWait: CE's sleep does not pump messages, so without this the
        // whole idle deadline is a frozen Cheat Engine window.
        Line(sb, indent, "  sleep(1); _idlePump(); _idleIters = _idleIters + 1");
        Line(sb, indent, $"  _idleCmd = readInteger({mbExpr} + {CeMailboxLayout.OffCmd})");
        // Real elapsed time when getTickCount exists; iteration count only as fallback.
        Line(sb, indent, $"  local _idleOver = _idleTick and (_idleTick() - _idleT0 >= " +
                         $"{CeMailboxLayout.MailboxIdleWaitMs}) or (_idleIters >= " +
                         $"{CeMailboxLayout.MailboxIdleWaitIters})");
        Line(sb, indent, $"  if _idleOver then {onBusy} end");
        Line(sb, indent, "end");
    }

    /// <summary>
    /// Timeout in ms for a synchronous <c>executeCodeEx</c> DLL call.
    ///
    /// <para><b>This is a ceiling on GUI freeze time, not just a timeout.</b> Read
    /// against CE source 7.5-195: <c>executeCodeEx</c> is a shim over
    /// <c>executeMethod</c> (LuaHandler.pas:11922 → :11417), whose wait is
    /// <c>CreateRemoteThread</c> (:11847) then <c>WaitForSingleObject(thread,
    /// timeout)</c> (:11861) — <b>on the calling thread, with nothing anywhere on that
    /// path pumping messages</b>. From an AA <c>{$lua}</c> block the calling thread IS
    /// CE's GUI thread, so this value is the longest CE can sit frozen. Unlike the
    /// <see cref="AppendMailboxWait"/> loop, a Lua-side
    /// <c>processMessagesPaintOnly()</c> cannot help here: it only runs once
    /// <c>executeCodeEx</c> has already returned.</para>
    ///
    /// <para>Finite on purpose, and the two sentinels are worse than they look:
    /// <c>nil</c>/<c>-1</c> means <b>INFINITE</b> (:11504-11505) — not "use a default" —
    /// so a stub that never completes freezes CE with no UI-level recovery; and
    /// <c>0</c> means "don't wait" AND skips the reclaim (<c>dontfree := timeout=0</c>
    /// at :11851), which is the source-level mechanism behind celua.txt's leak note.</para>
    ///
    /// <para><b>A timeout is not free either.</b> The <c>WAIT_TIMEOUT</c> branch also
    /// sets <c>dontfree</c> (:11880), so the stub, the result address and every string
    /// allocation stay allocated <i>in the target process</i> permanently. That is
    /// deliberate on CE's side — the remote thread may still be running — but it means
    /// repeated timeouts accumulate, so shortening this value is not a free win. Full
    /// detail: docs/ce-plugin-sdk-notes.md §13.</para>
    /// </summary>
    public const int DllCallTimeoutMs = 5000;

    /// <summary>
    /// Emit the shared <c>callDLL(name)</c> helper: resolve an exported symbol and
    /// call it, returning <c>true</c> only when the call actually ran. Requires
    /// <see cref="AppendDebugPreamble"/> earlier in the same <c>{$lua}</c> block.
    ///
    /// <para>Emitted from here because getting <c>executeCodeEx</c> wrong is
    /// <b>silent</b>, and this repo got it wrong everywhere for a long time. The real
    /// signature is <c>executeCodeEx(callmethod, timeout, address, params...)</c> —
    /// <c>callmethod</c> 0=stdcall / 1=cdecl, and <b>the address is argument 3</b>.
    /// Passing the address in slot 2 makes CE execute with <c>address = nil</c> and
    /// return <c>nil</c> <i>without raising</i>, so a <c>pcall</c> around it reports
    /// success for a call that never happened (audit #4 B1 — a CE Disable therefore
    /// tore nothing down while telling the user it had).</para>
    ///
    /// <para>Hence also the result check: <c>pcall</c>'s status answers "did Lua
    /// raise", never "did the function run". <c>executeCodeEx</c> returns the callee's
    /// RAX on success and <c>nil</c> on failure/timeout, so <c>nil</c> is the only
    /// honest failure signal.</para>
    ///
    /// <para><b>And the reason is read, not guessed.</b> CE returns <c>nil</c> plus a
    /// reason string, with six distinct values — <c>'Execution timeout'</c>,
    /// <c>'Failure launching thread'</c>, <c>'Wait failure'</c>,
    /// <c>'Failure reading the result address'</c> and two arity messages
    /// (docs/ce-plugin-sdk-notes.md §13.5). They send you to six different places, so
    /// the emitted helper reports CE's own answer. Discarding it and printing a
    /// guessed message is the same defect the build-2743 sweep fixed for the mailbox
    /// timeout (see <see cref="AppendTimeoutReason"/>) — one call site later.</para>
    /// </summary>
    public static void AppendCallDllHelper(StringBuilder sb, string indent = "")
    {
        Line(sb, indent, "-- executeCodeEx(callmethod, timeout, address): callmethod 0=stdcall, timeout ms.");
        Line(sb, indent, "-- The address is argument THREE. Put it in the timeout slot and CE runs with");
        Line(sb, indent, "-- address=nil and returns nil WITHOUT raising -- so pcall's status would say");
        Line(sb, indent, "-- 'success' for a call that never happened. Check the RESULT, not the status.");
        Line(sb, indent, "local function callDLL(name)");
        Line(sb, indent, "  local okGet, fn = pcall(getAddress, name)");
        Line(sb, indent, "  if not (okGet and fn and fn ~= 0) then");
        Line(sb, indent, "    dbg('[UE5CEDumper] export not found: ' .. name)");
        Line(sb, indent, "    return false");
        Line(sb, indent, "  end");
        Line(sb, indent, "  -- Three returns, and they mean different things depending on okCall:");
        Line(sb, indent, "  --   raised -> okCall=false, ret=the Lua error message, why=nil");
        Line(sb, indent, "  --   ran    -> okCall=true,  ret=the callee's RAX (nil on failure), why=CE's reason");
        Line(sb, indent, "  -- CE names WHICH failure it was -- 'Execution timeout', 'Failure launching");
        Line(sb, indent, "  -- thread', 'Failure reading the result address', ... -- six reasons that send");
        Line(sb, indent, "  -- you to six different places. Surface it rather than guess one.");
        Line(sb, indent, $"  local okCall, ret, why = pcall(executeCodeEx, 0, {DllCallTimeoutMs}, fn)");
        Line(sb, indent, "  if not okCall then");
        Line(sb, indent, "    dbg('[UE5CEDumper] call raised: ' .. name .. ' -- ' .. tostring(ret))");
        Line(sb, indent, "    return false");
        Line(sb, indent, "  end");
        Line(sb, indent, "  if ret == nil then");
        Line(sb, indent, "    dbg('[UE5CEDumper] call did not run: ' .. name .. ' -- ' .. tostring(why))");
        Line(sb, indent, "    return false");
        Line(sb, indent, "  end");
        Line(sb, indent, "  return true");
        Line(sb, indent, "end");
    }

    private static void Line(StringBuilder sb, string indent, string text)
    {
        sb.Append(indent).Append(text).Append('\n');
    }

    /// <summary>
    /// Escape arbitrary text for a Lua SINGLE-quoted literal. The one escaper new
    /// code should call — there are four divergent private copies in this repo
    /// (BakedScriptGenerator, FreezeScriptGenerator, InvokeScriptGenerator, plus
    /// EscapeLuaComment) and the weakest of them, InvokeScriptGenerator's, handles
    /// neither <c>\r</c> nor <c>\t</c> nor <c>]</c>.
    ///
    /// Handles backslash, single quote, LF, CR, TAB — and any closing long bracket
    /// (<c>]]</c>, <c>]=]</c>, <c>]==]</c>, …). That last one is not about Lua: the
    /// AOBMaker CE plugin wraps the WHOLE submitted script in <c>[==[ … ]==]</c> at a
    /// hardcoded level and does not escape the body, so the byte sequence must not
    /// appear anywhere in an emitted script — even inside a quoted string, where Lua
    /// itself would be perfectly happy. The leading <c>]</c> becomes the decimal
    /// escape <c>\093</c>: same runtime value, different source bytes. Three digits
    /// because <c>\ddd</c> greedily takes three (by construction the next emitted
    /// char here is <c>=</c> or <c>]</c>, never a digit, so it is belt-and-braces).
    ///
    /// A NUL cannot be escaped at all — <c>luaL_dostring</c> measures with
    /// <c>strlen</c> — so callers must reject it upstream (see
    /// <see cref="Models.CoordText.HasBlockedChars"/>).
    /// </summary>
    public static string EscapeLuaString(string? s)
    {
        if (string.IsNullOrEmpty(s)) return "";
        var sb = new StringBuilder(s!.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '\'': sb.Append("\\'");  break;
                case '\n': sb.Append("\\n");  break;
                case '\r': sb.Append("\\r");  break;
                case '\t': sb.Append("\\t");  break;
                case ']':
                {
                    int j = i + 1;
                    while (j < s.Length && s[j] == '=') j++;
                    sb.Append(j < s.Length && s[j] == ']' ? "\\093" : "]");
                    break;
                }
                default: sb.Append(c); break;
            }
        }
        return sb.ToString();
    }

    /// <summary>
    /// Emit the contract check. Place it immediately after the <c>mb</c> lookup and
    /// BEFORE the first write — if the layout moved, writing first scribbles on
    /// whatever now lives at those offsets, and the check exists precisely because we
    /// cannot assume the layout.
    ///
    /// <para>The DLL publishes a RANGE (<c>minimum..current</c>) rather than one
    /// number, which is what makes a months-old <c>.CT</c> keep working across builds
    /// that changed nothing it depends on — versioning on the build number would
    /// condemn every saved table on every release. It also separates the two failure
    /// directions, which need opposite advice: a script below the DLL's minimum must
    /// be regenerated, a script above the DLL's current means the DLL is the old one.
    /// That second case is silent corruption today.</para>
    ///
    /// <para>Degrades in two steps, both of which mean "do not write to the mailbox":
    /// the symbol not resolving at all is a DLL that predates versioning, and a wrong
    /// magic is a symbol that resolved to a stale address — the exact failure measured
    /// on 2026-08-06, where CE held a mailbox address the DLL no longer owned and the
    /// script wrote into it until it timed out.</para>
    /// </summary>
    /// <param name="tag">Script name for the messages, e.g. <c>"Movement"</c>.</param>
    /// <param name="onFail">What to do when the check fails. Same modes, and the same
    /// reason, as <see cref="AppendMailboxWait"/>.</param>
    public static void AppendContractCheck(
        StringBuilder sb, string tag,
        MailboxTimeout onFail = MailboxTimeout.UntickAndReturn,
        string indent = "")
    {
        bool announce = onFail != MailboxTimeout.SilentReturn;
        string say = announce ? "showMessage" : "dbg";

        sb.Append(indent).Append("local _cv = getAddressSafe('")
          .Append(CeMailboxLayout.ContractSymbol).Append("')\n");
        sb.Append(indent).Append("if not _cv or _cv == 0 then _cv = getAddressSafe('UE5Dumper.")
          .Append(CeMailboxLayout.ContractSymbol).Append("') end\n");
        // Self-heal the most common cause before reporting anything. CE snapshots the
        // module list when it OPENS a process, so a DLL injected after that point has no
        // exports in the symbol table and every lookup returns nil — with CE attached to
        // the right process and the DLL demonstrably working over the pipe. Verified on
        // DumperTest 2026-08-07: CE on the correct PID, UI showing "Connected, DLL 2761",
        // and the symbol still unresolvable until the handler re-enumerated.
        // reinitializeSymbolhandler is registered at LuaHandler.pas:16357 and celua.txt:218
        // documents this exact case ("E.g when new modules have been loaded").
        sb.Append(indent).Append("if not _cv or _cv == 0 then\n");
        sb.Append(indent).Append("  reinitializeSymbolhandler()\n");
        sb.Append(indent).Append("  _cv = getAddressSafe('")
          .Append(CeMailboxLayout.ContractSymbol).Append("')\n");
        sb.Append(indent).Append("  if not _cv or _cv == 0 then _cv = getAddressSafe('UE5Dumper.")
          .Append(CeMailboxLayout.ContractSymbol).Append("') end\n");
        sb.Append(indent).Append("end\n");
        sb.Append(indent).Append("local _want = ").Append(CeMailboxLayout.ContractVersion)
          .Append("   -- contract this script was generated against\n");
        sb.Append(indent).Append("if not _cv or _cv == 0 then\n");
        sb.Append(indent).Append("  ").Append(say).Append("('[").Append(tag)
          // NOT "the DLL is old". getAddressSafe returns nil for every reason a symbol can
          // fail to resolve, and an old DLL is the RAREST of them. Verified 2026-08-07: a
          // freshly built DLL that demonstrably exports g_mailboxContract (dumpbin ordinal
          // 64) still produced this message, because Cheat Engine was attached to a stale
          // PID. The old wording sent the user to rebuild a DLL that was already correct.
          // List the causes in likelihood order and assert none of them.
          .Append("] could not resolve g_mailboxContract, even after re-reading the module list.")
          .Append("\\n\\nThe usual cause -- CE having opened the process BEFORE the DLL was ")
          .Append("injected -- was already retried automatically and did not fix it, so one of ")
          .Append("these is true instead:")
          .Append("\\n  - UE5Dumper.dll is not injected into the process CE has open")
          .Append("\\n  - CE is attached to a different process, or to a stale PID from an ")
          .Append("earlier run (that looks identical to being attached)")
          .Append("\\n  - the DLL predates this script -- replace UE5Dumper.dll with a build ")
          .Append("at least as new as this table")
          // Do not assume a UI. Plenty of users have only the .CT plus a proxy DLL, and for
          // them "regenerate the table" is not an available action -- swapping the DLL is.
          .Append("\\n\\nIf you inject with a proxy (version.dll / dinput8.dll / dxgi.dll / ")
          .Append("winmm.dll), check UE5Dumper.dll actually sits beside the game exe: a proxy ")
          .Append("that loads but cannot find it leaves the game running with no mailbox at all.')\n");
        AppendBail(sb, onFail, indent + "  ");
        sb.Append(indent).Append("end\n");
        // Read ONCE into a local, and separate "cannot read" from "read the wrong value".
        // readInteger returns nil when the target is unreadable and `nil ~= MAGIC` is true, so
        // one combined test reported "stale address" for BOTH -- a guess dressed as a
        // diagnosis, and the one that fires FIRST, before either wait loop is reached.
        // Seen on DumperTest 2026-08-07: the DLL was demonstrably alive (poller logging, zero
        // `received cmd` in its log) and the script still blamed a stale symbol.
        sb.Append(indent).Append("local _cmagic = readInteger(_cv + ")
          .Append(CeMailboxLayout.OffContractMagic).Append(")\n");
        sb.Append(indent).Append("if _cmagic == nil then\n");
        sb.Append(indent).Append("  ").Append(say).Append("('[").Append(tag)
          .Append("] the contract symbol resolved to ' .. string.format('%X', _cv) ..")
          .Append(" ' but that memory could not be READ.")
          .Append("\\n\\nIs Cheat Engine still attached to the game? If the process is running, ")
          .Append("re-open it in CE, then untick and re-tick this table.')\n");
        AppendBail(sb, onFail, indent + "  ");
        sb.Append(indent).Append("end\n");
        sb.Append(indent).Append("if _cmagic ~= ")
          .Append(CeMailboxLayout.ContractMagic).Append(" then\n");
        sb.Append(indent).Append("  ").Append(say).Append("('[").Append(tag)
          .Append("] the contract symbol resolved to the wrong memory (stale address).")
          .Append("\\n\\nRe-inject UE5Dumper.dll, or untick and re-tick this table so CE re-resolves it.')\n");
        AppendBail(sb, onFail, indent + "  ");
        sb.Append(indent).Append("end\n");
        sb.Append(indent).Append("local _cur = readInteger(_cv + ")
          .Append(CeMailboxLayout.OffContractCur).Append(")\n");
        sb.Append(indent).Append("local _min = readInteger(_cv + ")
          .Append(CeMailboxLayout.OffContractMin).Append(")\n");
        // Both come off the page whose magic just verified, so nil here means the mapping
        // vanished between reads. Guard anyway: `_want < _min` against nil raises
        // "attempt to compare number with nil", and a Lua traceback is not a cause.
        sb.Append(indent).Append("if _cur == nil or _min == nil then\n");
        sb.Append(indent).Append("  ").Append(say).Append("('[").Append(tag)
          .Append("] the contract block became unreadable while it was being checked.")
          .Append("\\n\\nThe game most likely exited. Untick and re-tick this table.')\n");
        AppendBail(sb, onFail, indent + "  ");
        sb.Append(indent).Append("end\n");
        sb.Append(indent).Append("if _want < _min then\n");
        sb.Append(indent).Append("  ").Append(say).Append("('[").Append(tag)
          .Append("] this script is too old for the DLL (script contract ' .. _want ..")
          .Append(" ', DLL needs ' .. _min .. ' or newer).\\n\\nRegenerate this table from UE5DumpUI.')\n");
        AppendBail(sb, onFail, indent + "  ");
        sb.Append(indent).Append("elseif _want > _cur then\n");
        sb.Append(indent).Append("  ").Append(say).Append("('[").Append(tag)
          .Append("] the DLL is older than this script (script contract ' .. _want ..")
          .Append(" ', DLL speaks ' .. _cur .. ').\\n\\nUpdate UE5Dumper.dll.')\n");
        AppendBail(sb, onFail, indent + "  ");
        sb.Append(indent).Append("end\n");
    }

    /// <summary>
    /// The bail-out half of the contract check, shared so its three failures cannot end
    /// differently by accident.
    ///
    /// <para>It unticks DIRECTLY even for <see cref="MailboxTimeout.FlagAndBreak"/>,
    /// which the timeout path does not — and the asymmetry is deliberate, not an
    /// oversight. A momentary script relies on a deferred timer to untick, but that
    /// timer is created LATER in the block: at contract-check time it does not exist
    /// yet, so returning here would reach nothing and strand the record ticked. The
    /// timeout path can flag-and-break because by then the timer is downstream of the
    /// loop. Same rule as the mailbox-not-found bail-out these scripts already have,
    /// which unticks itself for exactly this reason.</para>
    /// </summary>
    private static void AppendBail(StringBuilder sb, MailboxTimeout mode, string indent)
    {
        if (mode != MailboxTimeout.SilentReturn)
            sb.Append(indent).Append("if memrec then memrec.Active = false end\n");
        sb.Append(indent).Append("return\n");
    }

    /// <summary>
    /// Bounded idle wait for a generator that owns a CE memory record — i.e. every
    /// stateful toggle and momentary action — emitting the bail in this repo's house
    /// shape rather than as a one-liner.
    ///
    /// <para><b>Why this exists rather than calling <see cref="AppendIdleWait"/>
    /// directly (audit #5 AA10).</b> Seven of the eleven mailbox emitters had NO idle
    /// wait at all: they sampled nothing and wrote straight over whatever command was in
    /// flight. The four that did have one are all helper-shaped
    /// (<c>return nil, reason</c>), so there was no toggle-shaped precedent to copy —
    /// and hand-rolling one per generator is precisely how build 2743's three defects
    /// reached all seven copies of the mailbox wait at once.</para>
    ///
    /// <para>The bail is emitted across SEPARATE LINES on purpose. Squeezing
    /// <c>showMessage(...); if memrec then ... end; return</c> onto the single line
    /// <see cref="AppendIdleWait"/> interpolates into makes it invisible to
    /// <c>CeMailboxBailoutTests</c>, which scans line-by-line for an untick FOLLOWING
    /// each message. That test caught this the first time it was written the short way,
    /// and it was right to: the one-liner is also unreadable in CE's editor.</para>
    ///
    /// <para>Placement is the caller's job and it matters: this belongs ABOVE the
    /// operand writes, not merely above the status clear. Writing operands into a
    /// mailbox the DLL still owns corrupts the command in flight — the same reason
    /// <see cref="AppendContractCheck"/> sits before the first write.</para>
    /// </summary>
    /// <param name="tag">Script tag used in the user-facing message, e.g. "GodMode".</param>
    /// <param name="onBusy">How this block bails: a toggle unticks and returns, a
    /// <c>[DISABLE]</c> block reports under <c>dbg</c> only.</param>
    public static void AppendIdleWaitOrBail(StringBuilder sb, string mbExpr, string tag,
                                            MailboxTimeout onBusy = MailboxTimeout.UntickAndReturn,
                                            string indent = "")
    {
        Line(sb, indent, "local _idleBusy, _idleGone = false, false");
        // `break` leaves AppendIdleWait's own while-loop; the reporting happens below so
        // the message and the untick land on lines of their own.
        AppendIdleWait(sb, mbExpr, "_idleBusy = true; break", indent,
                       "_idleGone = true; break");

        bool announce = onBusy != MailboxTimeout.SilentReturn;
        string say = announce ? "showMessage" : "dbg";

        Line(sb, indent, "if _idleGone then");
        Line(sb, indent, $"  {say}('[{tag}] the mailbox could not be read -- the game process has "
                         + "most likely exited (re-inject UE5Dumper.dll if it is still running)')");
        AppendBail(sb, onBusy, indent + "  ");
        Line(sb, indent, "end");

        Line(sb, indent, "if _idleBusy then");
        Line(sb, indent, $"  {say}('[{tag}] the DLL mailbox is busy with another command -- "
                         + "try again in a moment')");
        AppendBail(sb, onBusy, indent + "  ");
        Line(sb, indent, "end");
    }

    /// <summary>
    /// Emit the mailbox round-trip's WAIT loop plus its bail-out. Every generator that
    /// talks to <c>g_invokeMailbox</c> must use this rather than hand-rolling the loop,
    /// because three separate things were wrong in all seven hand-rolled copies and a
    /// per-file fix would only have drifted again.
    ///
    /// <list type="number">
    /// <item><b>The checkbox lied.</b> Every copy cleared <c>memrec.Active</c> on the
    /// "mailbox not found" path and none cleared it on the timeout path, so a timed-out
    /// enable left the CE row TICKED with nothing applied. Same class as the audit's
    /// B30/B40, which was fixed in <c>UE5CEDumper.CT</c> and
    /// <c>CeInjectScriptGenerator</c> and never propagated here.</item>
    ///
    /// <item><b>The message blamed the wrong thing.</b> "(DLL not responding?)" was a
    /// guess, and the mailbox already holds the answer: <c>status</c> is still
    /// <see cref="CeMailboxLayout.StatusIdle"/> when the DLL never saw the command
    /// (stale mailbox address / poller down) and
    /// <see cref="CeMailboxLayout.StatusProcessing"/> when it took the command and
    /// wedged. Measured 2026-08-06 on ES2: a real timeout occurred while the DLL's
    /// poller was demonstrably alive at 1 ms, so the message sent the user to inspect a
    /// healthy DLL.</item>
    ///
    /// <item><b>The timeout was 15.5x its stated value.</b> The loop counted
    /// <c>sleep(1)</c> iterations; <c>sleep(1)</c> measures 15.47 ms in CE. See
    /// <see cref="CeMailboxLayout.MailboxPollTimeoutMs"/>. This emits a real
    /// <c>getTickCount()</c> deadline, with the iteration count kept only as a fallback
    /// for a CE build that lacks it.</item>
    /// </list>
    ///
    /// <para>The bail-out <c>return</c>s, so a caller's success-close stays unreachable
    /// on this path — the "a timeout is an error" half of the auto-close rule.</para>
    /// </summary>
    /// <param name="tag">Script name for the message, e.g. <c>"Movement"</c>.</param>
    /// <param name="onTimeout">How the caller's block ends — see
    /// <see cref="MailboxTimeout"/>. Getting this wrong is not cosmetic: a momentary
    /// action that <c>return</c>s would strand its record ticked (that is why
    /// <see cref="MailboxTimeout.FlagAndBreak"/> exists), and a toggle that only
    /// <c>break</c>s would fall through into its success path.</param>
    public static void AppendMailboxWait(
        StringBuilder sb, string tag,
        MailboxTimeout onTimeout = MailboxTimeout.UntickAndReturn,
        string indent = "")
    {
        string mb = "mb + " + CeMailboxLayout.OffStatus;
        // Keep Cheat Engine's window alive while we block. CE's Lua `sleep` is a bare Win32
        // Sleep (LuaHandler.pas lua_sleep) and does NOT pump messages -- despite a comment
        // in this repo that claimed it did -- so a 10 s wait froze CE for the full 10 s.
        //
        // Prefer processMessagesPaintOnly. CE's own celua.txt calls processMessages "not
        // recommended" (timers and other scripts run during it and can modify locals), and
        // paint-only "does not handle mouse or keyboard events" -- so it cannot re-enter us
        // through a second button click. It is absent from the 7.5 SOURCE tree but present
        // in the 7.7 binary, which is why this feature-tests instead of calling it directly.
        sb.Append(indent).Append(
            "local _pump = (type(processMessagesPaintOnly) == 'function') and " +
            "processMessagesPaintOnly or processMessages\n");
        sb.Append(indent).Append("local _tick = (type(getTickCount) == 'function') and getTickCount or nil\n");
        sb.Append(indent).Append("local _t0, _iters = _tick and _tick() or 0, 0\n");
        sb.Append(indent).Append("local _st = readInteger(").Append(mb).Append(")\n");
        sb.Append(indent).Append("while _st ~= ").Append(CeMailboxLayout.StatusDone).Append(" do\n");
        // FIRST statement of the body, before the re-read — the position AppendIdleWait and
        // ue5_invoke_helper.lua both use. Two reasons it cannot move:
        //   * `_iters` is declared THREE lines up. Emitted above that declaration it binds to
        //     a nil GLOBAL and `_iters + 1` raises on the spot, after the command has already
        //     been written to the mailbox — so the DLL runs it and everything downstream of
        //     this loop (result read, state<0 diagnosis, untick, auto-close) is skipped.
        //   * Emitted after the re-read it would still terminate, but `_st` is sampled ONCE
        //     before the loop, so a round-trip that completes pays one extra sleep — 15.47 ms
        //     on every success — to leave a loop it already knows is done.
        // Build 2769 shipped it above both. Pinned by
        // CeLuaHygieneTests.Wait_loops_never_use_a_local_before_its_declaration and
        // ..._sleep_and_pump_inside_the_loop_body, which read POSITION, not substrings.
        sb.Append(indent).Append("  sleep(1); _pump(); _iters = _iters + 1\n");
        sb.Append(indent).Append("  _st = readInteger(").Append(mb).Append(")\n");
        // Real elapsed time when getTickCount exists; iteration count only as fallback.
        // `_st == nil` short-circuits the deadline: readInteger returns nil once the target
        // process is gone, and waiting the full timeout to say so helps nobody. It reaches
        // the caller's normal bail with the nil case named in _msg (AppendTimeoutReason).
        sb.Append(indent).Append("  local _over = _st == nil or (_tick and (_tick() - _t0 >= ")
          .Append(CeMailboxLayout.MailboxPollTimeoutMs).Append(") or (_iters >= ")
          .Append(CeMailboxLayout.MailboxPollTimeoutIters).Append("))\n");
        sb.Append(indent).Append("  if _st ~= ").Append(CeMailboxLayout.StatusDone)
          .Append(" and _over then\n");

        // ONE reason builder for every mode. It used to be three inline showMessage
        // literals, which meant a mode that reports by value or by raise would have
        // needed its own copy of the status table — and "two copies of a rule is how the
        // timeout defect got into seven files" (CeMailboxLayout.cs).
        AppendTimeoutReason(sb, indent + "    ");

        switch (onTimeout)
        {
            case MailboxTimeout.UntickAndReturn:
                sb.Append(indent).Append("    showMessage('[").Append(tag).Append("] ' .. _msg)\n");
                sb.Append(indent).Append("    if memrec then memrec.Active = false end\n");
                sb.Append(indent).Append("    return\n");
                break;
            case MailboxTimeout.FlagAndBreak:
                // The caller's deferred timer unticks unconditionally and suppresses the
                // success-close on hadError, so returning here would skip BOTH.
                sb.Append(indent).Append("    showMessage('[").Append(tag).Append("] ' .. _msg)\n");
                sb.Append(indent).Append("    hadError = true\n");
                sb.Append(indent).Append("    break\n");
                break;
            case MailboxTimeout.ReturnReason:
                // No dialog and no untick: the CALLER decides how to surface this and
                // whether it is fatal at all. The reason is untagged so the caller's own
                // prefix (e.g. '[Coordinate Library] ') does not double up.
                sb.Append(indent).Append("    return nil, _msg\n");
                break;
            case MailboxTimeout.RaiseError:
                sb.Append(indent).Append("    error('[").Append(tag).Append("] ' .. _msg)\n");
                break;
            default:
                // SilentReturn: no dialog on an untick, but DEBUG still sees the reason.
                sb.Append(indent).Append("    dbg('[").Append(tag).Append("] ' .. _msg)\n");
                sb.Append(indent).Append("    return\n");
                break;
        }
        sb.Append(indent).Append("  end\n");
        sb.Append(indent).Append("end\n");
    }

    /// <summary>
    /// Emit the status-to-reason table into a local <c>_msg</c>. Untagged: the caller
    /// bolts its own <c>[Tag]</c> on, and the by-value mode hands the bare reason to a
    /// caller that already has one.
    ///
    /// <para>The whole point of the table is that it does not GUESS. "(DLL not
    /// responding?)" blamed the DLL for the far more common case where the DLL never
    /// received the command at all — measured on ES2 2026-08-06 with the DLL's poller
    /// demonstrably alive at 1 ms — and the mailbox already answers the question:
    /// <see cref="CeMailboxLayout.StatusIdle"/> means untouched (stale
    /// <c>g_invokeMailbox</c> address), <see cref="CeMailboxLayout.StatusProcessing"/>
    /// means picked up and wedged. The two send the user to opposite places.</para>
    /// </summary>
    private static void AppendTimeoutReason(StringBuilder sb, string indent)
    {
        sb.Append(indent).Append("local _msg\n");
        // nil FIRST: it is not a status value, it is the absence of one. Every other branch
        // compares _st against a number, and in Lua `nil == 0` is false, so without this the
        // dead-process case fell into the else and told the user its status was "nil".
        sb.Append(indent).Append("if _st == nil then\n");
        sb.Append(indent).Append("  _msg = 'the mailbox could not be read.\\n\\n")
          .Append("The game process has most likely exited. If it is still running, ")
          .Append("re-inject UE5Dumper.dll, or untick and re-tick this table so CE ")
          .Append("re-resolves g_invokeMailbox.'\n");
        sb.Append(indent).Append("elseif _st == ").Append(CeMailboxLayout.StatusIdle).Append(" then\n");
        sb.Append(indent).Append("  _msg = 'timed out and the DLL never saw this command.\\n\\n")
          .Append("Most likely a stale g_invokeMailbox address: re-inject UE5Dumper.dll, ")
          .Append("or untick and re-tick this table so CE re-resolves the symbol.'\n");
        sb.Append(indent).Append("elseif _st == ").Append(CeMailboxLayout.StatusProcessing).Append(" then\n");
        sb.Append(indent).Append("  _msg = 'the DLL took this command but never finished it.\\n\\n")
          .Append("Is the game paused, or the game thread stalled?'\n");
        sb.Append(indent).Append("else\n");
        sb.Append(indent).Append("  _msg = 'timed out on an unexpected mailbox status: ' .. tostring(_st)\n");
        sb.Append(indent).Append("end\n");
    }

    /// <summary>
    /// Emit an ENABLE-block bail-out that applied NOTHING: show the reason, untick the
    /// record, return. The untick is the point — a bail-out that leaves the row ticked
    /// tells the user a cheat is active when it is not.
    /// </summary>
    public static void AppendFailedEnable(
        StringBuilder sb, string luaMessageExpr, string indent = "")
    {
        sb.Append(indent).Append("showMessage(").Append(luaMessageExpr).Append(")\n");
        sb.Append(indent).Append("if memrec then memrec.Active = false end\n");
        sb.Append(indent).Append("return\n");
    }

    /// <summary>Emit the success-path close: closes the Lua Engine window unless
    /// <c>DEBUG</c> is on. Pass <paramref name="extraCondition"/> to gate on an
    /// additional Lua boolean expression (e.g. <c>"not hadError"</c>, <c>"ok"</c>)
    /// so error paths keep the window open.</summary>
    public static void AppendCloseOnSuccess(
        StringBuilder sb, string? extraCondition = null, string indent = "")
    {
        var cond = string.IsNullOrEmpty(extraCondition)
            ? "DEBUG == 0"
            : $"{extraCondition} and DEBUG == 0";
        sb.Append(indent).Append("if ").Append(cond)
          .Append(" then ").Append(CloseCall).Append(" end\n");
    }

    /// <summary>
    /// A CE Lua GLOBAL table used to reference-count SLOT-backed registered symbols
    /// across the separate <c>[ENABLE]</c>/<c>[DISABLE]</c> Lua chunks (chunk locals
    /// do not cross, and CE's Lua state persists for the whole session — see
    /// docs/CE-Bugs-Minesweeper.md §5).
    ///
    /// <para>A count is required, not an address marker: two records that both resolve
    /// the same <c>&amp;GWorld</c>/<c>&amp;GEngine</c> slot register the IDENTICAL
    /// address, so the buffer branch's <c>cur == marker</c> ownership test cannot tell
    /// one holder from another and would let the FIRST record's disable unregister the
    /// symbol out from under a second still-ticked one. Refcounting is the only thing
    /// that makes "a second live record's symbol survives" true here.</para>
    /// </summary>
    private const string SlotSymRefTable = "UE5_slotSymRefcount";

    /// <summary>
    /// Emit the ENABLE half of a SLOT-backed CE symbol: bump the per-symbol reference
    /// count, then (re)register <paramref name="sym"/> to <paramref name="addrExpr"/>
    /// (a game address, never freed). Self-contained — it clears any prior registration
    /// of the same name itself, so the caller need not pre-unregister. Pairs with
    /// <see cref="AppendSlotSymbolRelease"/>; both go through here so the slot paths in
    /// different generators (and the two ends of one record) cannot drift.
    /// </summary>
    public static void AppendSlotSymbolRegister(
        StringBuilder sb, string sym, string addrExpr, string indent = "")
    {
        Line(sb, indent, $"{SlotSymRefTable} = {SlotSymRefTable} or {{}}");
        Line(sb, indent, $"{SlotSymRefTable}['{sym}'] = ({SlotSymRefTable}['{sym}'] or 0) + 1");
        // Clear any prior registration before republishing so registerSymbol can never
        // stack a second entry for this name (the case AppendSlotSymbolRelease's loop
        // also defends against on the way out).
        Line(sb, indent, $"if getAddressSafe('{sym}') then unregisterSymbol('{sym}') end");
        Line(sb, indent, $"registerSymbol('{sym}', {addrExpr})");
    }

    /// <summary>
    /// Emit the DISABLE half of a SLOT-backed CE symbol: drop the per-symbol reference
    /// count and unregister the symbol ONLY when the last holder releases it, so a
    /// second still-ticked record keeps resolving.
    ///
    /// <para><b>The message follows the FACT, not the intent.</b> The predecessor here
    /// printed <c>'&lt;sym&gt; unregistered'</c> unconditionally — the "report and reality
    /// computed by different code paths" defect (audit #4a) — while on the slot path the
    /// unregister was skipped entirely (it was nested inside a buffer-only guard). This
    /// re-reads <c>getAddressSafe</c> AFTER the unregister and claims success only when
    /// the symbol is actually gone. The bounded loop also removes a stacked registration
    /// were one ever to exist, and can never hang.</para>
    /// </summary>
    /// <param name="tag">Script tag for the diagnostic, e.g. <c>"GWorld"</c>.</param>
    public static void AppendSlotSymbolRelease(
        StringBuilder sb, string sym, string tag, string indent = "")
    {
        Line(sb, indent, $"{SlotSymRefTable} = {SlotSymRefTable} or {{}}");
        Line(sb, indent, $"local _rc = ({SlotSymRefTable}['{sym}'] or 0) - 1");
        Line(sb, indent, "if _rc < 0 then _rc = 0 end");
        Line(sb, indent, $"{SlotSymRefTable}['{sym}'] = _rc");
        Line(sb, indent, "if _rc > 0 then");
        Line(sb, indent, $"  dbg('[{tag}] {sym} still held by ' .. _rc .. ' other record(s) -- left registered')");
        Line(sb, indent, "else");
        Line(sb, indent, "  local _tries = 0");
        Line(sb, indent, $"  while getAddressSafe('{sym}') and _tries < 8 do");
        Line(sb, indent, $"    unregisterSymbol('{sym}')");
        Line(sb, indent, "    _tries = _tries + 1");
        Line(sb, indent, "  end");
        Line(sb, indent, $"  if getAddressSafe('{sym}') then");
        Line(sb, indent, $"    dbg('[{tag}] {sym} could NOT be unregistered after ' .. _tries .. ' attempt(s) -- it still resolves')");
        Line(sb, indent, "  else");
        Line(sb, indent, $"    dbg('[{tag}] {sym} unregistered')");
        Line(sb, indent, "  end");
        Line(sb, indent, "end");
    }
}
