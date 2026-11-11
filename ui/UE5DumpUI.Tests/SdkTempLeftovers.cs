using System.ComponentModel;
using System.Diagnostics;
using System.Security;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace UE5DumpUI.Tests;

/// <summary>
/// [TEST-TEMP-HYGIENE-2026-11-11] Removes what a finished `dotnet build` of THIS repository left in %TEMP% a day ago,
/// and nothing anyone else left there.
///
/// What the SDK leaves, read in dotnet/sdk v10.0.403 and measured on 2026-11-11: every build, test, publish, pack and
/// run starts a workload advertising-manifest check (RestoringCommand's constructor; `dotnet restore` leaves nothing). On
/// an SDK installed by MSI or Visual Studio it makes one empty folder named like Path.GetRandomFileName()
/// (Directory.CreateTempSubdirectory), then Microsoft.NET.Workload_&lt;pid&gt;_&lt;yyyyMMdd&gt;_&lt;HHmmss&gt;_&lt;fff&gt;.log, then
/// a second such folder -- up to about 0.8 s before the log and 0.11 s after it, and every group measured held exactly
/// two folders per log. Nothing in the SDK removes them and no setting skips them. `dotnet --info`, `dotnet new` and
/// `dotnet tool` make such folders with no log.
///
/// A folder carries no owner, so nothing goes on the absence of someone else's evidence; it goes on a positive match:
/// <list type="bullet">
/// <item>A log is ours when its "Executing:" command builds a project git tracks in this repository: the path exactly
/// as tracked (a command run from the repository root), an absolute path that ends with it at a folder boundary (any
/// checkout or worktree of this repository, gone or not), or the project's own folder when the folder is named like the
/// project. The word after an option known to take a value is that value, never the project; an option this code does
/// not know stops the reading (it may or may not take the next word), so that line is not ours. Anything after "--" is
/// the program's, and `dotnet run` builds only its --project. A bare `ui` is not ours, though this repository's ui
/// folder holds a solution: sibling repositories on this machine hold one in theirs.</item>
/// <item>A log is finished when it ends with the SDK's "Logging ended" line, every line is one an idle build writes (a
/// log that recorded work is kept for whoever reads it), it has not been written for <see cref="MinAge"/>, and nothing
/// holds it open.</item>
/// <item>Folders and logs are grouped: a folder joins every log made within its window, and logs that share a folder
/// are one group. A group goes only when every log in it is ours and finished, it holds exactly two folders per log,
/// each empty, no reparse point, and neither made nor written for <see cref="MinAge"/> -- and it is exactly the group a
/// run saw while it was young (made under <see cref="Young"/> ago, its last member over <see cref="Settle"/> ago),
/// recorded in <see cref="SeenGroupsFileName"/>. A test run sees the build that started it, and the headless suite,
/// which build.ps1 runs after this one, records its own (<see cref="RecordYoungGroups"/>).
/// Another cleanup can take a folder later, and a folder with no log of its own (another session's `dotnet --info`,
/// another program's temp folder) can sit in the window: the count would be right again, but the group is not the one
/// seen, so it stays whole, our log with it. A group first seen older is never known whole and stays. Our finished log
/// with no folder near it goes alone: it is ours, and nothing else goes with it.</item>
/// <item>A group's logs go only after all its folders went, so a folder that would not go keeps the log that says whose
/// it is. Folders go with a non-recursive delete, which removes an empty folder or nothing.</item>
/// <item>A finished log kept for good is remembered by name in <see cref="KeptLogsFileName"/> under the projects it was
/// judged against (<see cref="KeptHeader"/>), and is not read again by a run with the same projects. Another session's
/// logs are never ours, so without this every run would read all of them, about 0.17 ms each and ~600 more a day. A
/// wrong line there only keeps more. The seen record is the other way round: a line there lets that exact group go
/// (if its logs are ours and finished), so only lines under this code's <see cref="SeenHeader"/> are trusted, and a
/// run that cannot read the record neither records nor writes.</item>
/// </list>
/// Left behind on purpose: a build named by a relative path other than the tracked one (run from a subfolder, or a copy
/// of this repository under out/), a build with no path or with an option this code does not know before its project,
/// a group not seen young (a build no test run follows within <see cref="Young"/>), and a group changed since. Once the
/// SDK stops writing the log (dotnet/sdk main no longer does, 2026-11-10), nothing is ours and nothing goes.
/// </summary>
internal static class SdkTempLeftovers
{
    internal static readonly TimeSpan MinAge = TimeSpan.FromDays(1);

