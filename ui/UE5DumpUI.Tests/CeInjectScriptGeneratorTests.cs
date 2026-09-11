using System;
using System.Linq;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the shape of the CE bootstrap record (<see cref="CeInjectScriptGenerator"/>) —
/// the [ENABLE]/[DISABLE] script pushed into the user's ALREADY-OPEN CE table so the
/// standalone UE5CEDumper.CT is no longer needed just to inject.
///
/// The invariants that matter: it polls the DLL's mailbox initState instead of
/// sleeping a fixed budget, it never uses executeCodeEx on the START-UP path (games
/// block CreateRemoteThread then) and reaches the target only through the shared
/// <c>callDLL</c> emitter when it must, every failure path returns BEFORE the
/// success-close so the Lua Engine window stays readable, and the emitted text is
/// safe to hand to the AOBMaker plugin (which wraps the whole body in [==[ ... ]==]).
///
/// <para>One shape rule that is easy to trip over: an [ENABLE]-block comment must
/// never contain the literal "[DISABLE]" — both CE and <see cref="Enable"/> slice the
/// script on that marker, so it silently truncates the block.</para>
/// </summary>
public class CeInjectScriptGeneratorTests
{
    private const string Dll = @"D:\dist\UE5Dumper.dll";

    private static string Enable(string s)
    {
        var i = s.IndexOf("[ENABLE]", StringComparison.Ordinal);
        var j = s.IndexOf("[DISABLE]", StringComparison.Ordinal);
        return s.Substring(i, j - i);
    }

    private static string Disable(string s) =>
        s.Substring(s.IndexOf("[DISABLE]", StringComparison.Ordinal));

    [Fact]
    public void Generate_emits_enable_and_disable_blocks()
    {
        var s = CeInjectScriptGenerator.Generate(Dll);
        Assert.Contains("[ENABLE]", s);
        Assert.Contains("[DISABLE]", s);
        Assert.Contains("{$lua}", s);
    }

    [Fact]
    public void Enable_bakes_the_resolved_dll_path()
    {
        var s = CeInjectScriptGenerator.Generate(Dll);
        // Backslashes must survive as Lua escapes, not raw.
        Assert.Contains(@"D:\\dist\\UE5Dumper.dll", Enable(s));
        Assert.Contains("injectDLL(DLL_PATH)", Enable(s));
    }

    [Fact]
    public void Enable_polls_initstate_instead_of_sleeping_a_fixed_budget()
    {
        var e = Enable(CeInjectScriptGenerator.Generate(Dll));
        Assert.Contains("readInteger, mb + 0x0C", e);          // MailboxData.initState
        Assert.Contains($"sleep({CeReadinessLua.PollIntervalMs})", e);
        Assert.Contains($"while waited < {CeReadinessLua.ReadyTimeoutMs} do", e);
        // The old .CT behaviour we are replacing must not reappear.
        Assert.DoesNotContain("sleep(1000)", e);
        Assert.DoesNotContain("sleep(15000)", e);
    }

