using System.IO;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// Mock platform service for AobUsageService testing.
/// </summary>
public sealed class MockPlatformService : IPlatformService
{
    private readonly string _appDataPath;

    public MockPlatformService(string appDataPath)
    {
        _appDataPath = appDataPath;
    }

    public bool TryAcquireSingleInstance() => true;
    public void ReleaseSingleInstance() { }
    public string GetAppDataPath() => _appDataPath;
    public string GetLogDirectoryPath() => Path.Combine(_appDataPath, "Logs");

    /// <summary>Last text passed to <see cref="CopyToClipboardAsync"/> (null until first call).</summary>
    public string? LastClipboard { get; private set; }
    public Task<bool> CopyToClipboardAsync(string text) { LastClipboard = text; return Task.FromResult(true); }
    public Task RevealInExplorerAsync(string path) => Task.CompletedTask;
    public string GetMachineName() => "TEST-MACHINE";
    public void CloseImeForWindow(IntPtr windowHandle) { }
    public Task<string?> ShowSaveFileDialogAsync(string defaultFileName, string filterName, string filterExtension) => Task.FromResult<string?>(null);
}

public class AobUsageServiceTests : IDisposable
{
    private readonly string _tempDir;
    private readonly MockPlatformService _platform;
    private readonly MockLoggingService _log;

