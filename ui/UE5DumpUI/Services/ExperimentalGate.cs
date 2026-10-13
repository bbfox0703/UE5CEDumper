using System;
using System.IO;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Persists the experimental-features opt-in to a tiny JSON file:
/// %LOCALAPPDATA%\UE5CEDumper\experimental.json. Modelled on
/// <see cref="AobUsageService"/>'s persistence pattern (source-generated
/// JSON, atomic temp-then-rename writes, swallow-and-log on failure).
/// </summary>
public sealed class ExperimentalGate : IExperimentalGate
{
    private readonly string _filePath;
    private readonly ILoggingService? _log;
    private bool _enabled;
    private bool _locked;
    private int _quotaMb = 1024;

    // Source-generated JSON context (reflection-based JSON is disabled in trimmed/AOT builds)
    private static readonly ExperimentalSettingsJsonContext s_jsonCtx = ExperimentalSettingsJsonContext.Default;

    public event EventHandler? Changed;

    public ExperimentalGate(IPlatformService platform, ILoggingService? log = null)
    {
        _log = log;
        var dir = Path.Combine(platform.GetAppDataPath(), Constants.LogFolderName);
        Directory.CreateDirectory(dir);
        _filePath = Path.Combine(dir, Constants.ExperimentalSettingsFile);
        Load();
    }

    public bool IsEnabled
    {
        get => _enabled;
        set
        {
            // Once locked the opt-in is irreversible — refuse to turn it off
            // (the UI also disables the checkbox, this is defence in depth).
            if (_locked && !value) return;
            if (_enabled == value) return;
            _enabled = value;
            Save();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    public bool IsLocked => _locked;

    public void Lock()
    {
        // Session-only: the lock is NOT persisted. After a restart the user can
        // untick the opt-in again until they re-open an experimental tab.
        if (_locked) return;
        _locked = true;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    public bool TeardownOwed { get; set; }

    public int SnapshotQuotaMb
    {
        get => _quotaMb;
        set
        {
            if (_quotaMb == value) return;
            _quotaMb = value < 0 ? 0 : value;
            Save();  // no Changed — tab-visibility doesn't depend on the quota
        }
    }

    private void Load()
    {
        try
        {
            if (!File.Exists(_filePath)) return;
            var json = File.ReadAllText(_filePath);
            var settings = JsonSerializer.Deserialize(json, s_jsonCtx.ExperimentalSettings);
            if (settings != null)
            {
                _enabled = settings.Enabled;
                _quotaMb = settings.SnapshotQuotaMb < 0 ? 0 : settings.SnapshotQuotaMb;
            }
        }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatInit, $"ExperimentalGate: failed to load, using defaults: {ex.Message}");
        }
    }

    private void Save()
    {
        try
        {
            var json = JsonSerializer.Serialize(
                new ExperimentalSettings { Enabled = _enabled, SnapshotQuotaMb = _quotaMb },
                s_jsonCtx.ExperimentalSettings);
            var tempPath = _filePath + ".tmp";
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _filePath, overwrite: true);
        }
        catch (Exception ex)
        {
            _log?.Error(Constants.LogCatInit, "ExperimentalGate: failed to save", ex);
        }
    }

    /// <summary>File path for testing/diagnostics.</summary>
    public string FilePath => _filePath;
}
