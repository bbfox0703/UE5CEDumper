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
    public Task CopyToClipboardAsync(string text) => Task.CompletedTask;
    public string GetMachineName() => "TEST-MACHINE";
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

        var json = await File.ReadAllTextAsync(svc.FilePath);
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
        await File.WriteAllTextAsync(svc.FilePath, "{ corrupt json !!!");

        // Should not throw — recovers by starting fresh
        await svc.RecordScanAsync(MakeState());

        var file = await svc.LoadFileAsync();
        Assert.Single(file.Games);
        Assert.Equal(1, file.Games["5F3A1B2CCDD40000"].ScanCount);
    }

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
        var json1 = await File.ReadAllTextAsync($"{svc.FilePath}.001");
        var json2 = await File.ReadAllTextAsync($"{svc.FilePath}.002");
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
            await File.WriteAllTextAsync($"{svc.FilePath}.{i:D3}", $"backup-{i}");

        // Create current file
        await svc.RecordScanAsync(MakeState("NEWEST", "NewGame.exe"));

        // Reset — should delete .010, shift .009→.010 ... .001→.002, current→.001
        var result = await svc.ResetAllAsync();
        Assert.True(result);

        // .001 should be the newest (just-moved current file)
        Assert.True(File.Exists($"{svc.FilePath}.001"));
        var json1 = await File.ReadAllTextAsync($"{svc.FilePath}.001");
        Assert.Contains("NEWEST", json1);

        // .010 should be the old .009 content ("backup-9")
        Assert.True(File.Exists($"{svc.FilePath}.010"));
        var json10 = await File.ReadAllTextAsync($"{svc.FilePath}.010");
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
