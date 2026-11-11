using System.ComponentModel;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UE5DumpUI.Tests;

/// <summary>
/// [TEST-TEMP-HYGIENE-2026-11-11] Removes what a finished `dotnet build` of THIS repository left in %TEMP% a day ago,
/// and nothing anyone else left there.
///
/// What the SDK leaves, read in dotnet/sdk v10.0.403 and measured on 2026-11-11: every build, test, publish, pack, run
/// and restore starts a workload advertising-manifest check (RestoringCommand's constructor). On an SDK installed by
/// MSI or Visual Studio it makes one empty folder named like Path.GetRandomFileName() (Directory.CreateTempSubdirectory),
/// then Microsoft.NET.Workload_&lt;pid&gt;_&lt;yyyyMMdd&gt;_&lt;HHmmss&gt;_&lt;fff&gt;.log, then a second such folder -- 581 ms before
/// the log to 99 ms after it, and in 107 groups of them every group held exactly two folders per log. Nothing in the SDK
/// removes them and no setting skips them. `dotnet --info`, `dotnet new` and `dotnet tool` make such folders with no log.
///
/// A folder carries no owner, so nothing goes on the absence of someone else's evidence; it goes on a positive match:
/// <list type="bullet">
/// <item>A log is ours when its "Executing:" command builds a project git tracks in this repository: the path exactly
/// as tracked (a command run from the repository root), an absolute path that ends with it at a folder boundary (any
/// checkout or worktree of this repository, gone or not), or the project's own folder when the folder is named like the
/// project. An option's value, or anything after "--" (the program's own arguments), is not the project. A bare `ui`
/// is therefore not ours, though this repository's ui folder holds a solution: sibling repositories on this machine
/// hold one in theirs.</item>
/// <item>A log is finished when it ends with the SDK's "Logging ended" line, every line is one an idle build writes (a
/// log that recorded work is kept for whoever reads it), it has not been written for <see cref="MinAge"/>, and nothing
/// holds it open.</item>
/// <item>Folders and logs are grouped: a folder joins every log made within its window, and logs that share a folder
/// are one group. A group goes only when every log in it is ours and finished, and it holds exactly two folders per log,
/// each empty, no reparse point, and neither made nor written for <see cref="MinAge"/>. A group with one folder too many
/// (another build's whose log was removed, `dotnet --info`, another program's temp folder) or too few stays whole, our
/// log with it. Our finished log with no folder near it goes alone.</item>
/// <item>A group's logs go only after all its folders went, so a folder that would not go keeps the log that says whose
/// it is. Folders go with a non-recursive delete, which removes an empty folder or nothing.</item>
/// <item>A finished log kept for good is remembered by name in <see cref="KeptLogsFileName"/> under the projects it was
/// judged against (<see cref="KeptHeader"/>), and is not read again by a run with the same projects. Another session's
/// logs are never ours, so without this every run would read all of them, about 0.17 ms each and ~600 more a day. The
/// memory can only keep a log: a wrong or edited line makes the sweep keep more, never delete more.</item>
/// </list>
/// Left behind on purpose: a build run from a subfolder with a partial path, a build with no path, and a group
/// another cleanup took a folder from. Once the SDK stops writing the log (dotnet/sdk main no longer does, 2026-11-10),
/// nothing is ours and nothing goes.
/// </summary>
internal static class SdkTempLeftovers
{
    internal static readonly TimeSpan MinAge = TimeSpan.FromDays(1);

    // The window that joins a folder to a log: wider than the measured -581 / +99 ms, so a slow disk does not split a
    // build; a wider window only makes groups bigger, and a group that is not exactly ours stays.
    internal static readonly TimeSpan FolderBeforeLog = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan FolderAfterLog = TimeSpan.FromSeconds(1);

    private const int FoldersPerBuild = 2;

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

    // The commands that reach the manifest check (they derive from or build a RestoringCommand).
    private static readonly HashSet<string> BuildVerbs = new(StringComparer.OrdinalIgnoreCase)
    {
        "build", "test", "publish", "pack", "run", "restore",
    };

    /// <summary>The memory of logs kept for good, beside the test runs' folders.</summary>
    internal const string KeptLogsFileName = "sdk-logs-kept.txt";

