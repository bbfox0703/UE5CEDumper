using System;
using System.IO;
using System.Linq;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Pins <c>executeCodeEx</c>'s call shape everywhere we emit or ship it.
///
/// <para><b>Why this file exists.</b> CE's real signature is
/// <c>executeCodeEx(callmethod, timeout, address, params...)</c> — <c>callmethod</c>
/// 0=stdcall / 1=cdecl, <c>timeout</c> in ms, and <b>the address is argument 3</b>.
/// Passing the address in slot 2 fails completely silently: CE executes with
/// <c>address = nil</c> and returns <c>nil</c> <i>without raising</i>, so a
/// <c>pcall</c> around it reports success, and a CE Disable reports a clean teardown
/// although <c>UE5_Shutdown</c> never runs.</para>
///
/// <para>Two traps in the timeout, both pinned below: <c>timeout = nil</c>/<c>-1</c>
/// waits forever (hanging CE's UI on a stalled game thread), and <c>timeout = 0</c>
/// means "don't wait" AND never frees the call memory — CE's own <c>celua.txt</c>
/// flags it as a leak.</para>
///
/// <para>The <c>.CT</c> is hand-maintained and drifts from the generators exactly
/// here, so its call sites are checked too.</para>
/// </summary>
public class CeExecuteCodeExArityTests
{
    private const string Dll = @"D:\dist\UE5Dumper.dll";

    /// <summary>The one correct prefix. Anything else in an emitted script is a bug.</summary>
    private static string ValidPrefix => $"pcall(executeCodeEx, 0, {CeLuaHygiene.DllCallTimeoutMs},";

    [Fact]
    public void Timeout_is_finite_and_nonzero()
    {
        // nil/-1 = wait forever (hangs CE's UI); 0 = no wait AND a leaked call buffer.
        Assert.True(CeLuaHygiene.DllCallTimeoutMs > 0,
            "0 means 'do not wait' and never frees the call memory — CE documents it as a leak.");
        Assert.True(CeLuaHygiene.DllCallTimeoutMs <= 60000,
            "a teardown call must not be able to wedge CE's UI for a long time.");
    }

