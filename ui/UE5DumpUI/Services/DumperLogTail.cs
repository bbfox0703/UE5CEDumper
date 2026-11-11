using System.IO;
using System.Text;

namespace UE5DumpUI.Services;

/// <summary>
/// The end of a game's DLL log, read while the DLL keeps writing it. [PIPE-PER-PROCESS-2026-11-06] Stage C: Connect reads
/// the pipe log's at-capacity line (who holds the connections) when it cannot connect to ask.
/// </summary>
public static class DumperLogTail
{
    /// <summary>The DLL's <paramref name="category"/>-0.log for a game whose exe is <paramref name="imageName"/>, under
    /// <paramref name="logsRoot"/> (the Logs folder); null when the game or the file cannot be read.</summary>
    public static string? Read(string logsRoot, string imageName, string category, int maxBytes = 256 * 1024)
    {
        string folder = ProxyImportAnalyzer.ProcessLogFolderName(imageName);
        if (string.IsNullOrEmpty(folder) || string.IsNullOrEmpty(logsRoot)) return null;
        string path = Path.Combine(logsRoot, folder, category + "-0.log");
        try
        {
            // The DLL holds the file open for writing (Sein opens it deny-write, so the reader must share write), and
            // rotates it by renaming (so share delete too).
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                          FileShare.ReadWrite | FileShare.Delete);
            long start = Math.Max(0, fs.Length - maxBytes);
            fs.Seek(start, SeekOrigin.Begin);
            var buf = new byte[fs.Length - start];
            int read = 0;
            while (read < buf.Length)
            {
                int n = fs.Read(buf, read, buf.Length - read);
                if (n <= 0) break;
                read += n;
            }
            string text = Encoding.UTF8.GetString(buf, 0, read);
            if (start > 0)
            {
                int nl = text.IndexOf('\n');
                text = nl >= 0 ? text[(nl + 1)..] : "";   // the first line was cut by the seek
            }
            return text;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            return null;
        }
    }
}
