using System;
using System.Linq;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Locks the shape of the CE autorun helper (<see cref="CeAutorunScriptGenerator"/>) —
/// the standalone Lua dropped in <c>&lt;CheatEngine&gt;\autorun\</c> so every table gets
/// <c>ue5_inject()</c> permanently, with no <c>.CT</c> and no AOBMaker plugin.
///
/// The invariant that dominates every other one: <b>this file must only DEFINE things
/// at load time.</b> CE runs autorun before any process is attached, so a stray
/// top-level <c>injectDLL</c> / <c>getOpenedProcessID</c> / <c>readInteger</c> would
/// fire on every CE launch against nothing.
/// </summary>
public class CeAutorunScriptGeneratorTests
{
    private const string Dll = @"D:\dist\UE5Dumper.dll";

    /// <summary>Lines that are neither comments nor blank — i.e. real statements.</summary>
    private static string[] CodeLines(string lua) =>
        lua.Split('\n')
           .Where(l => l.Trim().Length > 0 && !l.TrimStart().StartsWith("--", StringComparison.Ordinal))
           .ToArray();

    /// <summary>Statements at column 0 that are NOT inside a function body.
    /// Everything the file executes at CE start-up lives here.</summary>
    private static string[] TopLevelCode(string lua)
    {
        var top = new System.Collections.Generic.List<string>();
        var inFunction = false;
        foreach (var raw in lua.Split('\n'))
        {
            var line = raw.TrimEnd();
            if (line.Trim().Length == 0 || line.TrimStart().StartsWith("--", StringComparison.Ordinal))
                continue;
            if (!inFunction && line.StartsWith("function ", StringComparison.Ordinal))
            {
                inFunction = true;
                continue;
            }
            if (inFunction)
            {
                // A column-0 `end` closes the top-level function.
                if (line == "end") inFunction = false;
                continue;
            }
            top.Add(line);
        }
        return top.ToArray();
    }

    [Fact]
    public void Defines_the_two_globals_every_table_needs()
    {
        var s = CeAutorunScriptGenerator.Generate(Dll);
        Assert.Contains("function ue5_inject()", s, StringComparison.Ordinal);
        Assert.Contains("function ue5_shutdown()", s, StringComparison.Ordinal);
    }

    [Fact]
    public void Nothing_process_dependent_runs_at_load_time()
    {
        // THE invariant: autorun executes before any process is attached.
        var top = string.Join('\n', TopLevelCode(CeAutorunScriptGenerator.Generate(Dll)));
        foreach (var api in new[] { "injectDLL", "getOpenedProcessID", "readInteger",
                                    "executeCodeEx", "showMessage" })
            Assert.DoesNotContain(api, top, StringComparison.Ordinal);
    }

    [Fact]
    public void Top_level_work_is_confined_to_definitions_and_guarded_extras()
    {
        // Whatever DOES run at load time must be a local, a pcall-guarded cosmetic,
        // or the idempotency guard — never bare work.
        foreach (var line in TopLevelCode(CeAutorunScriptGenerator.Generate(Dll)))
        {
            var t = line.Trim();
            var allowed =
                t.StartsWith("local ", StringComparison.Ordinal) ||
                t.StartsWith("pcall(", StringComparison.Ordinal) ||
                t.StartsWith("dbg(", StringComparison.Ordinal) ||
                t.StartsWith("if ", StringComparison.Ordinal) ||
                t.StartsWith("end", StringComparison.Ordinal) ||
                t.StartsWith("else", StringComparison.Ordinal) ||
                // the pcall-wrapped menu closure body
                t.StartsWith("item.", StringComparison.Ordinal) ||
                t.StartsWith("parent.", StringComparison.Ordinal) ||
                t.StartsWith("ue5_menuAdded", StringComparison.Ordinal) ||
                t.StartsWith("')", StringComparison.Ordinal) ||
                t.StartsWith("dbg", StringComparison.Ordinal);
            Assert.True(allowed, $"unexpected top-level statement: {t}");
        }
    }

    [Fact]
    public void Menu_registration_is_pcall_guarded_and_idempotent()
    {
        // Autorun runs earlier than any table script; a cosmetic menu entry must
        // never be able to break CE start-up, and a manual re-run must not
        // double-add the item.
        var s = CeAutorunScriptGenerator.Generate(Dll);
        Assert.Contains("if not ue5_menuAdded then", s, StringComparison.Ordinal);
        var menuIdx = s.IndexOf("getMainForm()", StringComparison.Ordinal);
        Assert.True(menuIdx > 0, "menu registration missing");
        var guardIdx = s.LastIndexOf("pcall(function()", menuIdx, StringComparison.Ordinal);
        Assert.True(guardIdx > 0 && guardIdx < menuIdx,
            "getMainForm() must sit inside a pcall");
    }

    [Fact]
    public void Uses_the_verified_ce_menu_api_shape()
    {
        // Matches the pattern proven by the vendored UE4 Dumper.CT — createMenuItem
        // on the parent, parent.add, then Caption / OnClick.
        var s = CeAutorunScriptGenerator.Generate(Dll);
        Assert.Contains("getMainForm().Menu.Items", s, StringComparison.Ordinal);
        Assert.Contains("createMenuItem(parent)", s, StringComparison.Ordinal);
        Assert.Contains("parent.add(item)", s, StringComparison.Ordinal);
        Assert.Contains("item.Caption =", s, StringComparison.Ordinal);
        Assert.Contains("item.OnClick =", s, StringComparison.Ordinal);
    }