    public AobUsageServiceTests()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), $"UE5DumpTest_{Guid.NewGuid():N}");
        Directory.CreateDirectory(_tempDir);
        _platform = new MockPlatformService(_tempDir);
        _log = new MockLoggingService();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_tempDir))
                Directory.Delete(_tempDir, recursive: true);
        }
        catch { /* cleanup best-effort */ }
    }

    private AobUsageService CreateService() => new(_platform, _log);

    private static EngineState MakeState(string peHash = "5F3A1B2CCDD40000", string moduleName = "TestGame.exe") => new()
    {
        PeHash = peHash,
        ModuleName = moduleName,
        UEVersion = 504,
        VersionDetected = true,
        GObjectsAddr = "0x7FF600A12340",
        GNamesAddr = "0x7FF600B56780",
        GWorldAddr = "0x7FF600C89000",
        GObjectsMethod = "aob",
        GNamesMethod = "string_ref",
        GWorldMethod = "not_found",
        GObjectsPatternId = "GOBJ_V1",
        GNamesPatternId = "",
        GWorldPatternId = "",
        GObjectsPatternsTried = 40,
        GObjectsPatternsHit = 3,
        GNamesPatternsTried = 27,
        GNamesPatternsHit = 0,
        GWorldPatternsTried = 37,
        GWorldPatternsHit = 0,
    };

    [Fact]
    public async Task RecordScan_CreatesNewFile()
    {
        var svc = CreateService();
        await svc.RecordScanAsync(MakeState());

        Assert.True(File.Exists(svc.FilePath));

        var json = await File.ReadAllTextAsync(svc.FilePath, TestContext.Current.CancellationToken);
        var file = JsonSerializer.Deserialize(json, AobUsageJsonContext.Default.AobUsageFile);

        Assert.NotNull(file);
        Assert.Equal(1, file!.Version);
        Assert.Equal("TEST-MACHINE", file.MachineName);
        Assert.Single(file.Games);
        Assert.True(file.Games.ContainsKey("5F3A1B2CCDD40000"));

        var record = file.Games["5F3A1B2CCDD40000"];
        Assert.Equal("TestGame.exe", record.GameName);
        Assert.Equal(504, record.UEVersion);
        Assert.True(record.VersionDetected);
        Assert.Equal(1, record.ScanCount);
        Assert.Equal("aob", record.GObjects.Method);
        Assert.Equal("GOBJ_V1", record.GObjects.PatternId);
        Assert.Equal(40, record.GObjects.PatternsTried);
        Assert.Equal(3, record.GObjects.PatternsHit);
        Assert.Equal("string_ref", record.GNames.Method);
        Assert.Equal("not_found", record.GWorld.Method);
    }

    [Fact]
    public async Task RecordScan_IncrementsScanCount()
    {
        var svc = CreateService();
        await svc.RecordScanAsync(MakeState());
        await svc.RecordScanAsync(MakeState());
        await svc.RecordScanAsync(MakeState());

        var file = await svc.LoadFileAsync();
        Assert.Equal(3, file.Games["5F3A1B2CCDD40000"].ScanCount);
    }

    [Fact]
    public async Task RecordScan_MultipleDifferentGames()
    {
        var svc = CreateService();
        await svc.RecordScanAsync(MakeState("AAAA1111BBBB2222", "Game1.exe"));
        await svc.RecordScanAsync(MakeState("CCCC3333DDDD4444", "Game2.exe"));

        var file = await svc.LoadFileAsync();
        Assert.Equal(2, file.Games.Count);
        Assert.Equal("Game1.exe", file.Games["AAAA1111BBBB2222"].GameName);
        Assert.Equal("Game2.exe", file.Games["CCCC3333DDDD4444"].GameName);
    }

    [Fact]
    public async Task RecordScan_SkipsEmptyPeHash()
    {
        var svc = CreateService();
        await svc.RecordScanAsync(MakeState(peHash: ""));

        Assert.False(File.Exists(svc.FilePath));
    }

    [Fact]
    public async Task RecordScan_HandlesCorruptJson()
    {
        var svc = CreateService();

        // Write corrupt JSON to the file
        var dir = Path.GetDirectoryName(svc.FilePath)!;
        Directory.CreateDirectory(dir);
        await File.WriteAllTextAsync(svc.FilePath, "{ corrupt json !!!", TestContext.Current.CancellationToken);

        // Should not throw — recovers by starting fresh
        await svc.RecordScanAsync(MakeState());

        var file = await svc.LoadFileAsync();
        Assert.Single(file.Games);
        Assert.Equal(1, file.Games["5F3A1B2CCDD40000"].ScanCount);
    }

    [Fact]
    public async Task RecordScan_PreservesUserOverrideAcrossRoundTrip()
    {
        // Simulate Flamme (DLL side) writing the override field to the JSON,
        // then AobUsageService doing a routine read-modify-write — the override
        // must NOT be silently dropped, otherwise the user's per-game setting
        // disappears the next time the DLL records a scan.
        var svc = CreateService();
        const string peHash = "5F3A1B2CCDD40000";

        // Step 1: pretend Flamme wrote a record with the override field present.
        Directory.CreateDirectory(Path.GetDirectoryName(svc.FilePath)!);
        var seed = new AobUsageFile
        {
            MachineName = "TEST-MACHINE",
            Games = new()
            {
                [peHash] = new AobUsageRecord
                {
                    PeHash = peHash,
                    GameName = "TestGame.exe",
                    UEVersion = 504,
                    VersionDetected = true,
                    UEVersionUserOverride = 427,
                    UEVersionUserOverrideAt = "2026-05-09T12:00:00Z",
                    InvokeTimeoutMs = 15000,
                    InvokeTimeoutMsAt = "2026-05-10T08:00:00Z",
                    ScanCount = 5,
                },
            },
        };
        await File.WriteAllTextAsync(svc.FilePath,
            JsonSerializer.Serialize(seed, AobUsageJsonContext.Default.AobUsageFile),
            TestContext.Current.CancellationToken);

        // Step 2: a fresh scan happens, AobUsageService updates the record.
        await svc.RecordScanAsync(MakeState(peHash));

        // Step 3: the override field must still be intact.
        var file = await svc.LoadFileAsync();
        var record = file.Games[peHash];
        Assert.Equal(427, record.UEVersionUserOverride);
        Assert.Equal("2026-05-09T12:00:00Z", record.UEVersionUserOverrideAt);
        Assert.Equal(15000, record.InvokeTimeoutMs);
        Assert.Equal("2026-05-10T08:00:00Z", record.InvokeTimeoutMsAt);
        Assert.Equal(6, record.ScanCount);  // sanity: scan increment still happened
    }

    [Fact]
    public async Task RecordScan_PreservesVersionDetectRevAcrossRoundTrip()
    {
        // The DLL stamps versionDetectRev when it saves a detected version. AobUsageService
        // does a routine read-modify-write after every connect — it MUST preserve that stamp,
        // otherwise the next launch sees rev=0, treats the cache as stale, and re-runs the
        // slow UE-version detection (defeating the whole acceleration).
        var svc = CreateService();
        const string peHash = "5F3A1B2CCDD40000";

        Directory.CreateDirectory(Path.GetDirectoryName(svc.FilePath)!);
        var seed = new AobUsageFile
        {
            MachineName = "TEST-MACHINE",
            Games = new()
            {
                [peHash] = new AobUsageRecord
                {
                    PeHash = peHash,
                    GameName = "TestGame.exe",
                    UEVersion = 427,
                    VersionDetected = false,
                    LowConfidence = true,
                    VersionDetectRev = 1,
                    ScanCount = 5,
                },
            },
        };
        await File.WriteAllTextAsync(svc.FilePath,
            JsonSerializer.Serialize(seed, AobUsageJsonContext.Default.AobUsageFile),
            TestContext.Current.CancellationToken);

        await svc.RecordScanAsync(MakeState(peHash));

        var file = await svc.LoadFileAsync();
        var record = file.Games[peHash];
        Assert.Equal(1, record.VersionDetectRev);   // stamp preserved (not clobbered)
        Assert.Equal(6, record.ScanCount);            // sanity: routine update still happened
    }

    [Fact]
    public async Task RecordScan_WritesLowConfidenceFromState()
    {
        var svc = CreateService();
        var state = new EngineState
        {
            PeHash = "AABBCCDD11223344",
            ModuleName = "Stripped.exe",
            UEVersion = 427,
            VersionDetected = false,
            IsLowConfidence = true,
        };

        await svc.RecordScanAsync(state);

        var file = await svc.LoadFileAsync();
        Assert.True(file.Games["AABBCCDD11223344"].LowConfidence);
    }

    [Fact]
    public async Task RecordScan_OverrideDoesNotClobberPriorDetection()
    {
        // Invariant #4 (update branch): when the version came from a user override, the override
        // value must NOT overwrite the last genuine detection cached as ueVersion — otherwise
        // clearing the override later would reuse the override value as a confident auto-detection.
        var svc = CreateService();
        const string peHash = "5F3A1B2CCDD40000";

        Directory.CreateDirectory(Path.GetDirectoryName(svc.FilePath)!);
        var seed = new AobUsageFile
        {
            MachineName = "TEST-MACHINE",
            Games = new()
            {
                [peHash] = new AobUsageRecord
                {
                    PeHash = peHash,
                    GameName = "TestGame.exe",
                    UEVersion = 504,            // the last GENUINE detection
                    VersionDetected = true,
                    LowConfidence = false,
                    VersionDetectRev = 1,
                    ScanCount = 3,
                },
            },
        };
        await File.WriteAllTextAsync(svc.FilePath,
            JsonSerializer.Serialize(seed, AobUsageJsonContext.Default.AobUsageFile),
            TestContext.Current.CancellationToken);

        // A scan reports the value as an ACTIVE user override (e.g. user forced 427).
        var overrideState = new EngineState
        {
            PeHash = peHash,
            ModuleName = "TestGame.exe",
            UEVersion = 427,
            VersionDetected = true,   // the DLL surfaces an override as confident...
            IsUserOverride = true,    // ...but this flag means "don't cache it as a detection"
        };
        await svc.RecordScanAsync(overrideState);

        var record = (await svc.LoadFileAsync()).Games[peHash];
        Assert.Equal(504, record.UEVersion);          // prior real detection untouched (NOT 427)
        Assert.True(record.VersionDetected);
        Assert.False(record.LowConfidence);
        Assert.Equal(1, record.VersionDetectRev);     // DLL stamp preserved
        Assert.Equal(4, record.ScanCount);            // routine update still happened
    }

    [Fact]
    public async Task RecordScan_NewRecordWithOverrideCachesNoDetection()
    {
        // Invariant #4 (new-record branch): a first-ever scan that reports an active override must
        // not seed the cache with the override value as a detection (versionDetectRev stays 0, so
        // the next launch re-detects rather than reusing the override as a confident auto-detection).
        var svc = CreateService();
        var overrideState = new EngineState
        {
            PeHash = "11220000333344445",
            ModuleName = "Fresh.exe",
            UEVersion = 427,
            VersionDetected = true,
            IsUserOverride = true,
        };

        await svc.RecordScanAsync(overrideState);

        var record = (await svc.LoadFileAsync()).Games["11220000333344445"];
        Assert.Equal(0, record.UEVersion);            // override value NOT cached as a detection
        Assert.False(record.VersionDetected);
        Assert.False(record.LowConfidence);
        Assert.Equal(0, record.VersionDetectRev);     // unstamped → next launch re-detects
    }

    // ────────────────────────────────────────────────────────────────
    // Corrupt-file durability (audit #5 AC4 + AC5) and staging hygiene (AC6).
    //
    // This file is not one game's cache: it holds EVERY game's scan record plus the
    // user's per-game UE-version override and invoke timeout. Answering a parse
    // failure with a blank document meant the next save published a one-game file
    // over all of it.
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// THE FINDING. A corrupt file must be moved aside, never overwritten — the bytes
    /// are the only copy of every other game's record.
    ///
    /// NEGATIVE CONTROL: restore `catch (JsonException) { return new AobUsageFile(); }`
    /// and this fails: no quarantine exists and the original bytes are gone for good.
    /// </summary>
    [Fact]
    public async Task RecordScan_CorruptJson_QuarantinesTheOriginalBytes()
    {
        var svc = CreateService();
        var dir = Path.GetDirectoryName(svc.FilePath)!;
        Directory.CreateDirectory(dir);

        // A file that fails to parse but plainly holds other games' data.
        const string corrupt = """{ "games": { "AAAA1111": { "ueVersionUserOverride": 427 """;
        await File.WriteAllTextAsync(svc.FilePath, corrupt, TestContext.Current.CancellationToken);

        await svc.RecordScanAsync(MakeState());

        var quarantined = Directory.GetFiles(dir, Path.GetFileName(svc.FilePath) + ".corrupt-*");
        Assert.Single(quarantined);
        Assert.Equal(corrupt,
            await File.ReadAllTextAsync(quarantined[0], TestContext.Current.CancellationToken));

        // ...and the fresh cache still works.
        var file = await svc.LoadFileAsync();
        Assert.Single(file.Games);
    }

    [Fact]
    public async Task LoadFile_NullDocument_IsTreatedAsCorruptNotAsEmpty()
    {
        // "null" is valid JSON that deserializes to null. The old `?? new AobUsageFile()`
        // wiped on it exactly like a parse failure did, and without even a log line.
        var svc = CreateService();
        Directory.CreateDirectory(Path.GetDirectoryName(svc.FilePath)!);
        await File.WriteAllTextAsync(svc.FilePath, "null", TestContext.Current.CancellationToken);

        var file = await svc.LoadFileAsync();

        Assert.Empty(file.Games);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(svc.FilePath)!,
                                         Path.GetFileName(svc.FilePath) + ".corrupt-*"));
    }

    [Fact]
    public async Task LoadFile_UnreadableFile_ThrowsRatherThanReportingAnEmptyCache()
    {
        // Locked or denied is NOT corrupt. Answering it with a blank document would let
        // the caller's save destroy a perfectly good file.
        var svc = CreateService();
        await svc.RecordScanAsync(MakeState("AAAA0000BBBB1111", "Game1.exe"));

        using (File.Open(svc.FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            await Assert.ThrowsAnyAsync<IOException>(() => svc.LoadFileAsync());

            // RecordScanAsync swallows it (fire-and-forget contract) and writes nothing.
            await svc.RecordScanAsync(MakeState("CCCC2222DDDD3333", "Game2.exe"));
        }

        var file = await svc.LoadFileAsync();
        Assert.Single(file.Games);
        Assert.True(file.Games.ContainsKey("AAAA0000BBBB1111"));
        Assert.Empty(Directory.GetFiles(Path.GetDirectoryName(svc.FilePath)!,
                                        Path.GetFileName(svc.FilePath) + ".corrupt-*"));
    }

    [Fact]
    public async Task LoadFile_QuarantineIsBounded()
    {
        // The app-data root's rule is "app-wide and fixed in number", so the quarantine
        // is capped like the reset backups are.
        var svc = CreateService();
        var dir = Path.GetDirectoryName(svc.FilePath)!;
        var name = Path.GetFileName(svc.FilePath);
        Directory.CreateDirectory(dir);

        for (int d = 1; d <= UE5DumpUI.Helpers.AtomicFileHygiene.MaxCorruptCopies; d++)
            await File.WriteAllTextAsync(Path.Combine(dir, $"{name}.corrupt-2020010{d}-000000000"),
                                         $"old-{d}", TestContext.Current.CancellationToken);

        await File.WriteAllTextAsync(svc.FilePath, "{ not json", TestContext.Current.CancellationToken);
        await svc.LoadFileAsync();

        var quarantined = Directory.GetFiles(dir, name + ".corrupt-*");
        Assert.Equal(UE5DumpUI.Helpers.AtomicFileHygiene.MaxCorruptCopies, quarantined.Length);
        // The OLDEST is the one that went.
        Assert.DoesNotContain(quarantined, p => p.EndsWith("corrupt-20200101-000000000", StringComparison.Ordinal));
    }

    [Fact]
    public async Task LoadFile_QuarantinePruneNeverEatsTheCopyItJustTook()
    {
        // Pruning sorts on the embedded timestamp. If the clock ran backwards — or a
        // pre-existing copy is stamped in the FUTURE — the copy taken this second sorts
        // oldest, and a naive prune would delete the very bytes the mechanism exists to
        // keep. NEGATIVE CONTROL: drop the `justSaved` exclusion and this fails.
        var svc = CreateService();
        var dir = Path.GetDirectoryName(svc.FilePath)!;
        var name = Path.GetFileName(svc.FilePath);
        Directory.CreateDirectory(dir);

        for (int d = 1; d <= UE5DumpUI.Helpers.AtomicFileHygiene.MaxCorruptCopies; d++)
            await File.WriteAllTextAsync(Path.Combine(dir, $"{name}.corrupt-2999010{d}-000000000"),
                                         $"future-{d}", TestContext.Current.CancellationToken);

        const string doomed = "{ the only copy of every game's overrides";
        await File.WriteAllTextAsync(svc.FilePath, doomed, TestContext.Current.CancellationToken);
        await svc.LoadFileAsync();

        var survivors = Directory.GetFiles(dir, name + ".corrupt-*")
                                 .Select(File.ReadAllText)
                                 .ToList();
        Assert.Contains(doomed, survivors);
        Assert.Equal(UE5DumpUI.Helpers.AtomicFileHygiene.MaxCorruptCopies, survivors.Count);
    }

    /// <summary>
    /// AC6. The staging name carries the writer's PID, so every abandoned copy is
    /// distinctly named and nothing ever deleted it — unbounded residue in the app-data
    /// root, one full copy of the cache per affected launch (the DLL writes the same
    /// name from the game process).
    ///
    /// NEGATIVE CONTROL: remove the SweepOrphanTemps() call and the stale file survives.
    /// </summary>
    [Fact]
    public async Task Save_SweepsAbandonedStagingFiles_ButSparesFreshOnesAndBackups()
    {
        var svc = CreateService();
        var dir = Path.GetDirectoryName(svc.FilePath)!;
        var name = Path.GetFileName(svc.FilePath);
        Directory.CreateDirectory(dir);

        var stale  = Path.Combine(dir, $"{name}.tmp.99999");
        var fresh  = Path.Combine(dir, $"{name}.tmp.88888");
        var backup = Path.Combine(dir, $"{name}.001");
        var ct = TestContext.Current.CancellationToken;
        await File.WriteAllTextAsync(stale,  "abandoned", ct);
        await File.WriteAllTextAsync(fresh,  "a game process may be mid-write", ct);
        await File.WriteAllTextAsync(backup, "a reset backup", ct);
        File.SetLastWriteTimeUtc(stale, DateTime.UtcNow - TimeSpan.FromHours(3));

        await svc.RecordScanAsync(MakeState());

        Assert.False(File.Exists(stale));    // age-guarded sweep took it
        Assert.True(File.Exists(fresh));     // could belong to a live writer — spared
        Assert.True(File.Exists(backup));    // scoped to the ".tmp." prefix only
        Assert.False(File.Exists(AobUsageServiceTests.OwnTempPath(svc)));
    }

    private static string OwnTempPath(AobUsageService svc)
        => UE5DumpUI.Helpers.AtomicFileHygiene.TempPathFor(svc.FilePath, Environment.ProcessId);

    [Fact]
    public void FilePath_ContainsMachineName()
    {
        var svc = CreateService();
        Assert.Contains("TEST-MACHINE", svc.FilePath);
        Assert.Contains(Constants.AobUsageFilePrefix, svc.FilePath);
    }

    // --- DeleteGameAsync tests ---

    [Fact]
    public async Task DeleteGame_RemovesExistingEntry()
    {
        var svc = CreateService();
        await svc.RecordScanAsync(MakeState("AAAA0000BBBB1111", "Game1.exe"));
        await svc.RecordScanAsync(MakeState("CCCC2222DDDD3333", "Game2.exe"));

        var result = await svc.DeleteGameAsync("AAAA0000BBBB1111");

        Assert.True(result);
        var file = await svc.LoadFileAsync();
        Assert.Single(file.Games);
        Assert.False(file.Games.ContainsKey("AAAA0000BBBB1111"));
        Assert.True(file.Games.ContainsKey("CCCC2222DDDD3333"));
    }

    [Fact]
    public async Task DeleteGame_ReturnsFalseForMissingHash()
    {
        var svc = CreateService();
        await svc.RecordScanAsync(MakeState());

        var result = await svc.DeleteGameAsync("NONEXISTENT_HASH");

        Assert.False(result);
        // Original entry still intact
        var file = await svc.LoadFileAsync();
        Assert.Single(file.Games);
    }

    [Fact]
    public async Task DeleteGame_ReturnsFalseForEmptyHash()
    {
        var svc = CreateService();
        Assert.False(await svc.DeleteGameAsync(""));
        Assert.False(await svc.DeleteGameAsync(null!));
    }

    [Fact]
    public async Task DeleteGame_WorksWhenNoFileExists()
    {
        var svc = CreateService();
        // File doesn't exist yet — LoadFileAsync returns empty AobUsageFile
        var result = await svc.DeleteGameAsync("AAAA0000BBBB1111");
        Assert.False(result);
    }

    // --- ResetAllAsync tests ---

    [Fact]
    public async Task ResetAll_RenamesFileToBackup001()
    {
        var svc = CreateService();
        await svc.RecordScanAsync(MakeState());
        Assert.True(File.Exists(svc.FilePath));

        var result = await svc.ResetAllAsync();

        Assert.True(result);
        Assert.False(File.Exists(svc.FilePath));
        Assert.True(File.Exists($"{svc.FilePath}.001"));
    }

    [Fact]
    public async Task ResetAll_ReturnsTrueWhenNoFileExists()
    {
        var svc = CreateService();
        Assert.False(File.Exists(svc.FilePath));

        var result = await svc.ResetAllAsync();
        Assert.True(result);
    }

    [Fact]
    public async Task ResetAll_RotatesMultipleBackups()
    {
        var svc = CreateService();

        // First scan + reset → .001
        await svc.RecordScanAsync(MakeState("AAAA", "Game1.exe"));
        await svc.ResetAllAsync();
        Assert.True(File.Exists($"{svc.FilePath}.001"));

        // Second scan + reset → old .001 → .002, new → .001
        await svc.RecordScanAsync(MakeState("BBBB", "Game2.exe"));
        await svc.ResetAllAsync();
        Assert.True(File.Exists($"{svc.FilePath}.001"));
        Assert.True(File.Exists($"{svc.FilePath}.002"));

        // Verify content: .001 should be Game2, .002 should be Game1
        var json1 = await File.ReadAllTextAsync($"{svc.FilePath}.001", TestContext.Current.CancellationToken);
        var json2 = await File.ReadAllTextAsync($"{svc.FilePath}.002", TestContext.Current.CancellationToken);
        Assert.Contains("BBBB", json1);
        Assert.Contains("AAAA", json2);
    }

    [Fact]
    public async Task ResetAll_PurgesOldestAtLimit()
    {
        var svc = CreateService();

        // Create 10 backups manually (.001 through .010)
        var dir = Path.GetDirectoryName(svc.FilePath)!;
        Directory.CreateDirectory(dir);
        for (int i = 1; i <= 10; i++)
            await File.WriteAllTextAsync($"{svc.FilePath}.{i:D3}", $"backup-{i}", TestContext.Current.CancellationToken);

        // Create current file
        await svc.RecordScanAsync(MakeState("NEWEST", "NewGame.exe"));

        // Reset — should delete .010, shift .009→.010 ... .001→.002, current→.001
        var result = await svc.ResetAllAsync();
        Assert.True(result);

        // .001 should be the newest (just-moved current file)
        Assert.True(File.Exists($"{svc.FilePath}.001"));
        var json1 = await File.ReadAllTextAsync($"{svc.FilePath}.001", TestContext.Current.CancellationToken);
        Assert.Contains("NEWEST", json1);

        // .010 should be the old .009 content ("backup-9")
        Assert.True(File.Exists($"{svc.FilePath}.010"));
        var json10 = await File.ReadAllTextAsync($"{svc.FilePath}.010", TestContext.Current.CancellationToken);
        Assert.Equal("backup-9", json10);

        // Original .010 ("backup-10") should be gone — purged
        Assert.DoesNotContain("backup-10", json10);
    }

    [Fact]
    public async Task ResetAll_NewRecordWritesAfterReset()
    {
        var svc = CreateService();
        await svc.RecordScanAsync(MakeState("OLD_HASH", "OldGame.exe"));
        await svc.ResetAllAsync();

        // New scan after reset should create fresh file
        await svc.RecordScanAsync(MakeState("NEW_HASH", "NewGame.exe"));

        Assert.True(File.Exists(svc.FilePath));
        var file = await svc.LoadFileAsync();
        Assert.Single(file.Games);
        Assert.True(file.Games.ContainsKey("NEW_HASH"));
        Assert.False(file.Games.ContainsKey("OLD_HASH"));

        // Backup should still exist
        Assert.True(File.Exists($"{svc.FilePath}.001"));
    }
}
