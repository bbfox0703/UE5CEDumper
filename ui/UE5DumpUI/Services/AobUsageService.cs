using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using UE5DumpUI.Core;
using UE5DumpUI.Helpers;
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
                // Cache the detected version ONLY when it's a genuine detection. An override
                // value (IsUserOverride) must not overwrite the last real detection — otherwise
                // clearing the override later would reuse the override value as a confident
                // detection (the DLL applies the same guard in Flamme::SaveResults; the UI write
                // lands second, so it must mirror it). The override lives in UEVersionUserOverride.
                if (!state.IsUserOverride)
                {
                    existing.UEVersion = state.UEVersion;
                    existing.VersionDetected = state.VersionDetected;
                    existing.LowConfidence = state.IsLowConfidence;
                }
                // NOTE: VersionDetectRev is DLL-authoritative — preserved here (never assigned),
                // exactly like UEVersionUserOverride. Clobbering it would force the next launch
                // to re-run the slow UE-version detection.
                UpdateScanEntry(existing.GObjects, state.GObjectsMethod, state.GObjectsPatternId, state.GObjectsPatternsTried, state.GObjectsPatternsHit);
                UpdateScanEntry(existing.GNames, state.GNamesMethod, state.GNamesPatternId, state.GNamesPatternsTried, state.GNamesPatternsHit);
                UpdateScanEntry(existing.GWorld, state.GWorldMethod, state.GWorldPatternId, state.GWorldPatternsTried, state.GWorldPatternsHit);
                existing.LastScanUtc = DateTime.UtcNow.ToString("o");
                existing.ScanCount++;
            }
            else
            {
                // New game record. Cache the version only for a genuine detection (mirror the
                // update branch + Flamme::SaveResults). VersionDetectRev left 0 so a record the
                // DLL hasn't stamped yet forces a one-time re-detection on the next launch.
                var record = new AobUsageRecord
                {
                    PeHash = state.PeHash,
                    GameName = state.ModuleName,
                    UEVersion = state.IsUserOverride ? 0 : state.UEVersion,
                    VersionDetected = !state.IsUserOverride && state.VersionDetected,
                    LowConfidence = !state.IsUserOverride && state.IsLowConfidence,
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

    /// <summary>
    /// Load the usage file, or create a new one if it doesn't exist.
    ///
    /// THE ONE INVARIANT (audit #5 AC4 + AC5, which are the same defect seen twice): an
    /// empty <see cref="AobUsageFile"/> may only be handed back once the bytes it replaces
    /// are safely somewhere else. This file is not one game's cache — it holds EVERY game's
    /// scan record, its DLL-stamped version detection, and the user's per-game UE-version
    /// override and invoke timeout. The caller's very next act is
    /// <see cref="SaveFileAsync"/>, so returning a blank document while the original still
    /// sits at <see cref="_filePath"/> published a one-game file over all of it — from a
    /// Warn nobody reads, and with none of the care the DELIBERATE reset takes ten numbered
    /// backups to provide. Quarantine first; if the quarantine cannot be taken, FAIL — the
    /// callers all treat a throw as "skip this write", which is exactly right.
    /// </summary>
    internal async Task<AobUsageFile> LoadFileAsync()
    {
        if (!File.Exists(_filePath))
            return new AobUsageFile();

        // An unreadable file (locked, denied) is NOT a corrupt one: it must propagate so
        // the caller skips the save, never be answered with a blank document.
        var json = await File.ReadAllTextAsync(_filePath);

        AobUsageFile? parsed;
        try
        {
            parsed = JsonSerializer.Deserialize(json, s_jsonCtx.AobUsageFile);
        }
        catch (JsonException ex)
        {
            QuarantineCorruptFile(ex.Message);
            return new AobUsageFile();
        }

        // A document of literally `null` parses fine and deserializes to null. The old
        // `?? new AobUsageFile()` treated that as "empty cache" and wiped the same way a
        // parse failure did, just without even a log line.
        if (parsed == null)
        {
            QuarantineCorruptFile("document deserialized to null");
            return new AobUsageFile();
        }

        return parsed;
    }

    /// <summary>
    /// Move the corrupt cache aside under a timestamped name and say so loudly. Throws if
    /// the move fails — see the invariant on <see cref="LoadFileAsync"/>; a caller that
    /// received an empty file when the original was still in place would destroy it.
    /// </summary>
    private void QuarantineCorruptFile(string reason)
    {
        var dir  = Path.GetDirectoryName(_filePath)!;
        var name = Path.GetFileName(_filePath);
        var quarantine = Path.Combine(dir, AtomicFileHygiene.QuarantineNameFor(name, DateTime.UtcNow));

        File.Move(_filePath, quarantine);   // deliberately unguarded

        _log.Error(Constants.LogCatInit,
            $"AobUsageService: cache file is corrupt ({reason}). It holds every game's scan record, " +
            $"UE-version override and invoke timeout, so it was moved aside instead of overwritten: " +
            $"{quarantine}. To recover, repair the JSON and rename it back to '{name}'. " +
            $"A fresh cache starts from the next scan.");

        PruneCorruptCopies(dir, name, justSaved: Path.GetFileName(quarantine));
    }

    /// <summary>Keep the quarantine bounded. Best-effort: the data is already safe by the
    /// time this runs, so a failure here must not fail the load.
    ///
    /// <paramref name="justSaved"/> is excluded unconditionally. Pruning sorts on the
    /// embedded timestamp, so a clock that ran backwards — or one pre-existing copy stamped
    /// in the future — would otherwise rank the copy we took THIS SECOND as the oldest and
    /// delete the very bytes this whole mechanism exists to keep.</summary>
    private void PruneCorruptCopies(string dir, string fileName, string justSaved)
    {
        try
        {
            var names = Directory
                .EnumerateFiles(dir, AtomicFileHygiene.CorruptPrefixFor(fileName) + "*")
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrEmpty(n))
                .Select(n => n!)
                .Where(n => !string.Equals(n, justSaved, StringComparison.OrdinalIgnoreCase));

            // -1 because `justSaved` is excluded above but still counts toward the cap.
            foreach (var stale in AtomicFileHygiene.SelectCorruptCopiesToPrune(
                         names, fileName, AtomicFileHygiene.MaxCorruptCopies - 1))
            {
                File.Delete(Path.Combine(dir, stale));
            }
        }
        catch (Exception ex)
        {
            _log.Warn(Constants.LogCatInit,
                $"AobUsageService: could not prune old quarantined caches: {ex.Message}");
        }
    }

    /// <summary>Save the usage file atomically (write to temp, then rename).</summary>
    private async Task SaveFileAsync(AobUsageFile file)
    {
        // PER-PROCESS temp name. The DLL's Flamme::SaveResults writes the SAME cache file
        // from the game process, and both sides used to stage through the byte-identical
        // "<file>.tmp" with truncate — so one could truncate the staging file while the
        // other was mid-write, and whichever renamed last published a half-written
        // document over the real cache. The in-process semaphore around this method
        // cannot see the other process. The final rename stays last-writer-wins (the
        // existing, accepted semantics); only the staging file must not be shared. Kept
        // byte-compatible with the DLL's MakeTempPath so the two are obviously a pair. (B39)
        var tempPath = AtomicFileHygiene.TempPathFor(_filePath, Environment.ProcessId);
        var json = JsonSerializer.Serialize(file, s_jsonCtx.AobUsageFile);
        try
        {
            await File.WriteAllTextAsync(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        finally
        {
            // AC6: on success the rename consumed it; on ANY failure the PID suffix makes
            // this a distinctly named full copy of the cache that nothing would ever
            // delete. The DLL's twin (Flamme FL2) does the same at the same point.
            try { if (File.Exists(tempPath)) File.Delete(tempPath); }
            catch { /* best-effort — the sweep below catches it on a later launch */ }
        }

        SweepOrphanTemps();
    }

    /// <summary>
    /// Delete staging files abandoned by earlier failures — ours AND the DLL's, which
    /// builds the same <c>&lt;file&gt;.tmp.&lt;pid&gt;</c> name. AGE-guarded, not
    /// PID-guarded: writing this file takes milliseconds, so anything an hour old is
    /// provably abandoned, whereas a liveness test would race a game process that is
    /// mid-write right now.
    ///
    /// Scoped to this cache file's own prefix, never a folder wildcard: the app-data root
    /// also holds the reset backups (<c>.001</c>-<c>.010</c>) and the quarantine, and the
    /// per-game families that must move and expire as a GROUP live in <c>Snapshots\</c> /
    /// <c>Bookmarks\</c>, which this never enters.
    ///
    /// Once per instance. A plain bool, not an Interlocked latch: every call sits inside
    /// <see cref="_lock"/>. Instance-scoped rather than static so a test can exercise it —
    /// the app composes exactly one of these.
    /// </summary>
    private bool _tempsSwept;

    private void SweepOrphanTemps()
    {
        if (_tempsSwept) return;
        _tempsSwept = true;

        try
        {
            var dir  = Path.GetDirectoryName(_filePath)!;
            var name = Path.GetFileName(_filePath);
            var now  = DateTime.UtcNow;
            int removed = 0;

            foreach (var full in Directory.EnumerateFiles(dir, AtomicFileHygiene.TempPrefixFor(name) + "*"))
            {
                DateTime mtime;
                try { mtime = File.GetLastWriteTimeUtc(full); }
                catch { continue; }

                if (!AtomicFileHygiene.IsStaleTemp(name, Path.GetFileName(full), mtime, now,
                                                   AtomicFileHygiene.StaleTempAge))
                    continue;

                try { File.Delete(full); removed++; }
                catch { /* someone else's, or in use — leave it */ }
            }

            if (removed > 0)
                _log.Info(Constants.LogCatInit,
                    $"AobUsageService: removed {removed} abandoned staging file(s) older than " +
                    $"{AtomicFileHygiene.StaleTempAge.TotalHours:0} h");
        }
        catch (Exception ex)
        {
            _log.Warn(Constants.LogCatInit, $"AobUsageService: staging-file sweep failed: {ex.Message}");
        }
    }

    private static void UpdateScanEntry(AobScanEntry entry, string method, string patternId, int tried, int hit)
    {
        entry.Method = method;
        entry.PatternId = patternId;
        entry.PatternsTried = tried;
        entry.PatternsHit = hit;
    }

    /// <summary>
    /// Delete a single game's cache entry by PE hash.
    /// Returns true if the entry was found and removed, false otherwise.
    /// </summary>
    public async Task<bool> DeleteGameAsync(string peHash)
    {
        if (string.IsNullOrEmpty(peHash)) return false;

        await _lock.WaitAsync();
        try
        {
            var file = await LoadFileAsync();
            if (!file.Games.Remove(peHash))
            {
                _log.Debug(Constants.LogCatInit, $"AobUsageService: PE={peHash} not in cache — nothing to delete");
                return false;
            }

            await SaveFileAsync(file);
            _log.Info(Constants.LogCatInit, $"AobUsageService: Deleted cache for PE={peHash}");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatInit, "AobUsageService: Failed to delete game cache", ex);
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>
    /// Reset all cache data by renaming the JSON file with numbered backup extensions
    /// (.001 through .010). Uses queue-purge: oldest backup is discarded when limit is reached.
    /// Returns true if the reset succeeded (or file didn't exist), false on error.
    /// </summary>
    public async Task<bool> ResetAllAsync()
    {
        await _lock.WaitAsync();
        try
        {
            if (!File.Exists(_filePath))
            {
                _log.Debug(Constants.LogCatInit, "AobUsageService: No cache file to reset");
                return true;
            }

            // Rotate backups: .010 is deleted, .009 → .010, ... .001 → .002, current → .001
            const int maxBackups = 10;

            // Delete the oldest backup if it exists
            var oldest = $"{_filePath}.{maxBackups:D3}";
            if (File.Exists(oldest))
                File.Delete(oldest);

            // Shift existing backups up by one
            for (int i = maxBackups - 1; i >= 1; i--)
            {
                var src = $"{_filePath}.{i:D3}";
                var dst = $"{_filePath}.{(i + 1):D3}";
                if (File.Exists(src))
                    File.Move(src, dst);
            }

            // Move current file to .001
            File.Move(_filePath, $"{_filePath}.001");

            _log.Info(Constants.LogCatInit, $"AobUsageService: Cache reset — backed up to {_filePath}.001");
            return true;
        }
        catch (Exception ex)
        {
            _log.Error(Constants.LogCatInit, "AobUsageService: Failed to reset cache", ex);
            return false;
        }
        finally
        {
            _lock.Release();
        }
    }

    /// <summary>File path for testing/diagnostics.</summary>
    public string FilePath => _filePath;
}