    [Fact]
    public void Shared_emitter_passes_the_address_as_argument_three()
    {
        var sb = new System.Text.StringBuilder();
        CeLuaHygiene.AppendCallDllHelper(sb);
        var lua = sb.ToString();

        Assert.Contains(ValidPrefix + " fn)", lua, StringComparison.Ordinal);
        // The result, not pcall's status, decides success — a wrong-arity call
        // returns nil without raising, so the status would say "fine". The two are
        // separate branches because they carry different payloads: on a raise
        // pcall's second return is the Lua error, on a clean run it is the RAX.
        Assert.Contains("if not okCall then", lua, StringComparison.Ordinal);
        Assert.Contains("if ret == nil then", lua, StringComparison.Ordinal);
        Assert.DoesNotContain("return (pcall(executeCodeEx", lua, StringComparison.Ordinal);

        // CE hands back a reason string as its SECOND return value, and the six
        // possible reasons point at six different problems. Printing a guessed
        // message instead sends the user after the wrong one; this pins that CE's
        // own reason is printed.
        Assert.Contains("local okCall, ret, why = pcall(executeCodeEx", lua, StringComparison.Ordinal);
        Assert.Contains("tostring(why)", lua, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipped_dissect_script_uses_a_finite_timeout_and_reports_CEs_reason()
    {
        // ue5_dissect.lua calls executeCodeEx too, and must pass a finite timeout.
        // A nil timeout is INFINITE, not "use a default" (LuaHandler.pas:11504-11505),
        // and the wait pumps nothing — so a suspended target would freeze CE with no
        // UI-level recovery, once per FIELD of a class walk. See
        // docs/ce-plugin-sdk-notes.md §13.2-13.3.
        var lua = FindRepoFile(Path.Combine("scripts", "ue5_dissect.lua"));
        Assert.NotNull(lua);   // shipped artifact — not finding it is a real failure
        var text = File.ReadAllText(lua!);

        var calls = CodeLines(text)
            .Where(l => l.Contains("executeCodeEx(", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(calls);

        foreach (var line in calls)
        {
            Assert.Contains("executeCodeEx(1, DLL_CALL_TIMEOUT_MS, fn", line, StringComparison.Ordinal);
            Assert.DoesNotContain(", nil,", line, StringComparison.Ordinal);
        }

        Assert.Contains("local DLL_CALL_TIMEOUT_MS = ", text, StringComparison.Ordinal);
        Assert.Contains("local ret, why = executeCodeEx(", text, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("inject")]
    [InlineData("autorun")]
    public void Generated_scripts_only_call_executeCodeEx_through_the_emitter(string which)
    {
        var s = which == "inject"
            ? CeInjectScriptGenerator.Generate(Dll)
            : CeAutorunScriptGenerator.Generate(Dll);

        foreach (var line in CodeLines(s).Where(l => l.Contains("executeCodeEx", StringComparison.Ordinal)))
            Assert.Contains(ValidPrefix, line, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipped_cheat_table_passes_the_address_as_argument_three()
    {
        var ct = FindRepoFile(Path.Combine("scripts", "UE5CEDumper.CT"));
        Assert.NotNull(ct);   // the .CT is a shipped artifact; not finding it is a real failure
        var text = File.ReadAllText(ct!);

        var calls = CodeLines(text)
            .Where(l => l.Contains("executeCodeEx(", StringComparison.Ordinal))
            .ToList();
        Assert.NotEmpty(calls);

        foreach (var line in calls)
        {
            // Three positional arguments before the varargs: callmethod, timeout, address.
            Assert.Contains("executeCodeEx(0, UE5_CALL_TIMEOUT_MS, fn", line, StringComparison.Ordinal);
        }

        Assert.Contains("local UE5_CALL_TIMEOUT_MS = ", text, StringComparison.Ordinal);
        // callmethod is a calling convention; a comment that reads it as a return
        // type invites a call that puts the address in the timeout slot.
        Assert.DoesNotContain("executeCodeEx: retType", text, StringComparison.Ordinal);

        // CE's second return value names the failure. A message that guesses at a
        // dead process is wrong for four of the six reasons, which occur with a
        // perfectly healthy one. Checked over CODE lines only: a comment in the .CT
        // quotes the guessed string deliberately, to explain it — and a whole-text
        // DoesNotContain would match that explanation and fail a correct table.
        Assert.Contains("local result, why = executeCodeEx(", text, StringComparison.Ordinal);
        Assert.DoesNotContain("process alive?", string.Join("\n", CodeLines(text)),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Shipped_cheat_table_unticks_the_record_when_inject_bails()
    {
        // memrec only exists in the record's own chunk, so ue5_inject has to report
        // failure and the [ENABLE] block has to act on it. Without this, CE leaves the
        // record ticked after an "already loaded and serving" bail-out, and unticking
        // then runs a real UE5_Shutdown against a proxy this script never injected.
        var ct = FindRepoFile(Path.Combine("scripts", "UE5CEDumper.CT"));
        Assert.NotNull(ct);
        var text = File.ReadAllText(ct!);

        Assert.Contains("if ue5_inject() == false then", text, StringComparison.Ordinal);
        Assert.Contains("memrec.Active = false", text, StringComparison.Ordinal);
        // ...and the disable side stays quiet when nothing was ever loaded.
        Assert.Contains("Nothing loaded — nothing to shut down.", text, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipped_cheat_table_clears_a_stale_ownership_flag_in_the_serving_branch()
    {
        // [A3-B30-STALE-FLAG] The same global, the same CE session, the same File > Open: ue5_inject's serving
        // branch returns false, the [ENABLE] block defers an untick, and [DISABLE] runs ue5_shutdown -- whose
        // guard a flag left true by the previous table would pass.
        var ct = FindRepoFile(Path.Combine("scripts", "UE5CEDumper.CT"));
        Assert.NotNull(ct);
        var text = File.ReadAllText(ct!);

        var serving = text.IndexOf("already loaded and serving in this process as '", StringComparison.Ordinal);
        Assert.True(serving >= 0, "the serving branch must still exist");
        var ret = text.IndexOf("return false", serving, StringComparison.Ordinal);
        var clear = text.IndexOf("UE5_StartedByThisRecord = false", serving, StringComparison.Ordinal);
        Assert.True(clear > serving && clear < ret,
            "ue5_inject's serving branch must clear the flag before it returns false");
    }

    [Fact]
    public void Shipped_cheat_table_clears_a_stale_ownership_flag_before_its_first_bailout()
    {
        // [A3-B30-STALE-FLAG] ue5_inject's DLL-not-found bail-out runs BEFORE the serving check. With the clear after
        // it, a cancelled file picker there would return false with the previous table's flag still true, and the
        // untick would then pass ue5_shutdown's guard and tear a serving pipe down. The clear leads the function.
        var ct = FindRepoFile(Path.Combine("scripts", "UE5CEDumper.CT"));
        Assert.NotNull(ct);
        var text = File.ReadAllText(ct!);

        var fn = text.IndexOf("function ue5_inject()", StringComparison.Ordinal);
        Assert.True(fn >= 0, "ue5_inject must still exist");
        var firstBail = text.IndexOf("return false", fn, StringComparison.Ordinal);
        var dllCheck = text.IndexOf("if not DLL_PATH then", fn, StringComparison.Ordinal);
        var clear = text.IndexOf("UE5_StartedByThisRecord = false", fn, StringComparison.Ordinal);
        Assert.True(clear > fn && clear < dllCheck && clear < firstBail,
            "ue5_inject must clear the flag before its DLL-path check and its first return false");
    }

    [Theory]
    [InlineData("inject")]
    [InlineData("autorun")]
    public void Teardown_calls_only_UE5_Shutdown_never_UE5_StopPipeServer(string which)
    {
        // UE5_Shutdown IS s_pipeServer.Stop() plus everything else, and it runs that
        // Stop deliberately AFTER Stark::Shutdown so a pipe thread blocked in
        // EnqueueInvoke receives its -7 and unwinds. Calling UE5_StopPipeServer first
        // inverts that ordering — and because the CE call times out at 5 s while the
        // remote thread keeps running, it puts a SECOND teardown into the game process
        // concurrently with the first.
        //
        // The export stays (third-party CE scripts use it standalone) and the
        // getAddress PROBE stays (it is how the block detects "nothing was loaded").
        // What must not appear is a call to it.
        var s = which == "inject"
            ? CeInjectScriptGenerator.Generate(Dll)
            : CeAutorunScriptGenerator.Generate(Dll);

        Assert.Contains("callDLL('UE5_Shutdown')", s, StringComparison.Ordinal);
        Assert.DoesNotContain("callDLL('UE5_StopPipeServer')", s, StringComparison.Ordinal);
        Assert.Contains("pcall(getAddress, 'UE5_StopPipeServer')", s, StringComparison.Ordinal);
    }

    [Fact]
    public void Shipped_cheat_table_teardown_calls_only_UE5_Shutdown()
    {
        var ct = FindRepoFile(Path.Combine("scripts", "UE5CEDumper.CT"));
        Assert.NotNull(ct);
        var text = File.ReadAllText(ct!);

        Assert.Contains("ue5_callDLL(\"UE5_Shutdown\", \"void\")", text, StringComparison.Ordinal);
        Assert.DoesNotContain("ue5_callDLL(\"UE5_StopPipeServer\"", text, StringComparison.Ordinal);
        // ...and it must not report success for a call that never returned.
        Assert.Contains("UE5_Shutdown did not return in time", text, StringComparison.Ordinal);
    }

    /// <summary>Lua source lines with full-line comments dropped — the comments in
    /// these files name executeCodeEx deliberately, to explain the trap.</summary>
    private static System.Collections.Generic.IEnumerable<string> CodeLines(string lua) =>
        lua.Split('\n').Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal));

    private static string? FindRepoFile(string relative)
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            string candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate)) return candidate;
        }
        return null;
    }
}
