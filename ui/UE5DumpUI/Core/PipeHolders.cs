using System.Text.Json;

namespace UE5DumpUI.Core;

/// <summary>One holder of a game's pipe connections, as the DLL logged it.</summary>
public sealed record PipeHolder(int Pid, string Exe, string LastCmd, bool InFlight);

/// <summary>The holders of the DLL's latest at-capacity line for one pipe: when it was logged, who, how many did not
/// fit the line, and whether a later "slot freed" line has superseded it.</summary>
public sealed record PipeHoldersSeen(string Time, IReadOnlyList<PipeHolder> Holders, int More, bool Superseded);

/// <summary>
/// [PIPE-PER-PROCESS-2026-11-06] Stage C. When every connection of a game's pipe is taken, no client can connect to
/// ask who holds them, so the DLL writes the holders on its at-capacity line in its pipe log
/// (Grimoire.h PIPE_AT_CAPACITY_TEXT / PIPE_HOLDERS_TOKEN). This reads that line. Pure: the caller reads the file.
/// </summary>
public static class PipeHolders
{
    /// <summary>The latest at-capacity line naming <paramref name="pipeName"/> (bare, e.g. UE5DumpBfx_668) in a DLL
    /// pipe log, or null when there is none or the latest one cannot be read. Two processes of one exe share a log
    /// folder, so the line is matched by the pipe's name, digit-exact.</summary>
    public static PipeHoldersSeen? ParseLatest(string? logText, string pipeName)
    {
        if (string.IsNullOrEmpty(logText)) return null;
        string pipeRef = @"\\.\pipe\" + pipeName;
        PipeHoldersSeen? latest = null;
        bool sawFull = false;
        foreach (var raw in logText.Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            if (!NamesPipe(line, pipeRef)) continue;
            if (line.Contains(Constants.PipeSlotFreedText, StringComparison.Ordinal))
            {
                if (latest is not null) latest = latest with { Superseded = true };
                continue;
            }
            if (!line.Contains(Constants.PipeAtCapacityText, StringComparison.Ordinal)) continue;
            sawFull = true;
            latest = ParseLine(line);   // a newer line that cannot be read is no answer, not the older one's
        }
        return sawFull ? latest : null;
    }

    private static bool NamesPipe(string line, string pipeRef)
    {
        for (int at = line.IndexOf(pipeRef, StringComparison.OrdinalIgnoreCase); at >= 0;
             at = line.IndexOf(pipeRef, at + 1, StringComparison.OrdinalIgnoreCase))
        {
            int after = at + pipeRef.Length;
            if (after >= line.Length || !char.IsAsciiDigit(line[after])) return true;
        }
        return false;
    }

    private static PipeHoldersSeen? ParseLine(string line)
    {
        int open = line.IndexOf('['), close = line.IndexOf(']');
        string time = open == 0 && close > open ? line[(open + 1)..close] : "";
        int tok = line.IndexOf(Constants.PipeHoldersToken, StringComparison.Ordinal);
        if (tok < 0) return null;
        string rest = line[(tok + Constants.PipeHoldersToken.Length)..];
        int more = 0;
        int moreAt = rest.IndexOf(Constants.PipeHoldersMoreToken, StringComparison.Ordinal);
        if (moreAt >= 0)
        {
            int.TryParse(rest[(moreAt + Constants.PipeHoldersMoreToken.Length)..].Trim(), out more);
            rest = rest[..moreAt];
        }
        try
        {
            using var doc = JsonDocument.Parse(rest);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            var holders = new List<PipeHolder>();
            foreach (var e in doc.RootElement.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                holders.Add(new PipeHolder(
                    e.TryGetProperty("pid", out var pid) && pid.TryGetInt32(out int p) ? p : 0,
                    e.TryGetProperty("exe", out var exe) && exe.ValueKind == JsonValueKind.String ? exe.GetString() ?? "?" : "?",
                    e.TryGetProperty("last_cmd", out var cmd) && cmd.ValueKind == JsonValueKind.String ? cmd.GetString() ?? "" : "",
                    e.TryGetProperty("in_flight", out var f) && f.ValueKind == JsonValueKind.True));
            }
            return new PipeHoldersSeen(time, holders, more, false);
        }
        catch (JsonException)
        {
            return null;   // cut or garbled: no answer
        }
    }
}