    [Fact]
    public void Inject_polls_readiness_and_never_uses_executeCodeEx_on_the_startup_path()
    {
        // Twin of CeInjectScriptGeneratorTests' rule, narrowed for the same reason
        // (audit #4 B1(b)): ue5_inject now also revives an already-mapped DLL that a
        // previous ue5_shutdown parked, and that needs a remote call because the
        // mailbox poller has been joined. Injection-time — from injectDLL through the
        // readiness poll — is the part that runs while a game may still be blocking
        // CreateRemoteThread, and it stays clean.
        var s = CeAutorunScriptGenerator.Generate(Dll);
        var inject = s.Substring(s.IndexOf("function ue5_inject()", StringComparison.Ordinal),
            s.IndexOf("function ue5_shutdown()", StringComparison.Ordinal)
                - s.IndexOf("function ue5_inject()", StringComparison.Ordinal));
        Assert.Contains("readInteger, mb + 0x0C", inject, StringComparison.Ordinal);
        Assert.Contains($"sleep({CeReadinessLua.PollIntervalMs})", inject, StringComparison.Ordinal);

        var startupPath = string.Join('\n', CodeLines(
            inject.Substring(inject.IndexOf("injectDLL(DLL_PATH)", StringComparison.Ordinal))));
        Assert.DoesNotContain("executeCodeEx", startupPath, StringComparison.Ordinal);

        // The revive path goes through the shared emitter, never a hand-rolled call:
        // that emitter is the one place that knows executeCodeEx's real signature.
        Assert.Contains("callDLL('UE5_AutoStart')", inject, StringComparison.Ordinal);
        Assert.Contains($"pcall(executeCodeEx, 0, {CeLuaHygiene.DllCallTimeoutMs},", inject,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Shutdown_may_use_executeCodeEx()
    {
        var s = CeAutorunScriptGenerator.Generate(Dll);
        var shutdown = s.Substring(s.IndexOf("function ue5_shutdown()", StringComparison.Ordinal));
        Assert.Contains("pcall(executeCodeEx,", shutdown, StringComparison.Ordinal);
        // ...but stays silent when nothing was ever loaded.
        Assert.Contains("nothing to shut down", shutdown, StringComparison.Ordinal);
    }

    [Fact]
    public void Bakes_and_escapes_the_dll_path()
    {
        var s = CeAutorunScriptGenerator.Generate(Dll);
        Assert.Contains(@"local DLL_PATH = 'D:\\dist\\UE5Dumper.dll'", s, StringComparison.Ordinal);
    }

    [Fact]
    public void Is_quiet_by_default_but_never_auto_closes()
    {
        var s = CeAutorunScriptGenerator.Generate(Dll);
        Assert.Contains("local function dbg(", s, StringComparison.Ordinal);
        // No window of its own — closing the Lua Engine on someone who opened it
        // deliberately would be hostile.
        Assert.DoesNotContain(CeLuaHygiene.CloseCall, s, StringComparison.Ordinal);
    }

    /// <summary>
    /// B23 — this file is loaded by CE's AUTORUN at start-up, so <c>DEBUG</c> must be
    /// read at CALL time. The eager preamble captured <c>UE5_DEBUG</c> into a local when
    /// the chunk ran — before the Lua console existed — which made the script's own
    /// instruction ("Set UE5_DEBUG = 1 in the Lua console") impossible to follow: the
    /// local was already 0 and nothing re-read it.
    /// </summary>
    [Fact]
    public void Debug_flag_is_read_at_call_time_not_at_load_time()
    {
        var s = CeAutorunScriptGenerator.Generate(Dll);

        // The instruction is still there, so it had better be true.
        Assert.Contains("Set UE5_DEBUG = 1 in the Lua console", s, StringComparison.Ordinal);
        Assert.Contains("return (UE5_DEBUG or 0) ~= 0", s, StringComparison.Ordinal);
        // ...and the load-time capture must be gone: a stale local IS the bug.
        Assert.DoesNotContain("local DEBUG = UE5_DEBUG or 0", s, StringComparison.Ordinal);
    }

    [Fact]
    public void Reports_failures_rather_than_returning_success()
    {
        var s = CeAutorunScriptGenerator.Generate(Dll);
        // Each of the three readiness failure verdicts must end in `return false`.
        foreach (var probe in new[] { "never published its state", "start-up did not finish",
                                      "Timed out waiting" })
        {
            var idx = s.IndexOf(probe, StringComparison.Ordinal);
            Assert.True(idx > 0, $"missing failure message: {probe}");
            var next = s.IndexOf("return", idx, StringComparison.Ordinal);
            Assert.StartsWith("return false", s.Substring(next), StringComparison.Ordinal);
        }
    }

    [Fact]
    public void Line_endings_are_lf_only()
    {
        Assert.DoesNotContain("\r", CeAutorunScriptGenerator.Generate(Dll), StringComparison.Ordinal);
    }

    [Fact]
    public void Target_file_and_folder_names_match_what_ce_scans()
    {
        Assert.Equal("ue5_autorun.lua", CeAutorunScriptGenerator.DefaultFileName);
        Assert.Equal("autorun", CeAutorunScriptGenerator.AutorunFolderName);
    }
}
