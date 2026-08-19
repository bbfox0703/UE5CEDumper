using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Log retention rules that had no test at all before audit #4 — which is how a
/// count-based sweep came to rank folders by the one signal its own sibling
/// documents as unusable (B37), and how an 8 MB "cap" turned out to be a kill
/// switch rather than a rotation point (B31).
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

    // ----- AF9: AGE is the only folder-retention rule --------------------------
    //
    // These replace the five B37 tests, which pinned the RANKING of a count-based cap
    // (`SelectFoldersToEvict`, keep the newest 20). Audit #4 fixed that ranking and the
    // tests were right about it; audit #5 found the cap itself contradicted CLAUDE.md's
    // Log-output rule, had nothing to guard against (a folder is named after the GAME,
    // so the count is "distinct games played"), and deleted RECURSIVELY — taking the
    // DLL's five log categories with it, which Sein.cpp's age-only sweep had kept.
    //
    // Tested through the real service against real directories rather than through a
    // pure helper, because what has to hold now is an ABSENCE — and the honest way to
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
        // The negative control for the removal: 30 games played inside the 21-day
        // window. Under the old cap of 20, ten of these were deleted — recursively,
        // including the DLL's own logs — and reported at Debug only.
        for (int i = 0; i < 30; i++) MakeFolder($"Game{i:D2}", daysOld: 1);

        using (var log = new LoggingService(_dir))
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

        using (new LoggingService(_dir)) { }   // CleanupOldLogFolders runs in the ctor

        Assert.False(Directory.Exists(Path.Combine(_dir, "StaleGame")));
        Assert.True(Directory.Exists(Path.Combine(_dir, "FreshGame")));
    }

    [Fact]
    public void The_UI_own_folder_survives_even_when_it_is_stale()
    {
        // Carried over from B37: the UI folder is the one a user is asked to send when
        // something goes wrong, and the age sweep exempts it by identity.
        MakeFolder(UE5DumpUI.Constants.LogSubfolderName,
                   daysOld: UE5DumpUI.Constants.LogMaxAgeDays + 100);

        using (new LoggingService(_dir)) { }

        Assert.True(Directory.Exists(
            Path.Combine(_dir, UE5DumpUI.Constants.LogSubfolderName)));
    }

    // ----- B31: the size cap must ROLL, not stop writing -----------------------

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

        using (var log = new LoggingService(_dir))
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
}
