using System.IO;
using System.Text.Json;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// [MCP-WRITE-GRANTS-2026-11-14] §4.10. The "Block AI writes" file, as an MCP server reads it. The server only ever
/// reads: the switch can narrow what an AI may do and never widen it, so a forged or edited file can at most block or
/// revoke.
/// </summary>
public sealed class AiWriteBlockGate : IAiWriteBlockGate
{
    // RED: the instance surface only.
    public AiWriteBlockGate(IPlatformService platform, ILoggingService? log = null) { }
    public bool IsBlocked => false;
    public long RevokeStamp => 0;
    public bool TrySetBlocked(bool blocked) => false;
    public void Reload() { }
    public event EventHandler? Changed { add { } remove { } }

    private static readonly AiWriteBlockJsonContext s_json = AiWriteBlockJsonContext.Default;

    /// <summary>Where the window keeps the switch: beside experimental.json, one file per Windows user.</summary>
    public static string PathFor(IPlatformService platform)
        => Path.Combine(platform.GetAppDataPath(), Constants.LogFolderName, Constants.AiWriteBlockFile);

    /// <summary>The switch now. No file is the switch never touched: not blocked, stamp 0. A file that cannot be read or
    /// parsed is <see cref="AiWriteBlockState.Unreadable"/>, which blocks: a stop control that failed open would stop
    /// nothing. The file is opened sharing read, write and delete, so a server reading it never makes the window's
    /// replace-by-rename fail.</summary>
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