    // The window that joins a folder to a log: wider than the measured -0.8 / +0.11 s, so a slow disk does not split a
    // build; a wider window only makes groups bigger, and a group that is not exactly ours stays.
    internal static readonly TimeSpan FolderBeforeLog = TimeSpan.FromSeconds(2);
    internal static readonly TimeSpan FolderAfterLog = TimeSpan.FromSeconds(1);

    // A group is recorded only once its last member is this old (a build makes its second folder within 0.11 s of the
    // log), and only while its first member is younger than Young: another session's cleanup removes SDK leftovers
    // about an hour old, so a group seen at half that age cannot have lost a folder to it yet.
    internal static readonly TimeSpan Settle = TimeSpan.FromSeconds(5);
    internal static readonly TimeSpan Young = TimeSpan.FromMinutes(30);

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
        "build", "test", "publish", "pack", "run",
    };

    // Options of those commands that take no value: the word after one stands alone. An option in neither set stops the
    // reading, so one missing from either costs only a log of ours not attributed, which keeps it.
    private static readonly HashSet<string> FlagsWithoutValue = new(StringComparer.OrdinalIgnoreCase)
    {
        "--no-restore", "--no-build", "--no-dependencies", "--no-incremental", "--no-self-contained",
        "--self-contained", "--sc", "--force", "--interactive", "--nologo", "-nologo", "--no-logo",
        "--disable-build-servers", "--blame", "--list-tests", "--no-launch-profile", "--no-cache",
        "--ignore-failed-sources", "--use-current-runtime", "--ucr", "--disable-parallel", "--locked-mode",
        "--force-evaluate", "--no-http-cache", "--include-symbols", "--include-source", "--serviceable",
        "--no-ansi", "--no-progress",
    };

    // Options of those commands that take a value as the next word.
    private static readonly HashSet<string> OptionsWithValue = new(StringComparer.OrdinalIgnoreCase)
    {
        "-c", "--configuration", "-f", "--framework", "-r", "--runtime", "-o", "--output", "-v", "--verbosity",
        "-a", "--arch", "--os", "-s", "--source", "--results-directory", "--filter", "--logger", "-l", "--settings",
        "--diag", "-d", "--property", "-p", "--collect", "--blame-crash-dump-type", "--blame-hang-dump-type",
        "--blame-hang-timeout", "--launch-profile", "-lp", "--artifacts-path", "--packages", "--version-suffix",
        "--test-adapter-path", "--environment", "-e", "--configfile",
    };

    /// <summary>The memory of logs kept for good, in the memory folder.</summary>
    internal const string KeptLogsFileName = "sdk-logs-kept.txt";

    /// <summary>The groups seen young, one per line: the names of their members, sorted, joined by '|'.</summary>
    internal const string SeenGroupsFileName = "sdk-groups-seen.txt";

    /// <summary>The seen record's first line. A line there lets a group go, so a change to how groups are formed or
    /// recorded changes this version, and a record under any other header is not trusted.</summary>
    internal const string SeenHeader = "# sdk-groups-seen v1";

    /// <summary>The folder in %TEMP% the test runs share: their run folders, and both memories.</summary>
    internal const string SharedFolderName = "UE5CEDumper.Tests";

    /// <summary>Records the groups in <paramref name="temp"/> seen young, in <paramref name="memoryFolder"/>; deletes
    /// nothing and reads no log. The headless suite runs it: build.ps1 runs that suite after this one, so its own build
    /// would otherwise never be seen young. Never throws.</summary>
    internal static void RecordYoungGroups(string temp, DateTime nowUtc, string memoryFolder)
    {
        try
        {
            var seenFile = Path.Combine(memoryFolder, SeenGroupsFileName);
            var seen = ReadSeen(seenFile);
            if (!seen.Readable) return;
            var decision = Decide(temp, Array.Empty<string>(), nowUtc, new HashSet<string>(StringComparer.OrdinalIgnoreCase),
                seen.Keys, record: true, judge: false);
            if (!seen.Clean || !decision.Seen.SetEquals(seen.Keys))
                WriteMemory(seenFile, decision.Seen.Order(StringComparer.Ordinal).Prepend(SeenHeader));
        }
        catch (Exception e) when (Expected(e)) { }
    }

    // The exceptions a look at %TEMP% can meet: I/O and access, and a time or path .NET cannot represent (anyone can
    // stamp a file with a time past the year 9999).
    private static bool Expected(Exception e)
        => e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or SecurityException;

    internal sealed record Plan(IReadOnlyList<string> Folders, IReadOnlyList<string> Logs);

    // What a log is to the sweep. Only KeepForever is final for a set of projects, and so only it is remembered: a log
    // that has not ended may still be written, and one of ours goes.
    private enum Verdict { Ours, NotYet, KeepForever }

    private sealed record LogInfo(string Path, string Name, DateTime CreatedUtc, bool OursAndFinished);

    // Candidate: no reparse point, and neither made nor written for MinAge. Emptiness is asked only of a group that
    // could go, so another session's thousands of folders are never opened.
    private sealed record FolderInfo(string Path, string Name, DateTime CreatedUtc, bool Candidate);

    private sealed record Group(List<LogInfo> Logs, List<FolderInfo> Folders)
    {
        public string Key => string.Join("|",
            Logs.Select(l => l.Name).Concat(Folders.Select(f => f.Name)).Order(StringComparer.OrdinalIgnoreCase));

        public bool Goes(HashSet<string> seen)
        {
            if (Logs.Count == 0 || !Logs.All(l => l.OursAndFinished)) return false;
            if (Folders.Count == 0) return true;
            return Folders.Count == FoldersPerBuild * Logs.Count
                   && seen.Contains(Key)
                   && Folders.All(f => f.Candidate)
                   && Folders.All(f => IsEmpty(f.Path));
        }
    }

    private sealed record Decision(Plan Plan, List<Group> Going, HashSet<string> Present, List<string> NewlyKept,
        HashSet<string> Seen);

    /// <summary>What <see cref="Sweep"/> would remove; deletes and writes nothing. Never throws.</summary>
    internal static Plan Find(string temp, string repoRoot, DateTime nowUtc, string? memoryFolder = null,
        IReadOnlyCollection<string>? ourProjects = null)
    {
        try
        {
            var projects = ourProjects ?? TrackedProjects(repoRoot);
            if (projects is not { Count: > 0 }) return new Plan(Array.Empty<string>(), Array.Empty<string>());
            var kept = ReadKept(MemoryFile(memoryFolder, KeptLogsFileName), KeptHeader(projects));
            var seen = ReadSeen(MemoryFile(memoryFolder, SeenGroupsFileName));
            return Decide(temp, projects, nowUtc, kept.Names, seen.Keys, record: false, judge: true).Plan;
        }
        catch (Exception e) when (Expected(e))
        {
            return new Plan(Array.Empty<string>(), Array.Empty<string>());
        }
    }

    /// <summary>Removes what <see cref="Find"/> names; returns what it removed. Records the groups it sees young, and
    /// the logs it keeps for good, in <paramref name="memoryFolder"/>. Never throws.
    /// <paramref name="ourProjects"/> defaults to the project files git tracks under <paramref name="repoRoot"/>.</summary>
    internal static Plan Sweep(string temp, string repoRoot, DateTime nowUtc, string? memoryFolder = null,
        IReadOnlyCollection<string>? ourProjects = null)
    {
        var folders = new List<string>();
        var logs = new List<string>();
        try
        {
            var projects = ourProjects ?? TrackedProjects(repoRoot);
            if (projects is not { Count: > 0 }) return new Plan(folders, logs);   // nothing can be told to be ours
            var keptFile = MemoryFile(memoryFolder, KeptLogsFileName);
            var seenFile = MemoryFile(memoryFolder, SeenGroupsFileName);
            var header = KeptHeader(projects);
            var kept = ReadKept(keptFile, header);
            var seenBefore = ReadSeen(seenFile);
            // A record that cannot be read: nothing is recorded, and nothing that needs it goes.
            var decision = Decide(temp, projects, nowUtc, kept.Names, seenBefore.Keys, record: seenBefore.Readable,
                judge: true);

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
                    catch (Exception e) when (Expected(e)) { allGone = false; }
                }
                if (!allGone) continue;
                foreach (var l in group.Logs)
                {
                    try
                    {
                        if (!File.Exists(l.Path)) continue;   // another cleanup took it: not ours to report
                        File.Delete(l.Path);
                        logs.Add(l.Path);
                    }
                    catch (Exception e) when (Expected(e)) { }
                }
            }

            // What is remembered and still in %TEMP%, and what this run found kept for good.
            var stillKept = kept.Names.Where(decision.Present.Contains).Concat(decision.NewlyKept)
                .ToHashSet(StringComparer.OrdinalIgnoreCase);
            // Neither memory is written from a read that failed: written from that empty read, it would lose what it held.
            if (keptFile != null && kept.Readable && (!kept.Clean || !stillKept.SetEquals(kept.Names)))
                WriteMemory(keptFile, stillKept.Order(StringComparer.OrdinalIgnoreCase).Prepend(header));
            if (seenFile != null && seenBefore.Readable && (!seenBefore.Clean || !decision.Seen.SetEquals(seenBefore.Keys)))
                WriteMemory(seenFile, decision.Seen.Order(StringComparer.Ordinal).Prepend(SeenHeader));
        }
        catch (Exception e) when (Expected(e)) { }
        return new Plan(folders, logs);
    }

    private static string? MemoryFile(string? folder, string name) => folder == null ? null : Path.Combine(folder, name);

    /// <summary>The kept-logs memory's first line: its verdicts hold only for the projects they were judged
    /// against.</summary>
    internal static string KeptHeader(IReadOnlyCollection<string> ourProjects)
    {
        var joined = string.Join("\n", ourProjects.Select(p => p.Replace('\\', '/').ToLowerInvariant()).Order());
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(joined)))[..16];
        return "# projects " + hash.ToLowerInvariant();
    }

    // The names remembered under this header. Clean: the file was exactly that (a line that is no log name, or a header
    // for other projects, is dropped on the next write). Readable: false when the file is there and cannot be read.
    private static (HashSet<string> Names, bool Clean, bool Readable) ReadKept(string? file, string header)
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = ReadMemory(file, out var readable);
        if (!readable) return (names, true, false);
        if (lines == null) return (names, true, true);
        if (lines.Length == 0 || lines[0] != header) return (names, false, true);
        foreach (var line in lines.Skip(1))
            if (LogName.IsMatch(line)) names.Add(line);
        return (names, names.Count == lines.Length - 1, true);
    }

    // The group keys seen young, under this code's header; the same Clean and Readable as ReadKept. A key is compared
    // whole to let a group go; its names are read only to prune it, and to keep its logs from being recorded again.
    private static (HashSet<string> Keys, bool Clean, bool Readable) ReadSeen(string? file)
    {
        var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var lines = ReadMemory(file, out var readable);
        if (!readable) return (keys, true, false);
        if (lines == null) return (keys, true, true);
        if (lines.Length == 0 || lines[0] != SeenHeader) return (keys, false, true);
        foreach (var line in lines.Skip(1))
            if (line.Length > 0) keys.Add(line);
        return (keys, keys.Count == lines.Length - 1, true);
    }

    // A memory's lines; null when there is no file. A file that is there and cannot be read (another run replacing it,
    // an AV scan) is not readable, which is not the same as empty.
    private static string[]? ReadMemory(string? file, out bool readable)
    {
        readable = true;
        if (file == null) return null;
        try
        {
            return File.Exists(file) ? File.ReadAllLines(file) : null;
        }
        catch (Exception e) when (Expected(e))
        {
            readable = false;
            return null;
        }
    }

    // Written whole and moved over the old one, so a run reading it at the same moment sees one or the other.
    private static void WriteMemory(string file, IEnumerable<string> lines)
    {
        var staged = $"{file}.{Environment.ProcessId}.tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(file))!);
            File.WriteAllLines(staged, lines);
            File.Move(staged, file, overwrite: true);
        }
        catch (Exception e) when (Expected(e))
        {
            try { File.Delete(staged); }
            catch (Exception e2) when (Expected(e2)) { }
        }
    }

    private static Decision Decide(string temp, IReadOnlyCollection<string> ourProjects, DateTime nowUtc,
        HashSet<string> kept, HashSet<string> seenBefore, bool record, bool judge)
    {
        var listing = new DirectoryInfo(temp);

        // Times and attributes come with the listing. Asking for them entry by entry cost 288 ms of a 900-ms sweep
        // over 739 logs and 1,513 folders (measured 2026-11-11); the listing itself took 12 ms. An entry whose time
        // cannot be read is skipped, never the sweep.
        var logs = new List<LogInfo>();
        var present = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var newlyKept = new List<string>();
        foreach (var f in listing.EnumerateFiles("Microsoft.NET.Workload_*.log"))
        {
            try
            {
                if (!LogName.IsMatch(f.Name)) continue;
                var created = f.CreationTimeUtc;
                var written = f.LastWriteTimeUtc;
                _ = created - FolderBeforeLog;
                _ = created + FolderAfterLog + Settle;
                present.Add(f.Name);
                var ours = false;
                if (judge && !kept.Contains(f.Name) && nowUtc - written >= MinAge)
                {
                    var verdict = Judge(f.FullName, ourProjects);
                    if (verdict == Verdict.KeepForever) newlyKept.Add(f.Name);
                    ours = verdict == Verdict.Ours;
                }
                // A remembered log stays in the list: a folder beside it may be its build's.
                logs.Add(new LogInfo(f.FullName, f.Name, created, ours));
            }
            catch (Exception e) when (Expected(e)) { }
        }
        logs.Sort((x, y) => x.CreatedUtc.CompareTo(y.CreatedUtc));

        var folders = new List<FolderInfo>();
        foreach (var d in listing.EnumerateDirectories())
        {
            try
            {
                if (!RandomName.IsMatch(d.Name)) continue;
                var created = d.CreationTimeUtc;
                var written = d.LastWriteTimeUtc;
                _ = created - FolderAfterLog;
                _ = created + FolderBeforeLog + Settle;
                // An SDK folder is never written after it is made: one written since was someone's, however recently.
                var candidate = !d.Attributes.HasFlag(FileAttributes.ReparsePoint)
                                && nowUtc - created >= MinAge
                                && nowUtc - written >= MinAge;
                folders.Add(new FolderInfo(d.FullName, d.Name, created, candidate));
            }
            catch (Exception e) when (Expected(e)) { }
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
        var withLogs = groups.Values.Where(g => g.Logs.Count > 0).ToList();

        // The groups seen young: what was recorded and still has a log here -- less a group holding a log kept for
        // good, which can never go -- and each group seen young now whose logs no record names (a group recorded once
        // and changed since is not recorded again, so it never matches).
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var recordedLogs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var keptNow = new HashSet<string>(kept.Concat(newlyKept), StringComparer.OrdinalIgnoreCase);
        foreach (var key in seenBefore)
        {
            var names = key.Split('|');
            if (!names.Any(present.Contains) || names.Any(keptNow.Contains)) continue;
            seen.Add(key);
            foreach (var n in names) recordedLogs.Add(n);
        }
        if (record)
        {
            foreach (var g in withLogs)
            {
                var times = g.Logs.Select(l => l.CreatedUtc).Concat(g.Folders.Select(f => f.CreatedUtc)).ToList();
                var settled = nowUtc - times.Max() >= Settle;
                var young = nowUtc - times.Min() < Young;
                if (settled && young && !g.Logs.Any(l => recordedLogs.Contains(l.Name))) seen.Add(g.Key);
            }
        }

        var going = withLogs.Where(g => g.Goes(seen)).ToList();
        var plan = new Plan(going.SelectMany(g => g.Folders).Select(f => f.Path).ToList(),
                            going.SelectMany(g => g.Logs).Select(l => l.Path).ToList());
        return new Decision(plan, going, present, newlyKept, seen);
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
        catch (Exception e) when (Expected(e)) { return false; }
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
        catch (Exception e) when (Expected(e)) { return Verdict.NotYet; }

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
        var verb = args[1];
        if (!BuildVerbs.Contains(verb)) return false;
        // `dotnet run` builds only its --project: a positional word there is the program's, or a file-based app.
        var positional = !string.Equals(verb, "run", StringComparison.OrdinalIgnoreCase);

        for (var i = 2; i < args.Count; i++)
        {
            var arg = args[i];
            if (arg == "--") break;   // what follows is the program's own arguments, not the build's
            if (arg.StartsWith("--project=", StringComparison.OrdinalIgnoreCase)
                || arg.StartsWith("--solution=", StringComparison.OrdinalIgnoreCase))
            {
                if (IsOurProject(arg[(arg.IndexOf('=') + 1)..], ourProjects)) return true;
                continue;
            }
            if (string.Equals(arg, "--project", StringComparison.OrdinalIgnoreCase)
                || string.Equals(arg, "--solution", StringComparison.OrdinalIgnoreCase))
            {
                if (i + 1 < args.Count && IsOurProject(args[++i], ourProjects)) return true;
                continue;
            }
            if (arg.StartsWith('-'))
            {
                // A value attached (-p:X=Y, --verbosity=q) or a flag that takes none: the next word stands alone.
                if (arg.Contains(':') || arg.Contains('=') || FlagsWithoutValue.Contains(arg)) continue;
                // An option this code does not know may or may not take the next word, so the rest of the line cannot
                // be read: not ours. A log of ours missed this way is kept, the safe way to be wrong.
                if (!OptionsWithValue.Contains(arg)) return false;
                // Its value -- never an option or "--", which would shift the reading of everything after it.
                if (i + 1 < args.Count && !args[i + 1].StartsWith('-')) i++;
                continue;
            }
            if (positional && IsOurProject(arg, ourProjects)) return true;
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

    /// <summary>The words of a Windows command line as dotnet.exe's argv holds them (the MSVC UCRT rules): spaces
    /// separate outside quotes; 2n backslashes before a quote give n and the quote opens or closes; 2n+1 give n and a
    /// literal quote; "" inside quotes is a literal quote and quoting goes on (CommandLineToArgvW would end it there,
    /// and split a word out that the program never sees); any other backslash is itself.</summary>
    internal static List<string> SplitArgs(string command)
    {
        var args = new List<string>();
        var current = new StringBuilder();
        var quoted = false;
        var any = false;
        var i = 0;
        while (i < command.Length)
        {
            var c = command[i];
            if (c == '\\')
            {
                var n = 0;
                while (i < command.Length && command[i] == '\\') { n++; i++; }
                if (i < command.Length && command[i] == '"')
                {
                    current.Append('\\', n / 2);
                    if (n % 2 == 1) { current.Append('"'); i++; }   // escaped: a literal quote
                }
                else current.Append('\\', n);
                any = true;
                continue;
            }
            if (c == '"')
            {
                if (quoted && i + 1 < command.Length && command[i + 1] == '"')
                {
                    current.Append('"');   // "" inside quotes
                    i += 2;
                    continue;
                }
                quoted = !quoted;
                any = true;
                i++;
                continue;
            }
            if ((c == ' ' || c == '\t') && !quoted)
            {
                if (any) args.Add(current.ToString());
                current.Clear();
                any = false;
                i++;
                continue;
            }
            current.Append(c);
            any = true;
            i++;
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
