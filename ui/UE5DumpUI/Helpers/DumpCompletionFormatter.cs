using UE5DumpUI.Services;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Pure composition of the "Dump All" completion status line (audit X4).
///
/// Defects it exists to prevent:
/// <list type="bullet">
///   <item>The old line was derived from the output file's byte length, so a
///     zero-class or all-errored dump still read as a successful export. The
///     honest signal is <see cref="DumpResult.ClassesEmitted"/> — what the dump
///     actually wrote.</item>
///   <item>The size was <c>length / 1024 / 1024</c> — <b>integer</b> division on a
///     <see cref="long"/>, so a 3.7 MB dump printed "3.0 MB" and anything under
///     1 MB printed "0.0 MB". <see cref="FormatSize"/> divides in
///     <see cref="double"/> and steps down to KB / bytes so a small dump is not
///     rounded to "0.0 MB".</item>
///   <item>A dump whose enums are missing, partial or nameless ending on a bare
///     success line: only the file's summary line recorded it, and no reader
///     shows that line.</item>
/// </list>
/// </summary>
internal static class DumpCompletionFormatter
{
    /// <summary>Human-readable byte size that never rounds a real file to
    /// "0.0 MB": MB at/above 1 MiB, KB at/above 1 KiB, otherwise bytes. The
    /// division is done in <see cref="double"/> (long ÷ double promotes) so the
    /// <c>:F1</c> is a real fraction, not an integer with a ".0" suffix.</summary>
    internal static string FormatSize(long bytes)
    {
        if (bytes < 0) bytes = 0;
        if (bytes >= 1024L * 1024L)
            return $"{bytes / (1024.0 * 1024.0):F1} MB";
        if (bytes >= 1024L)
            return $"{bytes / 1024.0:F1} KB";
        return $"{bytes} B";
    }

    /// <summary>[EXTPR-539-540-2026-10-02] D4.2: an estimate's time, as the confirmation shows it — rounded,
    /// because it is scaled from one page. Each form carries its own "about", so the sentence around it reads
    /// for "less than a second" too.</summary>
    internal static string FormatDuration(TimeSpan t)
    {
        if (t < TimeSpan.FromSeconds(1)) return Core.Res.Get("str.Duration.UnderSecond");
        if (t < TimeSpan.FromMinutes(2)) return Core.Res.Format("str.Duration.Seconds", Math.Round(t.TotalSeconds));
        return Core.Res.Format("str.Duration.Minutes", Math.Round(t.TotalMinutes));
    }

    /// <summary>
    /// Compose the completion status line from the dump's actual counters.
    /// <paramref name="byteLength"/> is used only to state the file's size, never
    /// to decide whether the dump succeeded.
    /// </summary>
    internal static string Format(DumpResult result, long byteLength, string fileName)
    {
        if (result.ClassesEmitted <= 0)
        {
            // No class lines were written — the file holds only the meta + summary
            // envelope and is not usable for analysis. Say so instead of claiming
            // an export.
            return result.Errors > 0
                ? $"Dump wrote no classes ({result.Errors} errors) — nothing usable in {fileName}"
                : $"Dump wrote no classes — is the game scanned? Nothing usable in {fileName}";
        }

        string size = FormatSize(byteLength);
        var parts = new List<string> { $"{result.ClassesEmitted:N0} classes" };
        if (result.StructsEmitted > 0) parts.Add($"{result.StructsEmitted:N0} structs");
        if (result.EnumsEmitted > 0) parts.Add($"{result.EnumsEmitted:N0} enums");
        string types = parts.Count == 1
            ? parts[0]
            : string.Join(", ", parts.Take(parts.Count - 1)) + " and " + parts[^1];
        // [EXTPR-539-540-2026-10-02] As for USMAP [P1-ENUMNAMES]. The worst condition only, to keep the status
        // one line; the summary line records each.
        string enumNote = !result.EnumsListed
            ? " — ⚠ the enum list could not be read (see the file's list_enums error line)"
            : result.EnumNamesFailed
                ? " — ⚠ enum member names are unavailable on this build, so every enum's entries are empty"
                : result.EnumsTruncated
                    ? " — ⚠ the enum list was cut short; re-export for a complete file"
                    : "";
        return result.Errors > 0
            ? $"Dumped {types} ({size}, {result.Errors} errors) to {fileName}{enumNote}"
            : $"Dumped {types} ({size}) to {fileName}{enumNote}";
    }
}
