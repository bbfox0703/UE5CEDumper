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
/// <item>A finished log kept for good (another repository's, or one that recorded work) is remembered by name in
/// <see cref="KeptLogsFileName"/> and never read again: the other session's logs are never ours to delete, so without
/// this every run would read all of them, about 0.17 ms each and ~600 more a day. The memory can only keep a log; a
/// wrong or edited line makes the sweep keep more, never delete more.</item>
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

    /// <summary>The memory of logs kept for good, beside the test runs' folders.</summary>
    internal const string KeptLogsFileName = "sdk-logs-kept.txt";

    internal sealed record Plan(IReadOnlyList<string> Folders, IReadOnlyList<string> Logs);

    // What a log is to the sweep. Only KeepForever is final, and so only it is remembered: a log that has not ended may
    // still be written, and one of ours goes.
    private enum Verdict { Ours, NotYet, KeepForever }

    private sealed record LogInfo(string Path, DateTime CreatedUtc, bool OursAndFinished);

    private sealed record FolderInfo(string Path, DateTime CreatedUtc, bool Deletable);

    /// <summary>What <see cref="Sweep"/> would remove; deletes nothing. Never throws.</summary>
    internal static Plan Find(string temp, string repoRoot, DateTime nowUtc, string? keptLogsFile = null,
        IReadOnlyCollection<string>? ourProjects = null)
    {
        try
        {
            return Decide(temp, repoRoot, nowUtc, ReadKept(keptLogsFile).Names).Plan;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Plan(Array.Empty<string>(), Array.Empty<string>());
        }
    }

    /// <summary>Removes what <see cref="Find"/> names; returns what it removed. Never throws.</summary>
    internal static Plan Sweep(string temp, string repoRoot, DateTime nowUtc, string? keptLogsFile = null,
        IReadOnlyCollection<string>? ourProjects = null)
    {
        var folders = new List<string>();
        var logs = new List<string>();
        try
        {
            var kept = ReadKept(keptLogsFile);
            var decision = Decide(temp, repoRoot, nowUtc, kept.Names);
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

            // What is remembered and still in %TEMP%, and what this run found kept for good.
            var stillKept = kept.Names.Where(decision.Present.Contains).Concat(decision.NewlyKept)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (keptLogsFile != null && (!kept.Clean || !stillKept.SetEquals(kept.Names)))
                WriteKept(keptLogsFile, stillKept);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new Plan(folders, logs);
    }

    // The names remembered, and whether every line was one: a line that is no log name is dropped on the next write.
    private static (HashSet<string> Names, bool Clean) ReadKept(string? file)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (file == null) return (names, true);
        string[] lines;
        try
        {
            if (!File.Exists(file)) return (names, true);
            lines = File.ReadAllLines(file);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return (names, true); }
        foreach (var line in lines)
            if (LogName.IsMatch(line)) names.Add(line);
        return (names, names.Count == lines.Length);
    }

    // Written whole and moved over the old one, so a run reading it at the same moment sees one or the other.
    private static void WriteKept(string file, HashSet<string> names)
    {
        var staged = $"{file}.{Environment.ProcessId}.tmp";
        try
        {
            File.WriteAllLines(staged, names.Order(StringComparer.OrdinalIgnoreCase));
            File.Move(staged, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(staged); }
            catch (Exception e2) when (e2 is IOException or UnauthorizedAccessException) { }
        }
    }

    private sealed record Decision(Plan Plan, List<LogInfo> GoingLogs, List<FolderInfo> Folders,
        HashSet<string> Present, List<string> NewlyKept)
    {
        // Folders is sorted by creation time.
        public IEnumerable<FolderInfo> FoldersBeside(LogInfo log)
            => InRange(Folders, f => f.CreatedUtc, log.CreatedUtc - FolderBeforeLog, log.CreatedUtc + FolderAfterLog);
    }

    private static Decision Decide(string temp, string repoRoot, DateTime nowUtc, HashSet<string> kept)
    {
        var root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(repoRoot));
        var listing = new DirectoryInfo(temp);

        // Times and attributes come with the listing. Asking for them entry by entry cost 288 ms of a 900-ms sweep
        // over 739 logs and 1,513 folders (measured 2026-11-11); the listing itself took 12 ms.
        var logs = new List<LogInfo>();
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newlyKept = new List<string>();
        foreach (var f in listing.EnumerateFiles("Microsoft.NET.Workload_*.log"))
        {
            if (!LogName.IsMatch(f.Name)) continue;
            present.Add(f.Name);
            var ours = false;
            if (!kept.Contains(f.Name) && nowUtc - f.LastWriteTimeUtc >= MinAge)
            {
                var verdict = Judge(f.FullName, root);
                if (verdict == Verdict.KeepForever) newlyKept.Add(f.Name);
                ours = verdict == Verdict.Ours;
            }
            logs.Add(new LogInfo(f.FullName, f.CreationTimeUtc, ours));
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

        var decision = new Decision(new Plan(Array.Empty<string>(), Array.Empty<string>()), new List<LogInfo>(), folders,
            present, newlyKept);
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

    private static Verdict Judge(string log, string repoRoot)
    {
        string text;
        try
        {
            // Read as the SDK's logger would let a reader in: a log still held without sharing is a run in progress.
            using var fs = new FileStream(log, FileMode.Open, FileAccess.Read, FileShare.Read);
            // An idle build's log is about 1.6 KB: one this large recorded work, whatever else it is.
            if (fs.Length > 64 * 1024) return Verdict.KeepForever;
            using var reader = new StreamReader(fs);
            text = reader.ReadToEnd();
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Verdict.NotYet; }

        var lines = text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        // Until it ends, the SDK may still write to it, so nothing about it is final yet.
        if (lines.Length == 0 || !lines[^1].EndsWith(EndedLine, StringComparison.Ordinal)) return Verdict.NotYet;
        string? command = null;
        foreach (var line in lines)
        {
            var m = LogLine.Match(line);
            if (!m.Success) return Verdict.KeepForever;
            var body = m.Groups["body"].Value;
            if (!IdleLinePrefixes.Any(p => body.StartsWith(p, StringComparison.Ordinal))) return Verdict.KeepForever;
            var ex = Executing.Match(body);
            if (ex.Success) command = ex.Groups["cmd"].Value;
        }
        return command != null && NamesOurProject(command, repoRoot) ? Verdict.Ours : Verdict.KeepForever;
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

    /// <summary>Red stub: the project files git tracks in the repository.</summary>
    internal static IReadOnlyList<string>? TrackedProjects(string repoRoot) => null;

    /// <summary>Red stub: the memory's first line for a set of projects.</summary>
    internal static string KeptHeader(IReadOnlyCollection<string> ourProjects) => "# projects";

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