    /// <summary>Red stub: the groups seen young.</summary>
    internal const string SeenGroupsFileName = "sdk-groups-seen.txt";
    internal static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan Young = TimeSpan.FromMinutes(10);

    internal sealed record Plan(IReadOnlyList<string> Folders, IReadOnlyList<string> Logs);

    // What a log is to the sweep. Only KeepForever is final for a set of projects, and so only it is remembered: a log
    // that has not ended may still be written, and one of ours goes.
    private enum Verdict { Ours, NotYet, KeepForever }

    private sealed record LogInfo(string Path, DateTime CreatedUtc, bool OursAndFinished);

    // Candidate: no reparse point, and neither made nor written for MinAge. Emptiness is asked only of a group that
    // could go, so another session's thousands of folders are never opened.
    private sealed record FolderInfo(string Path, DateTime CreatedUtc, bool Candidate);

    private sealed record Group(List<LogInfo> Logs, List<FolderInfo> Folders)
    {
        public bool Goes()
        {
            if (Logs.Count == 0 || !Logs.All(l => l.OursAndFinished)) return false;
            if (Folders.Count == 0) return true;
            return Folders.Count == FoldersPerBuild * Logs.Count
                   && Folders.All(f => f.Candidate)
                   && Folders.All(f => IsEmpty(f.Path));
        }
    }

    private sealed record Decision(Plan Plan, List<Group> Going, HashSet<string> Present, List<string> NewlyKept);

    /// <summary>What <see cref="Sweep"/> would remove; deletes and writes nothing. Never throws.</summary>
    internal static Plan Find(string temp, string repoRoot, DateTime nowUtc, string? memoryFolder = null,
        IReadOnlyCollection<string>? ourProjects = null)
    {
        var keptLogsFile = memoryFolder == null ? null : Path.Combine(memoryFolder, KeptLogsFileName);
        try
        {
            var projects = ourProjects ?? TrackedProjects(repoRoot);
            if (projects is not { Count: > 0 }) return new Plan(Array.Empty<string>(), Array.Empty<string>());
            return Decide(temp, projects, nowUtc, ReadKept(keptLogsFile, KeptHeader(projects)).Names).Plan;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            return new Plan(Array.Empty<string>(), Array.Empty<string>());
        }
    }

    /// <summary>Removes what <see cref="Find"/> names; returns what it removed. Never throws.
    /// <paramref name="ourProjects"/> defaults to the project files git tracks under <paramref name="repoRoot"/>.</summary>
    internal static Plan Sweep(string temp, string repoRoot, DateTime nowUtc, string? memoryFolder = null,
        IReadOnlyCollection<string>? ourProjects = null)
    {
        var keptLogsFile = memoryFolder == null ? null : Path.Combine(memoryFolder, KeptLogsFileName);
        var folders = new List<string>();
        var logs = new List<string>();
        try
        {
            var projects = ourProjects ?? TrackedProjects(repoRoot);
            if (projects is not { Count: > 0 }) return new Plan(folders, logs);   // nothing can be told to be ours
            var header = KeptHeader(projects);
            var kept = ReadKept(keptLogsFile, header);
            var decision = Decide(temp, projects, nowUtc, kept.Names);

            foreach (var group in decision.Going)
            {
                var allGone = true;
                foreach (var f in group.Folders)
                {
                    // Non-recursive: it removes an EMPTY folder or nothing, whatever arrived in it since it was looked at.
                    try
                    {
                        Directory.Delete(f.Path, recursive: false);
                        folders.Add(f.Path);
                    }
                    catch (DirectoryNotFoundException) { }   // gone already (another cleanup): not one left behind
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { allGone = false; }
                }
                if (!allGone) continue;
                foreach (var l in group.Logs)
                {
                    try
                    {
                        File.Delete(l.Path);
                        logs.Add(l.Path);
                    }
                    catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
                }
            }

            // What is remembered and still in %TEMP%, and what this run found kept for good.
            var stillKept = kept.Names.Where(decision.Present.Contains).Concat(decision.NewlyKept)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            if (keptLogsFile != null && (!kept.Clean || !stillKept.SetEquals(kept.Names)))
                WriteKept(keptLogsFile, header, stillKept);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
        return new Plan(folders, logs);
    }

    /// <summary>The memory's first line: its verdicts hold only for the projects they were judged against.</summary>
    internal static string KeptHeader(IReadOnlyCollection<string> ourProjects)
    {
        var joined = string.Join("\n", ourProjects.Select(p => p.Replace('\\', '/').ToLowerInvariant()).Order());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
        return "# projects " + hash.ToLowerInvariant();
    }

    // The names remembered under this header, and whether the file was exactly that: a line that is no log name, or a
    // header for other projects, is dropped on the next write.
    private static (HashSet<string> Names, bool Clean) ReadKept(string? file, string header)
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
        if (lines.Length == 0 || lines[0] != header) return (names, false);
        foreach (var line in lines.Skip(1))
            if (LogName.IsMatch(line)) names.Add(line);
        return (names, names.Count == lines.Length - 1);
    }

