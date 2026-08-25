using UE5DumpUI.Services;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Pure composition of the "Dump All" completion status line (audit X4).
///
/// Two defects it exists to prevent:
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
        return result.Errors > 0
            ? $"Dumped {result.ClassesEmitted:N0} classes ({size}, {result.Errors} errors) to {fileName}"
            : $"Dumped {result.ClassesEmitted:N0} classes ({size}) to {fileName}";
    }
}
