using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Log retention rules: folders expire by AGE alone, and the 8 MB per-file cap is a
/// rotation point, never a kill switch that silently ends the log mid-session.
/// </summary>
public class LoggingServiceRetentionTests : IDisposable
{
    private readonly string _dir;

    public LoggingServiceRetentionTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "ue5cd-logret-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // ----- AGE is the only folder-retention rule --------------------------------
    //
    // No count-based cap: it contradicts CLAUDE.md's Log-output rule, has nothing to
    // guard against (a folder is named after the GAME, so the count is "distinct games
    // played"), and an eviction deletes RECURSIVELY — taking the DLL's five log
    // categories with it, which Sein.cpp's age-only sweep keeps.
    //
    // Tested through the real service against real directories rather than through a
    // pure helper, because what has to hold is an ABSENCE — and the honest way to
    // pin "nothing deletes this" is to put a folder on disk and check it survives.

    private void MakeFolder(string name, int daysOld)
    {
        var dir = Path.Combine(_dir, name);
        Directory.CreateDirectory(dir);
        var file = Path.Combine(dir, "dll-init-0.log");
        File.WriteAllText(file, "x");
        File.SetLastWriteTimeUtc(file, DateTime.UtcNow.AddDays(-daysOld));
    }

    [Fact]
    public void Far_more_than_twenty_recent_game_folders_all_survive()
    {
        // The negative control for a count cap: 30 games played inside the 21-day
        // window. A cap of 20 would delete ten of these — recursively, including the
        // DLL's own logs.
        for (int i = 0; i < 30; i++) MakeFolder($"Game{i:D2}", daysOld: 1);

        using (var log = new LoggingService(_dir, writeToConsole: false))
            log.StartProcessMirror("Game00");

        for (int i = 0; i < 30; i++)
            Assert.True(Directory.Exists(Path.Combine(_dir, $"Game{i:D2}")),
                $"Game{i:D2} was deleted inside the {UE5DumpUI.Constants.LogMaxAgeDays}-day " +
                "window — folder retention is by AGE only (CLAUDE.md Log output).");
    }

    [Fact]
    public void A_folder_past_the_age_window_is_still_deleted()
    {
        // The other direction, so the test above cannot be satisfied by removing all
        // retention: age remains the policy and it still fires.
        MakeFolder("StaleGame", daysOld: UE5DumpUI.Constants.LogMaxAgeDays + 5);
        MakeFolder("FreshGame", daysOld: 1);

        using (new LoggingService(_dir, writeToConsole: false)) { }   // CleanupOldLogFolders runs in the ctor

        Assert.False(Directory.Exists(Path.Combine(_dir, "StaleGame")));
        Assert.True(Directory.Exists(Path.Combine(_dir, "FreshGame")));
    }

    [Fact]
    public void The_UI_own_folder_survives_even_when_it_is_stale()
    {
        // The UI folder is the one a user is asked to send when
        // something goes wrong, and the age sweep exempts it by identity.
        MakeFolder(UE5DumpUI.Constants.LogSubfolderName,
                   daysOld: UE5DumpUI.Constants.LogMaxAgeDays + 100);

        using (new LoggingService(_dir, writeToConsole: false)) { }

        Assert.True(Directory.Exists(
            Path.Combine(_dir, UE5DumpUI.Constants.LogSubfolderName)));
    }

    // ----- the size cap must ROLL, not stop writing ----------------------------

    [Fact]
    public void Crossing_the_size_cap_rolls_instead_of_silently_dropping_events()
    {
        // The property that matters, tested the only honest way: write past the cap and
        // then check the later events are actually ON DISK. Serilog's default is
        // rollOnFileSizeLimit:false, under which the sink stops emitting for the rest of
        // the process — no exception, no SelfLog, and the log simply ends mid-session
        // while docs/architecture.md promises it archives.
        //
        // 4 KB per message keeps this to a couple of thousand writes rather than ~84k.
        var payload = new string('x', 4096);
        const string marker = "MARKER-AFTER-THE-CAP";

        using (var log = new LoggingService(_dir, writeToConsole: false))
        {
            long budget = UE5DumpUI.Constants.LogMaxSizeBytes + (2L * 1024 * 1024);
            for (long written = 0; written < budget; written += payload.Length)
                log.Info(UE5DumpUI.Constants.LogCatInit, payload);

            log.Info(UE5DumpUI.Constants.LogCatInit, marker);
        }

        var initLogs = Directory.GetFiles(_dir, "init-*.log", SearchOption.AllDirectories);
        Assert.NotEmpty(initLogs);

        // More than one file => it rolled rather than freezing at the limit.
        Assert.True(initLogs.Length > 1,
            $"expected the sink to roll past {UE5DumpUI.Constants.LogMaxSizeBytes} bytes, " +
            $"but only {initLogs.Length} file(s) exist — rollOnFileSizeLimit is off again");

        // And the decisive one: the event written AFTER the cap survived.
        Assert.Contains(initLogs, f => File.ReadAllText(f).Contains(marker, StringComparison.Ordinal));
    }

    [Fact]
    public void A_logged_message_is_written_verbatim_braces_included()
    {
        // [PATH-SERILOG-BRACES] Info/Warn/Error/Debug must not pass the text to Serilog as a message TEMPLATE: a doubled
        // brace in a folder name would be logged single ("A{{B}}" -> "A{B}"), and the leftover finder, which recovers
        // deploy paths from this log, would look for a folder that does not exist.
        string[] texts =
        {
            @"Deployed version.dll to G: E:\Games\A{{B}}\Binaries\Win64\version.dll",
            @"E:\Games\Title {GOTY}\x.dll",
            "index {0} and {Name:l} and }} and {{",
        };
        using (var log = new LoggingService(_dir, writeToConsole: false))
        {
            log.Info(UE5DumpUI.Constants.LogCatInit, texts[0]);
            log.Warn(UE5DumpUI.Constants.LogCatInit, texts[1]);
            log.Error(UE5DumpUI.Constants.LogCatInit, texts[2]);
        }
        string all = string.Concat(Directory.GetFiles(_dir, "init-*.log", SearchOption.AllDirectories)
            .Select(File.ReadAllText));
        foreach (var t in texts)
            Assert.Contains(t, all, StringComparison.Ordinal);
    }
}
