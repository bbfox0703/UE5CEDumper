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
}