    // Written whole and moved over the old one, so a run reading it at the same moment sees one or the other.
    private static void WriteKept(string file, string header, HashSet<string> names)
    {
        var staged = $"{file}.{Environment.ProcessId}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            File.WriteAllLines(staged, names.Order(StringComparer.OrdinalIgnoreCase).Prepend(header));
            File.Move(staged, file, overwrite: true);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            try { File.Delete(staged); }
            catch (Exception e2) when (e2 is IOException or UnauthorizedAccessException) { }
        }
    }

    private static Decision Decide(string temp, IReadOnlyCollection<string> ourProjects, DateTime nowUtc,
        HashSet<string> kept)
    {
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
                var verdict = Judge(f.FullName, ourProjects);
                if (verdict == Verdict.KeepForever) newlyKept.Add(f.Name);
                ours = verdict == Verdict.Ours;
            }
            // A remembered log stays in the list: a folder beside it may be its build's.
            logs.Add(new LogInfo(f.FullName, f.CreationTimeUtc, ours));
        }
        logs.Sort((x, y) => x.CreatedUtc.CompareTo(y.CreatedUtc));

        var folders = new List<FolderInfo>();
        foreach (var d in listing.EnumerateDirectories())
        {
            if (!RandomName.IsMatch(d.Name)) continue;
            // An SDK folder is never written after it is made: one written since was someone's, however recently.
            var candidate = !d.Attributes.HasFlag(FileAttributes.ReparsePoint)
                            && nowUtc - d.CreationTimeUtc >= MinAge
                            && nowUtc - d.LastWriteTimeUtc >= MinAge;
            folders.Add(new FolderInfo(d.FullName, d.CreationTimeUtc, candidate));
        }

        // Group: a folder joins every log made within its window; logs sharing a folder are one group.
        var parent = Enumerable.Range(0, logs.Count + folders.Count).ToArray();
        int Root(int x)
        {
            while (parent[x] != x) x = parent[x] = parent[parent[x]];
            return x;
        }
        for (var i = 0; i < folders.Count; i++)
        {
            var created = folders[i].CreatedUtc;
            foreach (var j in InRange(logs, l => l.CreatedUtc, created - FolderAfterLog, created + FolderBeforeLog))
                parent[Root(logs.Count + i)] = Root(j);
        }
        var groups = new Dictionary<int, Group>();
        Group At(int x)
        {
            var r = Root(x);
            if (!groups.TryGetValue(r, out var g)) groups[r] = g = new Group(new List<LogInfo>(), new List<FolderInfo>());
            return g;
        }
        for (var j = 0; j < logs.Count; j++) At(j).Logs.Add(logs[j]);
        for (var i = 0; i < folders.Count; i++) At(logs.Count + i).Folders.Add(folders[i]);

        var going = groups.Values.Where(g => g.Goes()).ToList();
        var plan = new Plan(going.SelectMany(g => g.Folders).Select(f => f.Path).ToList(),
                            going.SelectMany(g => g.Logs).Select(l => l.Path).ToList());
        return new Decision(plan, going, present, newlyKept);
    }

    // The indexes of a list sorted by key whose key is within [lo, hi]: a binary search to the first, then a walk.
    private static IEnumerable<int> InRange<T>(List<T> sorted, Func<T, DateTime> key, DateTime lo, DateTime hi)
    {
        int a = 0, b = sorted.Count;
        while (a < b)
        {
            var m = (a + b) / 2;
            if (key(sorted[m]) < lo) a = m + 1;
            else b = m;
        }
        for (var i = a; i < sorted.Count && key(sorted[i]) <= hi; i++) yield return i;
    }

    private static bool IsEmpty(string folder)
    {
        try { return !Directory.EnumerateFileSystemEntries(folder).Any(); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
    }

    private static Verdict Judge(string log, IReadOnlyCollection<string> ourProjects)
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
        return command != null && NamesOurProject(command, ourProjects) ? Verdict.Ours : Verdict.KeepForever;
    }

    /// <summary>Whether a dotnet command line builds one of <paramref name="ourProjects"/> (paths as git tracks them,
    /// relative to the repository root).</summary>
    internal static bool NamesOurProject(string command, IReadOnlyCollection<string> ourProjects)
    {
        var args = SplitArgs(command);
        if (args.Count < 2) return false;
        if (!string.Equals(Path.GetFileNameWithoutExtension(args[0]), "dotnet", StringComparison.OrdinalIgnoreCase))
            return false;
        if (!BuildVerbs.Contains(args[1])) return false;

        foreach (var arg in args.Skip(2))
        {
            if (arg == "--") break;   // what follows is the program's own arguments, not the build's
            var path = arg;
            if (arg.StartsWith("--project=", StringComparison.OrdinalIgnoreCase)) path = arg["--project=".Length..];
            else if (arg.StartsWith('-')) continue;
            if (IsOurProject(path, ourProjects)) return true;
        }
        return false;
    }

    private static bool IsOurProject(string arg, IReadOnlyCollection<string> ourProjects)
    {
        var p = arg.Replace('/', '\\').TrimEnd('\\');
        while (p.StartsWith(".\\", StringComparison.Ordinal)) p = p[2..];
        if (p.Length == 0) return false;
        var absolute = Path.IsPathFullyQualified(p);
        foreach (var tracked in ourProjects)
        {
            var file = tracked.Replace('/', '\\');
            if (Matches(p, file, absolute)) return true;
            // Its folder, when the folder is named like the project: `ui` holding UE5DumpUI.sln is not.
            var folder = Path.GetDirectoryName(file);
            if (!string.IsNullOrEmpty(folder)
                && string.Equals(Path.GetFileName(folder), Path.GetFileNameWithoutExtension(file),
                    StringComparison.OrdinalIgnoreCase)
                && Matches(p, folder, absolute))
                return true;
        }
        return false;

        static bool Matches(string path, string tracked, bool absolute)
            => absolute
                ? path.EndsWith("\\" + tracked, StringComparison.OrdinalIgnoreCase)
                : string.Equals(path, tracked, StringComparison.OrdinalIgnoreCase);
    }

    // Windows command-line words: spaces separate, double quotes group.
    private static List<string> SplitArgs(string command)
    {
        var args = new List<string>();
        var current = new StringBuilder();
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

    /// <summary>The project files git tracks in the repository at <paramref name="repoRoot"/>, as git prints them; null
    /// when git cannot say (no git, not a checkout), and then nothing is ours. A clone of another repository under this
    /// one (vendor/) is tracked by no one here, so it is not ours.</summary>
    internal static IReadOnlyList<string>? TrackedProjects(string repoRoot)
    {
        try
        {
            var psi = new ProcessStartInfo("git")
            {
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true,
            };
            foreach (var a in new[] { "-C", repoRoot, "ls-files", "-z", "--", "*.csproj", "*.sln", "*.slnx" })
                psi.ArgumentList.Add(a);
            using var p = Process.Start(psi);
            if (p == null) return null;
            var stdout = p.StandardOutput.ReadToEndAsync();
            _ = p.StandardError.ReadToEndAsync();
            if (!p.WaitForExit(10_000))
            {
                try { p.Kill(); }
                catch (Exception e) when (e is InvalidOperationException or Win32Exception) { }
                return null;
            }
            if (p.ExitCode != 0) return null;
            var list = stdout.Result.Split('\0', StringSplitOptions.RemoveEmptyEntries);
            return list.Length > 0 ? list : null;
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException or IOException) { return null; }
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
