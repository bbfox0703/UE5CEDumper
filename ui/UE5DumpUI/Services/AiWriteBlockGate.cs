using System.IO;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] §4.10. The "Block AI writes" file: the window's instance writes it, and an MCP server
/// only ever reads it through <see cref="ReadState"/>. The switch can narrow what an AI may do and never widen it, so a
/// forged or edited file can at most block or revoke.
///
/// Unlike ExperimentalGate's save, a failed save is reported, never only logged: a stop control that looks on while no
/// server sees it stops nothing. The write is temp-then-rename, retried a few times since a server may be reading the
/// file at that moment, and then in place, which a server's read allows.
/// </summary>
public sealed class AiWriteBlockGate : IAiWriteBlockGate
{
    private static readonly AiWriteBlockJsonContext s_json = AiWriteBlockJsonContext.Default;
    private const int SaveAttempts = 4;
    private const int SaveRetryDelayMs = 40;

    private readonly string _filePath;
    private readonly ILoggingService? _log;
    private bool _blocked;
    private long _stamp;

    public event EventHandler? Changed;

    public AiWriteBlockGate(IPlatformService platform, ILoggingService? log = null)
    {
        _log = log;
        _filePath = PathFor(platform);
        var state = ReadState(_filePath);
        if (!state.Unreadable)
        {
            _blocked = state.Blocked;
            _stamp = state.RevokeStamp;
        }
        else
        {
            // Shown ticked, as the servers treat it: the user sees a block they can lift, never a lie the other way.
            _blocked = true;
        }
    }

    public bool IsBlocked => _blocked;
    public long RevokeStamp => _stamp;

    public bool TrySetBlocked(bool blocked)
    {
        // A turn-on revokes: the stamp goes past both what this window wrote and what the file holds now (another
        // window may have written it), so every server sees a change.
        long stamp = _stamp;
        if (blocked)
        {
            var onDisk = ReadState(_filePath);
            stamp = Math.Max(stamp, onDisk.Unreadable ? 0 : onDisk.RevokeStamp) + 1;
        }
        if (!Save(blocked, stamp)) return false;
        bool changed = _blocked != blocked || _stamp != stamp;
        _blocked = blocked;
        _stamp = stamp;
        _log?.Info(Constants.LogCatInit, $"Block AI writes: {(blocked ? "on" : "off")} (revoke stamp {stamp}).");
        if (changed) Changed?.Invoke(this, EventArgs.Empty);
        return true;
    }

    public void Reload()
    {
        var state = ReadState(_filePath);
        bool blocked = state.Unreadable || state.Blocked;
        long stamp = state.Unreadable ? _stamp : state.RevokeStamp;
        if (blocked == _blocked && stamp == _stamp) return;
        _blocked = blocked;
        _stamp = stamp;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private bool Save(bool blocked, long stamp)
    {
        string json = JsonSerializer.Serialize(new AiWriteBlockSettings { Blocked = blocked, RevokeStamp = stamp },
            s_json.AiWriteBlockSettings);
        for (int attempt = 1; attempt <= SaveAttempts; attempt++)
        {
            string temp = _filePath + ".tmp";
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
                File.WriteAllText(temp, json);
                File.Move(temp, _filePath, overwrite: true);
                return true;
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                try { File.Delete(temp); } catch (Exception) { /* the next attempt rewrites it */ }
                if (attempt < SaveAttempts)
                {
                    Thread.Sleep(SaveRetryDelayMs);
                    continue;
                }
                // Measured in AiWriteBlockGateTests: a replace-by-rename fails while any reader holds the file, even one
                // sharing delete. Written in place instead, the way a server's read shares: a server that reads it half
                // written finds it unreadable and blocks for that call, which errs the safe way.
                if (WriteInPlace(json)) return true;
                _log?.Error(Constants.LogCatInit, "Block AI writes: could not save", e);
                return false;
            }
        }
        return false;
    }

    private bool WriteInPlace(string json)
    {
        try
        {
            using var stream = new FileStream(_filePath, FileMode.OpenOrCreate, FileAccess.Write,
                FileShare.ReadWrite | FileShare.Delete);
            byte[] bytes = new System.Text.UTF8Encoding(false).GetBytes(json);
            stream.SetLength(0);
            stream.Write(bytes, 0, bytes.Length);
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    /// <summary>Where the window keeps the switch: beside experimental.json, one file per Windows user.</summary>
    public static string PathFor(IPlatformService platform)
        => Path.Combine(platform.GetAppDataPath(), Constants.LogFolderName, Constants.AiWriteBlockFile);

    /// <summary>The switch now. No file is the switch never touched: not blocked, stamp 0. A file that cannot be read or
    /// parsed is <see cref="AiWriteBlockState.Unreadable"/>, which blocks: a stop control that failed open would stop
    /// nothing. The file is opened sharing read, write and delete, so the window's save can still go through while a
    /// server reads it (in place, when its rename cannot).</summary>
    public static AiWriteBlockState ReadState(string path)
    {
        try
        {
            if (!File.Exists(path)) return new AiWriteBlockState(false, 0, false);
            string json;
            using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                       FileShare.ReadWrite | FileShare.Delete))
            using (var reader = new StreamReader(stream))
                json = reader.ReadToEnd();
            var settings = JsonSerializer.Deserialize(json, s_json.AiWriteBlockSettings);
            return settings is null
                ? new AiWriteBlockState(false, 0, true)
                : new AiWriteBlockState(settings.Blocked, settings.RevokeStamp, false);
        }
        catch (FileNotFoundException)
        {
            return new AiWriteBlockState(false, 0, false);   // deleted between the check and the open
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException)
        {
            return new AiWriteBlockState(false, 0, true);
        }
    }
}
