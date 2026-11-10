using System.Text.RegularExpressions;

namespace UE5DumpUI.Tests;

/// <summary>
/// [TEST-TEMP-HYGIENE-2026-11-11] Removes what a finished `dotnet build` of THIS repository left in %TEMP% a day ago,
/// and nothing anyone else left there.
///
/// Measured on 2026-11-11 with SDK 10.0.403: every `dotnet build` / `dotnet test` / `dotnet publish` leaves one
/// Microsoft.NET.Workload_&lt;pid&gt;_&lt;yyyyMMdd&gt;_&lt;HHmmss&gt;_&lt;fff&gt;.log and two empty folders named like
/// Path.GetRandomFileName(), made from 581 ms before the log to 99 ms after it; no setting tried stops them, and the SDK
/// never removes them (1,452 folders and 726 logs in a day, from this repository and from an AOBMaker session's builds).
///
/// Only what is provably ours and provably finished goes, because the folders carry no owner: a folder is the SDK's only
/// by its name, its emptiness and the log made beside it, and anything that cannot be told apart stays.
/// <list type="bullet">
/// <item>A log is ours when its "Executing:" command names a project of this repository: an absolute path under the
/// repository root, or a relative path that is a project file here (or a folder holding one). A relative path is
/// resolved against this repository, so another repository's project would have to sit at the same relative path here.
/// No path at all, a `workload` command, or a path elsewhere: not ours.</item>
/// <item>A log is finished when it ends with the SDK's "Logging ended" line, every line is one an idle build writes
/// (a log that recorded real work is kept for whoever reads it), it has not been written for <see cref="MinAge"/>, and
/// nothing holds it open.</item>
/// <item>A folder goes when its name is a random file name, it is empty, it is no reparse point, it is
/// <see cref="MinAge"/> old, and every log made beside it is ours and finished: a folder beside another repository's
/// build, or a build still running, could be theirs.</item>
/// <item>A log goes only once no random-named folder beside it is left, so a folder that could not go keeps the log
/// that says whose it is.</item>
/// </list>
/// </summary>
internal static class SdkTempLeftovers
{
    internal static readonly TimeSpan MinAge = TimeSpan.FromDays(1);

    // A folder is made up to 581 ms before its log and up to 99 ms after it (measured over 1,452 folders); the margin
    // keeps a slow disk inside the window without reaching the next build.
    internal static readonly TimeSpan FolderBeforeLog = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan FolderAfterLog = TimeSpan.FromSeconds(1);

    private static readonly Regex LogName = new(
        @"^Microsoft\.NET\.Workload_\d+_\d{8}_\d{6}_\d{3}\.log$", RegexOptions.CultureInvariant);

    // Path.GetRandomFileName: 8 + 3 characters of the base32 alphabet a-z, 0-5.
    private static readonly Regex RandomName = new(@"^[a-z0-5]{8}\.[a-z0-5]{3}$", RegexOptions.CultureInvariant);

    private static readonly Regex LogLine = new(
        @"^\[\d{4}-\d\d-\d\d \d\d:\d\d:\d\d\.\d{3}\] \[[0-9A-F]{8}\] (?<body>.*)$", RegexOptions.CultureInvariant);

    private static readonly Regex Executing = new(
        @"^Executing: (?<cmd>.*), PID: \d+, PPID: \d+$", RegexOptions.CultureInvariant);

    private const string EndedLine = "=== Logging ended ===";

    // Every line an idle build's log holds (all 723 finished logs of 2026-11-11 hold exactly these, and nothing else).
    private static readonly string[] IdleLinePrefixes =
    {
        "=== Logging started ===", "Executing: ", "IsElevated: ", "Is64BitProcess: ", "RebootPending: ",
        "ProcessorArchitecture: ", "HostArchitecture: ", "SdkDirectory: ", "VerifySignatures: ", "SDK feature band: ",
        "Shutting down", "Shutdown completed.", "Restart required: ", EndedLine,
    };