    /// <summary>Drop Lua line comments so a check can target real code only —
    /// the emitted comments deliberately mention APIs the code must not use.</summary>
    private static string CodeOnly(string lua) =>
        string.Join('\n', lua.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    [Fact]
    public void Enable_never_uses_executeCodeEx_on_the_startup_path()
    {
        // NARROWED (audit #4 B1(b)), and the narrowing is the point: the original
        // rule was "no executeCodeEx anywhere in [ENABLE]", justified by "start-up
        // is exactly when games block CreateRemoteThread". That reason only covers
        // the START-UP path. [ENABLE] now also has a revive path — the DLL is
        // already mapped and parked because a previous disable ran UE5_Shutdown —
        // and reviving it REQUIRES a remote call: the mailbox poller is joined, so
        // there is no memory-write channel left to ask through.
        //
        // The invariant that actually protects users is therefore the one asserted
        // here: from injectDLL onwards — the fresh-injection and readiness-poll
        // region, the only part that runs while a game is still starting up —
        // nothing may call into the target.
        var e = Enable(CeInjectScriptGenerator.Generate(Dll));
        var startupPath = CodeOnly(e.Substring(e.IndexOf("injectDLL(DLL_PATH)", StringComparison.Ordinal)));
        Assert.DoesNotContain("executeCodeEx", startupPath, StringComparison.Ordinal);
    }

    [Fact]
    public void Enable_only_reaches_the_target_through_the_shared_callDLL_helper()
    {
        // The other half of the narrowing above. Any remote call in [ENABLE] must go
        // through CeLuaHygiene's emitter, which is the one place that knows
        // executeCodeEx's real signature — (callmethod, timeout, address). Getting
        // those slots wrong is silent: CE returns nil WITHOUT raising, so a bare
        // inline call reads as success while doing nothing (that is B1(a), which
        // shipped for months in exactly this file).
        var code = CodeOnly(Enable(CeInjectScriptGenerator.Generate(Dll)));
        foreach (var idx in Occurrences(code, "executeCodeEx"))
        {
            var line = LineAt(code, idx);
            Assert.Contains($"pcall(executeCodeEx, 0, {CeLuaHygiene.DllCallTimeoutMs},", line,
                StringComparison.Ordinal);
        }
        // ...and the revive path really is wired to the helper, not hand-rolled.
        Assert.Contains("callDLL('UE5_AutoStart')", code, StringComparison.Ordinal);
    }

    private static System.Collections.Generic.IEnumerable<int> Occurrences(string s, string needle)
    {
        for (int i = s.IndexOf(needle, StringComparison.Ordinal); i >= 0;
             i = s.IndexOf(needle, i + 1, StringComparison.Ordinal))
            yield return i;
    }

    private static string LineAt(string s, int idx)
    {
        var start = s.LastIndexOf('\n', idx) + 1;
        var end = s.IndexOf('\n', idx);
        return end < 0 ? s[start..] : s[start..end];
    }

    [Fact]
    public void Enable_resolves_the_symbol_inside_the_poll_loop()
    {
        var e = Enable(CeInjectScriptGenerator.Generate(Dll));
        var loopStart = e.IndexOf("while waited <", StringComparison.Ordinal);
        var loopBody = e.Substring(loopStart);
        // CE's symbol handler may not see the just-injected module on the first try.
        Assert.Contains("pcall(getAddress, sym)", loopBody);
        Assert.Contains($"if mb == nil and waited >= {CeReadinessLua.SymbolGraceMs} then", loopBody);
    }

    /// <summary>
    /// B33 — lessons-learned.md mandates trying BOTH symbol spellings: depending on how
    /// CE's symbol handler picked up the module, an export is reachable bare or only as
    /// "&lt;module&gt;.&lt;name&gt;". Eight other sites in the repo obey it; this generator
    /// was a holdout, and a single-spelling miss silently degrades the readiness poll
    /// back to the blind fixed wait it was written to replace.
    /// </summary>
    [Fact]
    public void Enable_tries_both_mailbox_symbol_spellings()
    {
        var e = Enable(CeInjectScriptGenerator.Generate(Dll));
        var loopStart = e.IndexOf("while waited <", StringComparison.Ordinal);
        var loopBody = e.Substring(loopStart);
        Assert.Contains("'g_invokeMailbox'", loopBody);
        Assert.Contains("'UE5Dumper.g_invokeMailbox'", loopBody);
        // Both must be in ONE iterated list, not two hand-copied branches that can drift.
        Assert.Contains("ipairs({'g_invokeMailbox', 'UE5Dumper.g_invokeMailbox'})", loopBody);
    }

    [Fact]
    public void Enable_guards_against_double_inject()
    {
        var e = Enable(CeInjectScriptGenerator.Generate(Dll));
        var probeIdx = e.IndexOf("pcall(getAddress, 'UE5_Init')", StringComparison.Ordinal);
        var injectIdx = e.IndexOf("injectDLL(DLL_PATH)", StringComparison.Ordinal);
        Assert.True(probeIdx > 0, "already-loaded probe missing");
        Assert.True(probeIdx < injectIdx, "the already-loaded probe must run BEFORE injectDLL");
    }

    [Fact]
    public void Enable_requires_an_attached_process_before_injecting()
    {
        var e = Enable(CeInjectScriptGenerator.Generate(Dll));
        var checkIdx = e.IndexOf("getOpenedProcessID() == 0", StringComparison.Ordinal);
        var injectIdx = e.IndexOf("injectDLL(DLL_PATH)", StringComparison.Ordinal);
        Assert.True(checkIdx > 0 && checkIdx < injectIdx);
    }

    [Fact]
    public void Every_enable_failure_path_returns_before_the_success_close()
    {
        var e = Enable(CeInjectScriptGenerator.Generate(Dll));
        var closeIdx = e.IndexOf(CeLuaHygiene.CloseCall, StringComparison.Ordinal);
        Assert.True(closeIdx > 0, "success-close missing");
        // Each showMessage (an error report) must be followed by a `return` that
        // lands before the close, so a failure never auto-closes the window.
        int from = 0;
        int seen = 0;
        while (true)
        {
            var msg = e.IndexOf("showMessage(", from, StringComparison.Ordinal);
            if (msg < 0 || msg > closeIdx) break;
            seen++;
            var ret = e.IndexOf("return", msg, StringComparison.Ordinal);
            Assert.True(ret > msg && ret < closeIdx,
                $"showMessage at {msg} has no `return` before the success-close");
            from = msg + 1;
        }
        Assert.True(seen >= 4, $"expected at least 4 guarded error paths, saw {seen}");
    }

    [Fact]
    public void Success_close_is_debug_gated()
    {
        var s = CeInjectScriptGenerator.Generate(Dll);
        Assert.Contains("local DEBUG = UE5_DEBUG or 0", s);
        Assert.Contains($"if DEBUG == 0 then {CeLuaHygiene.CloseCall} end", s);
    }

    [Fact]
    public void Skipped_state_is_treated_as_success_not_failure()
    {
        // Another instance owning the pipe means a pipe server IS up — proceeding
        // is correct, erroring is not.
        var e = Enable(CeInjectScriptGenerator.Generate(Dll));
        Assert.Contains("state ~= INIT_READY and state ~= INIT_SKIPPED", e);
    }

    [Fact]
    public void Disable_shuts_the_dll_down()
    {
        var d = Disable(CeInjectScriptGenerator.Generate(Dll));
        Assert.Contains("UE5_StopPipeServer", d, StringComparison.Ordinal);
        Assert.Contains("UE5_Shutdown", d, StringComparison.Ordinal);
        // executeCodeEx IS fine here: the game is running normally by now. It goes
        // through pcall, so match that form rather than a direct call.
        Assert.Contains("pcall(executeCodeEx,", CodeOnly(d), StringComparison.Ordinal);
    }

    [Fact]
    public void Disable_is_a_quiet_noop_when_nothing_was_ever_loaded()
    {
        // [ENABLE]'s early bail-outs untick the record, so CE runs [DISABLE]
        // against a DLL that never loaded. That must not report a failure.
        var d = Disable(CeInjectScriptGenerator.Generate(Dll));
        var probeIdx = d.IndexOf("pcall(getAddress, 'UE5_StopPipeServer')", StringComparison.Ordinal);
        var shoutIdx = d.IndexOf("shutdown did not complete cleanly", StringComparison.Ordinal);
        Assert.True(probeIdx > 0, "not-loaded probe missing");
        Assert.True(probeIdx < shoutIdx, "the not-loaded probe must short-circuit before the failure print");
        Assert.Contains("nothing to shut down", d, StringComparison.Ordinal);
    }

    [Fact]
    public void Emitted_text_is_safe_for_the_aobmaker_long_bracket_wrapper()
    {
        // The CE plugin wraps the whole submitted script in [==[ ... ]==] without
        // escaping the body, so that byte sequence must not appear anywhere.
        var s = CeInjectScriptGenerator.Generate(Dll);
        Assert.DoesNotContain("]==]", s, StringComparison.Ordinal);
        // NUL check uses the CHAR overload on purpose: the string overload of
        // Contains/IndexOf is culture-sensitive, and under ICU a NUL has zero
        // collation weight, so `s.Contains("\0")` reports a match at position 0
        // of ANY string. The char overload is always ordinal.
        Assert.False(s.Contains('\0'), "a NUL cannot be escaped for luaL_dostring");
    }

    [Fact]
    public void Line_endings_are_lf_only()
    {
        Assert.DoesNotContain("\r", CeInjectScriptGenerator.Generate(Dll));
    }

    [Fact]
    public void Record_is_grouped_so_it_does_not_litter_the_user_table_root()
    {
        Assert.False(string.IsNullOrWhiteSpace(CeInjectScriptGenerator.RecordGroup));
        Assert.False(string.IsNullOrWhiteSpace(CeInjectScriptGenerator.RecordDescription));
    }

    [Theory]
    [InlineData(@"C:\Program Files\Game's Folder\UE5Dumper.dll")]
    [InlineData(@"D:\a\b\UE5Dumper.dll")]
    public void Dll_path_is_escaped_for_a_lua_literal(string path)
    {
        var e = Enable(CeInjectScriptGenerator.Generate(path));
        // The raw path (with unescaped backslashes / quotes) must never appear.
        Assert.DoesNotContain($"'{path}'", e);
        Assert.Contains("local DLL_PATH = '", e);
    }

    /// <summary>
    /// The inert reminder row pushed beside the bootstrap. It exists because the one thing
    /// that breaks every mailbox script -- commands run on the GAME thread, so a paused or
    /// alt-tabbed game times them all out -- is invisible from inside Cheat Engine.
    /// </summary>
    [Fact]
    public void Reminder_row_is_inert_unticks_itself_and_states_both_hazards()
    {
        string s = CeInjectScriptGenerator.GenerateReminder();

        Assert.Contains("[ENABLE]", s, StringComparison.Ordinal);
        Assert.Contains("[DISABLE]", s, StringComparison.Ordinal);

        // It applies nothing, so leaving the row ticked would claim a cheat is active.
        Assert.Contains("memrec.Active = false", s, StringComparison.Ordinal);

        // Inert: it must not touch the mailbox at all.
        Assert.DoesNotContain("writeInteger(mb", s, StringComparison.Ordinal);
        Assert.DoesNotContain("g_invokeMailbox", s, StringComparison.Ordinal);

        // Both hazards, and the second is the surprising one: a timed-out command is not
        // cancelled. Measured on DumperTest 2026-08-07 -- a teleport completed 35 s after
        // it was sent, 25 s after the script had given up on it.
        Assert.Contains("GAME THREAD", s, StringComparison.Ordinal);
        Assert.Contains("NOT cancelled", s, StringComparison.Ordinal);

        // Paragraph breaks use string.char(10), not a backslash escape: the escape has to
        // survive C# -> Lua -> CE's XML-encoded script body, and arrives as a literal
        // backslash-n when it does not. Assert the fragile form is absent, not just that
        // the robust one is present.
        Assert.Contains("string.char(10)", s, StringComparison.Ordinal);
        // Built from char codes so the assertion itself cannot be mangled by escaping --
        // which is precisely the failure mode it guards against, and which mangled an
        // earlier attempt at this very line into a verbatim string holding a real newline.
        string backslashN = new string(new[] { (char)92, (char)110 });
        Assert.DoesNotContain(backslashN, s, StringComparison.Ordinal);

        Assert.Contains("GAME", CeInjectScriptGenerator.ReminderDescription,
            StringComparison.Ordinal);
    }

    // ────────────────────────────────────────────────────────────────────────
    // [B30-REOPEN-2026-09-10] — the disable must tear down only what THIS record
    // started. The original B30 fix guarded on "is a DLL loaded", which is not the
    // same question: all four proxy .def files export UE5_StopPipeServer, so in the
    // exact case B30 was filed about — a proxy already loaded and serving — the probe
    // SUCCEEDED and UE5_Shutdown ran against a pipe this record never started.
    // ────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Disable_refuses_to_tear_down_a_pipe_this_record_did_not_start()
    {
        var d = Disable(CeInjectScriptGenerator.Generate(Dll));

        // The ownership guard exists, and it is a REFUSAL with an early return.
        Assert.Contains("if not UE5_StartedByThisRecord then", d, StringComparison.Ordinal);

        // ⭐ And it comes BEFORE the shutdown call, which is the whole point — a guard
        // below the teardown would document the hazard without preventing it.
        var guard = d.IndexOf("UE5_StartedByThisRecord", StringComparison.Ordinal);
        var kill = d.IndexOf("callDLL('UE5_Shutdown')", StringComparison.Ordinal);
        Assert.True(guard >= 0 && kill >= 0, "both the guard and the shutdown must be present");
        Assert.True(guard < kill,
            "the ownership guard must precede callDLL('UE5_Shutdown'), not follow it");

        // The symbol probe stays: it answers a DIFFERENT question (nothing loaded at
        // all) and it also stops a bare getAddress throwing. Losing it would be a
        // regression of B40, so pin that both survive.
        Assert.Contains("UE5_StopPipeServer", d, StringComparison.Ordinal);
    }

    [Fact]
    public void Enable_claims_ownership_only_on_the_success_path()
    {
        var e = Enable(CeInjectScriptGenerator.Generate(Dll));

        Assert.Contains("UE5_StartedByThisRecord = true", e, StringComparison.Ordinal);

        // ⛔ THE ANTI-VACUITY HALF, and it is the assertion that actually decides this:
        // the claim must sit AFTER the already-serving bail-out, or the serving path
        // would set it on its way out and the guard would pass anyway.
        var serving = e.IndexOf("already loaded AND serving", StringComparison.Ordinal);
        var claim = e.IndexOf("UE5_StartedByThisRecord = true", StringComparison.Ordinal);
        Assert.True(serving >= 0, "the already-serving branch must still exist");
        Assert.True(claim > serving,
            "ownership must be claimed after the serving bail-out, never before it");

        // Exactly one claim site — a second one would be a way back into the defect.
        Assert.Equal(1, CountOccurrences(e, "UE5_StartedByThisRecord = true"));
    }

    [Fact]
    public void Serving_branch_clears_a_stale_ownership_flag_before_its_untick()
    {
        // [A3-B30-STALE-FLAG] The flag is ONE global in CE's Lua state, which outlives a File > Open: CE frees the
        // records without running [DISABLE]. A reloaded record ticked while the old pipe still serves takes the
        // serving branch -- and its deferred untick runs [DISABLE] with the PREVIOUS table's `true` still set.
        var e = Enable(CeInjectScriptGenerator.Generate(Dll));
        var serving = e.IndexOf("already loaded AND serving", StringComparison.Ordinal);
        Assert.True(serving >= 0, "the already-serving branch must still exist");
        var clear = e.IndexOf("UE5_StartedByThisRecord = false", serving, StringComparison.Ordinal);
        var untick = e.IndexOf("memrec.Active = false", serving, StringComparison.Ordinal);
        Assert.True(untick > serving, "the serving branch must still untick the record");
        Assert.True(clear > serving && clear < untick,
            "the serving branch must clear the flag BEFORE its deferred untick runs the disable block");
    }

    [Fact]
    public void Disable_releases_ownership_after_tearing_down()
    {
        var d = Disable(CeInjectScriptGenerator.Generate(Dll));
        Assert.Contains("UE5_StartedByThisRecord = false", d, StringComparison.Ordinal);
    }

    [Fact]
    public void Already_loaded_precheck_resolves_BOTH_mailbox_spellings()
    {
        // [B33] — this site and the autorun twin were the last two holdouts. A
        // single-spelling miss leaves `pre` nil, so a SERVING DLL is misread as
        // "parked" and UE5_AutoStart is fired at a pipe that is already up.
        var e = Enable(CeInjectScriptGenerator.Generate(Dll));
        Assert.Contains("getAddressSafe('g_invokeMailbox')", e, StringComparison.Ordinal);
        Assert.Contains("getAddressSafe('UE5Dumper.g_invokeMailbox')", e, StringComparison.Ordinal);

        // The throwing single-spelling form must be gone from this block.
        Assert.DoesNotContain("pcall(getAddress, 'g_invokeMailbox')", e, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string haystack, string needle)
    {
        int n = 0, i = 0;
        while ((i = haystack.IndexOf(needle, i, StringComparison.Ordinal)) >= 0)
        {
            n++;
            i += needle.Length;
        }
        return n;
    }

}
