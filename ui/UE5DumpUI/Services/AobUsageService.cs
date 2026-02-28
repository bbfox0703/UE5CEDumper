using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Persists AOB scan results to a per-machine JSON file for usage analysis.
/// File location: %LOCALAPPDATA%\UE5CEDumper\UE5CEDumper.{MachineName}.json
/// </summary>
public sealed class AobUsageService
{
    private readonly string _filePath;
    private readonly string _machineName;
    private readonly ILoggingService _log;
    private readonly SemaphoreSlim _lock = new(1, 1);

    // Source-generated JSON context (reflection-based JSON is disabled in trimmed/AOT builds)
    private static readonly AobUsageJsonContext s_jsonCtx = AobUsageJsonContext.Default;

    public AobUsageService(IPlatformService platform, ILoggingService log)
    {
        _log = log;
        var appData = platform.GetAppDataPath();
        var dir = Path.Combine(appData, Constants.LogFolderName);
        Directory.CreateDirectory(dir);
        _machineName = platform.GetMachineName();
        _filePath = Path.Combine(dir, $"{Constants.AobUsageFilePrefix}.{_machineName}.json");
    }

    /// <summary>
    /// Record a scan result for the current game. Creates or updates the JSON file.
    /// This is fire-and-forget safe — errors are logged but never thrown.
    /// </summary>
    public async Task RecordScanAsync(EngineState state)
    {
        if (string.IsNullOrEmpty(state.PeHash))
        {
            _log.Debug(Constants.LogCatInit, "AobUsageService: No PE hash — skipping record");
            return;
        }

        await _lock.WaitAsync();
        try
        {
            var file = await LoadFileAsync();

            if (file.Games.TryGetValue(state.PeHash, out var existing))
            {
                // Update existing record
                existing.GameName = state.ModuleName;
                existing.UEVersion = state.UEVersion;
                existing.VersionDetected = state.VersionDetected;
                UpdateScanEntry(existing.GObjects, state.GObjectsMethod, state.GObjectsPatternId, state.GObjectsPatternsTried, state.GObjectsPatternsHit);
                UpdateScanEntry(existing.GNames, state.GNamesMethod, state.GNamesPatternId, state.GNamesPatternsTried, state.GNamesPatternsHit);
                UpdateScanEntry(existing.GWorld, state.GWorldMethod, state.GWorldPatternId, state.GWorldPatternsTried, state.GWorldPatternsHit);
                existing.LastScanUtc = DateTime.UtcNow.ToString("o");
                existing.ScanCount++;
            }
            else
            {
                // New game record
                var record = new AobUsageRecord
                {
                    PeHash = state.PeHash,
                    GameName = state.ModuleName,
                    UEVersion = state.UEVersion,
                    VersionDetected = state.VersionDetected,
                    LastScanUtc = DateTime.UtcNow.ToString("o"),
                    ScanCount = 1,
                };
                UpdateScanEntry(record.GObjects, state.GObjectsMethod, state.GObjectsPatternId, state.GObjectsPatternsTried, state.GObjectsPatternsHit);
                UpdateScanEntry(record.GNames, state.GNamesMethod, state.GNamesPatternId, state.GNamesPatternsTried, state.GNamesPatternsHit);
                UpdateScanEntry(record.GWorld, state.GWorldMethod, state.GWorldPatternId, state.GWorldPatternsTried, state.GWorldPatternsHit);
                file.Games[state.PeHash] = record;
            }

            file.MachineName = _machineName;
            await SaveFileAsync(file);
            _log.Info(Constants.LogCatInit, $"AobUsageService: Recorded scan for {state.ModuleName} (PE={state.PeHash}, count={file.Games[state.PeHash].ScanCount})");
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatInit, "AobUsageService: Failed to record scan", ex);
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>Load the usage file, or create a new one if it doesn't exist.</summary>
    internal async Task<AobUsageFile> LoadFileAsync()
    {
        if (!File.Exists(_filePath))
            return new AobUsageFile();

        try
        {
            var json = await File.ReadAllTextAsync(_filePath);
            return JsonSerializer.Deserialize(json, s_jsonCtx.AobUsageFile)
                   ?? new AobUsageFile();
        }
        catch (JsonException ex)
        {
            _log.Warn(Constants.LogCatInit, $"AobUsageService: Corrupt JSON, starting fresh: {ex.Message}");
            return new AobUsageFile();
        }
    }

    /// <summary>Save the usage file atomically (write to temp, then rename).</summary>
    private async Task SaveFileAsync(AobUsageFile file)
    {
        var tempPath = _filePath + ".tmp";
        var json = JsonSerializer.Serialize(file, s_jsonCtx.AobUsageFile);
        await File.WriteAllTextAsync(tempPath, json);
        File.Move(tempPath, _filePath, overwrite: true);
    }

    private static void UpdateScanEntry(AobScanEntry entry, string method, string patternId, int tried, int hit)
    {
        entry.Method = method;
        entry.PatternId = patternId;
        entry.PatternsTried = tried;
        entry.PatternsHit = hit;
    }

    /// <summary>File path for testing/diagnostics.</summary>
    public string FilePath => _filePath;
}