    // The dotnet commands that leave these while doing nothing with workloads.
    private static readonly HashSet<string> BuildVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "build", "test", "publish", "run", "restore", "pack", "clean", "msbuild",
    };

    private static readonly string[] ProjectPatterns = { "*.csproj", "*.sln", "*.slnx" };

    internal sealed record Plan(IReadOnlyList<string> Folders, IReadOnlyList<string> Logs);

    private sealed record LogInfo(string Path, DateTime CreatedUtc, bool OursAndFinished);

    private sealed record FolderInfo(string Path, DateTime CreatedUtc, bool Deletable);

    /// <summary>What <see cref="Sweep"/> would remove; deletes nothing. Never throws.</summary>
    internal static Plan Find(string temp, string repoRoot, DateTime nowUtc)
    {
        try
        {
            return Decide(temp, repoRoot, nowUtc).Plan;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Plan(Array.Empty<string>(), Array.Empty<string>());
        }
    }

    /// <summary>Removes what <see cref="Find"/> names; returns what it removed. Never throws.</summary>
    internal static Plan Sweep(string temp, string repoRoot, DateTime nowUtc)
    {
        var folders = new List<string>();
        var logs = new List<string>();
        try
        {
            var decision = Decide(temp, repoRoot, nowUtc);
            foreach (var f in decision.Plan.Folders)
            {
                // Non-recursive: it removes an EMPTY folder or nothing, whatever arrived in it since it was looked at.
                try
                {
                    Directory.Delete(f, recursive: false);
                    folders.Add(f);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }

            var removed = new HashSet<string>(folders, StringComparer.OrdinalIgnoreCase);
            foreach (var log in decision.GoingLogs)
            {
                if (decision.FoldersBeside(log).Any(f => !removed.Contains(f.Path))) continue;
                try
                {
                    File.Delete(log.Path);
                    logs.Add(log.Path);
                }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new Plan(folders, logs);
    }

    private sealed record Decision(Plan Plan, List<LogInfo> GoingLogs, List<FolderInfo> Folders)
    {
        // Folders is sorted by creation time.
        public IEnumerable<FolderInfo> FoldersBeside(LogInfo log)
            => InRange(Folders, f => f.CreatedUtc, log.CreatedUtc - FolderBeforeLog, log.CreatedUtc + FolderAfterLog);
    }

    private static Decision Decide(string temp, string repoRoot, DateTime nowUtc)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        var listing = new DirectoryInfo(temp);

        // Times and attributes come with the listing. Asking for them entry by entry cost 288 ms of a 900-ms sweep
        // over 739 logs and 1,513 folders (measured 2026-11-11); the listing itself took 12 ms.
        var logs = new List<LogInfo>();
        foreach (var f in listing.EnumerateFiles("Microsoft.NET.Workload_*.log"))
        {
            if (!LogName.IsMatch(f.Name)) continue;
            var finished = nowUtc - f.LastWriteTimeUtc >= MinAge && IsOursAndFinished(f.FullName, root);
            logs.Add(new LogInfo(f.FullName, f.CreationTimeUtc, finished));
        }
        logs.Sort((x, y) => x.CreatedUtc.CompareTo(y.CreatedUtc));

        var folders = new List<FolderInfo>();
        foreach (var d in listing.EnumerateDirectories())
        {
            if (!RandomName.IsMatch(d.Name)) continue;
            var created = d.CreationTimeUtc;
            bool deletable;
            try
            {
                // The logs whose window holds this folder: made up to FolderAfterLog before it, FolderBeforeLog after.
                var beside = InRange(logs, l => l.CreatedUtc, created - FolderAfterLog, created + FolderBeforeLog)
                    .ToList();
                deletable = beside.Count > 0
                            && beside.All(l => l.OursAndFinished)
                            && nowUtc - created >= MinAge
                            && !d.Attributes.HasFlag(FileAttributes.ReparsePoint)
                            && !Directory.EnumerateFileSystemEntries(d.FullName).Any();
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException)
            {
                deletable = false;   // gone or unreadable since the listing: not ours to judge
            }
            folders.Add(new FolderInfo(d.FullName, created, deletable));
        }
        folders.Sort((x, y) => x.CreatedUtc.CompareTo(y.CreatedUtc));

        var decision = new Decision(new Plan(Array.Empty<string>(), Array.Empty<string>()), new List<LogInfo>(), folders);
        var goingLogs = logs.Where(l => l.OursAndFinished && decision.FoldersBeside(l).All(f => f.Deletable)).ToList();
        var plan = new Plan(folders.Where(f => f.Deletable).Select(f => f.Path).ToList(),
                            goingLogs.Select(l => l.Path).ToList());
        return decision with { Plan = plan, GoingLogs = goingLogs };
    }

    // The items of a list sorted by key whose key is within [lo, hi]: a binary search to the first, then a walk.
    private static IEnumerable<T> InRange<T>(List<T> sorted, Func<T, DateTime> key, DateTime lo, DateTime hi)
    {
        int a = 0, b = sorted.Count;
        while (a < b)
        {
            var m = (a + b) / 2;
            if (key(sorted[m]) < lo) a = m + 1;
            else b = m;
        }
        for (var i = a; i < sorted.Count && key(sorted[i]) <= hi; i++) yield return sorted[i];
    }

    private static bool Beside(DateTime folderCreatedUtc, DateTime logCreatedUtc)
        => folderCreatedUtc >= logCreatedUtc - FolderBeforeLog && folderCreatedUtc <= logCreatedUtc + FolderAfterLog;

    private static bool IsOursAndFinished(string log, string repoRoot)
    {
        string text;
        try
        {
            // Read as the SDK's logger would let a reader in: a log still held without sharing is a run in progress.
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.Read);
            if (fs.Length > 64 * 1024) return false;   // an idle build's log is about 1.6 KB
            using var reader = new StreamReader(fs);
            text = reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        string? command = null;
        foreach (var line in lines)
        {
            var m = LogLine.Match(line);
            if (!m.Success) return false;
            var body = m.Groups["body"].Value;
            if (!IdleLinePrefixes.Any(p => body.StartsWith(p, StringComparison.Ordinal))) return false;
            var ex = Executing.Match(body);
            if (ex.Success) command = ex.Groups["cmd"].Value;
        }
        if (lines.Length == 0 || !lines[^1].EndsWith(EndedLine, StringComparison.Ordinal)) return false;
        return command != null && NamesOurProject(command, repoRoot);
    }

    /// <summary>Whether a dotnet command line builds a project of the repository at <paramref name="repoRoot"/>.</summary>
    internal static bool NamesOurProject(string command, string repoRoot)
    {
        var args = SplitArgs(command);
        if (args.Count < 2) return false;
        var exe = Path.GetFileNameWithoutExtension(args[0]);
        if (!string.Equals(exe, "dotnet", StringComparison.OrdinalIgnoreCase)) return false;
        if (!BuildVerbs.Contains(args[1])) return false;

        foreach (var arg in args.Skip(2))
        {
            if (arg.StartsWith('-')) continue;
            string full;
            try
            {
                full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(arg, repoRoot));
            }
            catch (Exception e) when (e is ArgumentException or NotSupportedException or PathTooLongException) { continue; }
            if (!full.StartsWith(repoRoot + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)) continue;
            if (IsProjectFile(full) && File.Exists(full)) return true;
            if (Directory.Exists(full) && ProjectPatterns.Any(p => Directory.EnumerateFiles(full, p).Any())) return true;
        }
        return false;
    }

    private static bool IsProjectFile(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".sln", StringComparison.OrdinalIgnoreCase)
               || ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase);
    }

    // Windows command-line words: spaces separate, double quotes group.
    private static List<string> SplitArgs(string command)
    {
        var args = new List<string>();
        var current = new System.Text.StringBuilder();
        var quoted = false;
        var any = false;
        foreach (var c in command)
        {
            if (c == '"') { quoted = !quoted; any = true; continue; }
            if (c == ' ' && !quoted)
            {
                if (any) args.Add(current.ToString());
                current.Clear();
                any = false;
                continue;
            }
            current.Append(c);
            any = true;
        }
        if (any) args.Add(current.ToString());
        return args;
    }

    /// <summary>The repository holding the test binaries: the folder above them with this test project in it.</summary>
    internal static string? FindRepoRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "ui", "UE5DumpUI.Tests", "UE5DumpUI.Tests.csproj")))
                return dir.FullName;
        }
        return null;
    }
}
