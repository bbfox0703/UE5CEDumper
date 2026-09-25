using System;
using System.IO;
using System.Linq;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Pins the shipped <c>scripts/UE5CEDumper.CT</c>'s "where is UE5Dumper.dll" block.
///
/// <para>The bug being guarded (2026-08-04): a user opened the table from Cheat Engine's
/// recent-files menu with the DLL in the same folder, and the script reported it missing.
/// A CE table script cannot read its own <c>.CT</c> path — no such API exists — so the
/// folder is inferred, and every source it used (the Open/Save dialog objects) is empty
/// unless the table came through <c>File &gt; Open</c>. The script also told the user to
/// "place UE5Dumper.dll in the same folder as this CT file", which is what they had
/// already done.</para>
///
/// <para>None of this can be executed here — it is CE Lua — so these assertions are
/// structural: the sources are present, the risky ones are guarded, the order puts
/// table-derived folders above CE's own install folder, and the misleading text is gone.</para>
/// </summary>
public class CtDllDiscoveryTests
{
    private static string Ct()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        for (int i = 0; i < 8 && dir != null; i++, dir = dir.Parent)
        {
            var p = Path.Combine(dir.FullName, "scripts", "UE5CEDumper.CT");
            if (File.Exists(p)) return File.ReadAllText(p);
        }
        throw new FileNotFoundException("scripts/UE5CEDumper.CT not found from " + AppContext.BaseDirectory);
    }

    /// <summary>Lua source with full-line comments dropped. The comments here name dead
    /// APIs deliberately, to record WHY they are dead — an assertion about what the code
    /// does must not match them.</summary>
    private static string CodeOnly(string lua) =>
        string.Join('\n', lua.Split('\n')
            .Where(l => !l.TrimStart().StartsWith("--", StringComparison.Ordinal)));

    [Fact]
    public void Reads_CEs_recent_files_list_because_that_is_the_only_channel_a_double_click_fills()
    {
        var s = Ct();
        // MEASURED on a real CE build: getSettings() cannot read this value.
        // "Recent Files" is REG_MULTI_SZ, and both documented accessors refuse that
        // type — Value["Recent Files"] returns an EMPTY string and
        // getBinaryValue("Recent Files") returns nil, despite celua.txt:3516
        // advertising a bytetable. A control read in the same session
        // (getSettings("Plugins64").Value["00000000 A"]) succeeded, so it is the TYPE
        // that is unreadable, not the call. reg.exe is the working route.
        Assert.Contains("reg query", s, StringComparison.Ordinal);
        Assert.Contains("Recent Files", s, StringComparison.Ordinal);
        Assert.Contains("REG_MULTI_SZ%s+", s, StringComparison.Ordinal);
        // The dead API must not creep back in AS CODE -- the note explaining why it
        // is dead is a comment, and comments are excluded here.
        var code = CodeOnly(s);
        Assert.DoesNotContain("getBinaryValue", code, StringComparison.Ordinal);
        Assert.DoesNotContain("getSettings(", code, StringComparison.Ordinal);
    }

    [Fact]
    public void The_registry_read_is_deferred_and_self_healing()
    {
        var s = Ct();
        // Shelling out flashes a console window, so it must run only after every cheap
        // slot has missed...
        int probe1 = s.IndexOf("_probeSlots(1)", StringComparison.Ordinal);
        int reg = s.IndexOf("reg query", StringComparison.Ordinal);
        Assert.True(probe1 > 0 && reg > probe1,
            "the reg.exe read must come after the first probe pass, not before it");
        Assert.Contains("if not DLL_PATH then", s, StringComparison.Ordinal);
        // ...and a hit must be recorded so no later run needs it again.
        Assert.Contains("ue5_recordDllDir(_dllFoundIn)", s, StringComparison.Ordinal);
    }

    [Fact]
    public void Picks_the_MRU_entry_by_this_tables_own_filename()
    {
        // Measured on a real machine: UE5CEDumper.CT was entry 2, not entry 1. Taking
        // entry[0] blindly would bind to an unrelated game's folder and could load a
        // stale UE5Dumper.dll sitting beside it.
        var s = Ct();
        Assert.Contains("\"ue5cedumper.ct\"", s, StringComparison.Ordinal);
        Assert.Contains("extractFileName", s, StringComparison.Ordinal);
    }

    [Fact]
    public void Reads_the_UI_breadcrumb_file()
    {
        var s = Ct();
        // Must agree with Constants.DllPathBreadcrumbFile / DumperDllPathStore.
        Assert.Contains("dll-path.txt", s, StringComparison.Ordinal);
        Assert.Equal("dll-path.txt", UE5DumpUI.Constants.DllPathBreadcrumbFile);
    }

    [Fact]
    public void Table_derived_folders_outrank_the_CE_install_folder()
    {
        // A UE5Dumper.dll in CE's own folder can only have been hand-placed, and is
        // most likely a stale build. Silently loading it is worse than failing.
        var s = Ct();
        int crumb = s.IndexOf("io.open(_appData", StringComparison.Ordinal);
        int ceDir = s.IndexOf("getCheatEngineDir", StringComparison.Ordinal);
        Assert.True(crumb > 0 && ceDir > 0);
        Assert.True(crumb < ceDir, "the UI breadcrumb must be probed before CE's install folder");
        // The recent-files slots are the exception, and deliberately so: they are
        // appended AFTER the first probe pass because reading them spawns a console
        // window. They are last in file order but they are also the only slots that
        // verify the folder is this table's own, so a hit there is trustworthy.
    }

    [Fact]
    public void A_miss_no_longer_aborts_the_whole_table_script()
    {
        // The old code returned out of the [ENABLE] chunk, leaving ue5_inject /
        // ue5_shutdown / ue5_log undefined — so the next tick failed with "attempt to
        // call a nil value", a second error that looked unrelated to the first.
        var s = Ct();
        Assert.Contains("function ue5_renderDllSearch()", s, StringComparison.Ordinal);
        Assert.Contains("function ue5_pickDllManually()", s, StringComparison.Ordinal);
        // [PATH-CT-INJECT-ANSI] The pick sets both encodings of the path (injectDLL needs the ANSI one).
        Assert.Contains("DLL_PATH_ANSI, DLL_PATH = ue5_pathForms(ue5_pickDllManually(), false)", s,
            StringComparison.Ordinal);
    }

    [Fact]
    public void The_failure_message_no_longer_tells_the_user_to_do_what_they_already_did()
    {
        var s = Ct();
        Assert.DoesNotContain("Please place UE5Dumper.dll in the same folder as this CT file",
            s, StringComparison.Ordinal);
        // ...and names the real cause plus every way out.
        Assert.Contains("has no way to read its own .CT path", s, StringComparison.Ordinal);
        Assert.Contains("Launch UE5DumpUI.exe once", s, StringComparison.Ordinal);
        Assert.Contains("File &gt; Open", s, StringComparison.Ordinal);
        Assert.Contains("file picker", s, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_undocumented_CE_probe_is_pcall_guarded()
    {
        // These are undocumented published components / optional APIs. An unguarded
        // read that throws aborts the chunk — which is how the Save-dialog read used to
        // kill the script before the log was even open.
        // Comment lines mention these APIs deliberately, to explain why they are
        // guarded — match CODE only. And the guard is not always on the same line:
        // `pcall(function()` may open a block a few lines above, which is how the
        // recent-files reader is written.
        var lines = Ct().Split('\n');
        foreach (var api in new[] { "OpenDialog1.FileName", "SaveDialog1.FileName",
                                    "getCurrentScriptPath()", "io.popen(",
                                    "createOpenDialog(" })
        {
            int at = -1;
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].TrimStart().StartsWith("--", StringComparison.Ordinal)) continue;
                if (lines[i].Contains(api, StringComparison.Ordinal)) { at = i; break; }
            }
            Assert.True(at >= 0, $"{api} missing from the .CT's code");

            bool guarded = false;
            for (int i = Math.Max(0, at - 3); i <= at && !guarded; i++)
                guarded = lines[i].Contains("pcall", StringComparison.Ordinal);
            Assert.True(guarded,
                $"{api} at line {at + 1} is not inside a pcall — a throw there aborts the " +
                "whole chunk, which is how the bare SaveDialog1 read used to kill the script " +
                "before the log was open.");
        }
    }

    /// <summary>
    /// B33 — the readiness poll must try BOTH symbol spellings, via the single shared
    /// helper. Two hand-copied call sites is how the bare-only spelling survived here
    /// while eight other places in the repo already tried both.
    /// </summary>
    [Fact]
    public void Mailbox_symbol_is_resolved_through_the_two_spelling_helper()
    {
        var code = CodeOnly(Ct());
        Assert.Contains("function ue5_findMailbox()", code, StringComparison.Ordinal);
        Assert.Contains("\"UE5Dumper.g_invokeMailbox\"", code, StringComparison.Ordinal);
        // No call site may resolve the bare spelling on its own any more.
        Assert.DoesNotContain("getAddress, \"g_invokeMailbox\"", code, StringComparison.Ordinal);
    }

    /// <summary>
    /// B32 — a readiness field that NEVER left IDLE is an old DLL (offset 0x0C used to be
    /// an <c>int32_t reserved</c> nothing wrote), not a wedged scan. Reporting it as a
    /// timeout threw a modal error on a perfectly healthy injection, so the two outcomes
    /// must be distinguished before the error branch is reached.
    /// </summary>
    [Fact]
    public void Never_leaving_idle_is_not_reported_as_a_timeout()
    {
        var code = CodeOnly(Ct());
        Assert.Contains("sawProgress", code, StringComparison.Ordinal);
        var progressIdx = code.IndexOf("elseif not sawProgress then", StringComparison.Ordinal);
        var timeoutIdx = code.IndexOf("Timed out after", StringComparison.Ordinal);
        Assert.True(progressIdx > 0, "the never-progressed branch is missing");
        Assert.True(timeoutIdx > 0, "the timeout branch is missing");
        Assert.True(progressIdx < timeoutIdx,
            "the never-progressed branch must be tested BEFORE the timeout error, or an " +
            "old DLL still lands in the modal.");
    }

    [Fact]
    public void Stale_auto_run_comment_is_gone()
    {
        // The block is the [ENABLE] of CheatEntry 102, not a table-load LuaScript —
        // the file has no <LuaScript> element at all.
        var s = Ct();
        Assert.DoesNotContain("Auto-runs when this CT is opened", s, StringComparison.Ordinal);
        Assert.DoesNotContain("<LuaScript>", s, StringComparison.Ordinal);
    }
}
