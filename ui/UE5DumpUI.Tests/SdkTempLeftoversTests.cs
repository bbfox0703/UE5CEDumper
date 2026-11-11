using System.Globalization;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [TEST-TEMP-HYGIENE-2026-11-11] <see cref="SdkTempLeftovers"/>: what a finished `dotnet build` of THIS repository
/// left in %TEMP% a day ago goes, and nothing else does -- not another repository's (another session builds beside
/// this one, and cleans up after itself), not a build still running, not a log that recorded real work, not another
/// program's folder.
/// Measured on 2026-11-11 (SDK 10.0.403): each build leaves one Microsoft.NET.Workload_*.log and two empty folders
/// named like Path.GetRandomFileName(), one made before the log (up to 581 ms) and one after it (up to 99 ms); in 107
/// groups of them every group held exactly two folders per log.
/// </summary>
public class SdkTempLeftoversTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "SdkTempLeftoversTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _temp;
    private readonly string _repo;
    private readonly DateTime _now = DateTime.UtcNow;
    private readonly DateTime _old;
    private int _pid = 1000;
    private int _folder;

    // The projects git tracks in the fake repository, as `git ls-files` prints them. The repository's own `ui` folder
    // holds a solution, as this repository's does, and so do sibling repositories on this machine.
    private static readonly string[] Ours = { "ui/App.sln", "ui/App/App.csproj", "ui/App.Tests/App.Tests.csproj" };

    public SdkTempLeftoversTests()
    {
        _temp = Directory.CreateDirectory(Path.Combine(_sandbox, "temp")).FullName;
        _repo = Directory.CreateDirectory(Path.Combine(_sandbox, "repo")).FullName;
        foreach (var p in Ours)
        {
            var full = Path.Combine(_repo, p.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllText(full, "<Project />");
        }
        Directory.CreateDirectory(Path.Combine(_repo, "tools", "verify"));   // a folder with no project in it
        // A clone of another repository under this one, as vendor/ holds: on disk here, tracked by no one here.
        Directory.CreateDirectory(Path.Combine(_repo, "vendor", "Lib"));
        File.WriteAllText(Path.Combine(_repo, "vendor", "Lib", "Lib.csproj"), "<Project />");
        _old = _now - SdkTempLeftovers.MinAge - TimeSpan.FromHours(1);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_sandbox)) return;
        foreach (var d in new DirectoryInfo(_temp).EnumerateDirectories())
            if (!d.Attributes.HasFlag(FileAttributes.ReparsePoint))
                d.Attributes &= ~FileAttributes.ReadOnly;   // a test may leave a folder read-only
        TestTempRoot.ClearReadOnly(_sandbox);
        Directory.Delete(_sandbox, recursive: true);
    }

    private const string OurBuild = "dotnet build ui/App/App.csproj -c Release -v q";
    private const string TheirBuild = "dotnet build tests/AOBMaker.Core.Tests -v q -nologo";

    private SdkTempLeftovers.Plan Sweep(string? kept = null, IReadOnlyCollection<string>? ours = null)
        => SdkTempLeftovers.Sweep(_temp, _repo, _now, kept, ours ?? Ours);

    [Fact]
    public void OurFinishedBuild_ADayOld_ItsLogAndBothFoldersGo()
    {
        var (log, before, after) = Build(_old, OurBuild);

        var plan = Sweep();

        Assert.False(Directory.Exists(before));
        Assert.False(Directory.Exists(after));
        Assert.False(File.Exists(log));
        Assert.Equal(2, plan.Folders.Count);
        Assert.Single(plan.Logs);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" test --project \"{repo}\\ui\\App\\App.csproj\" -c Release")]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" publish \"{repo}/ui/App/App.csproj\" -c Release -r win-x64")]
    [InlineData("dotnet build ui/App -v q -nologo")]
    [InlineData("dotnet build ui/App.sln -v q")]
    [InlineData("dotnet test ui\\App.Tests\\App.Tests.csproj -c Release --filter FullyQualifiedName~X")]
    // A worktree removed since: its path no longer exists, and the build was still this repository's.
    [InlineData("dotnet test --project \"D:\\Gone\\.claude\\worktrees\\wf_1\\ui\\App.Tests\\App.Tests.csproj\" -c Release")]
    public void OurCommandLines_AreOurs(string command)
    {
        var (log, before, after) = Build(_old, command.Replace("{repo}", _repo));

        Sweep();

        Assert.False(File.Exists(log));
        Assert.False(Directory.Exists(before));
        Assert.False(Directory.Exists(after));
    }

    [Theory]
    [InlineData(TheirBuild)]
    [InlineData("dotnet build Atk.csproj -c Debug -nologo -v q")]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" build src/AOBMaker.Core -v q -nologo")]
    [InlineData("dotnet build D:/Github/AOBMaker/.claude/worktrees/wf_1\\tests/AOBMaker.Core.Tests -v q -nologo")]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" build -v q -nologo")]
    [InlineData("dotnet build tools/verify -v q")]
    [InlineData("dotnet build . -v q")]
    [InlineData("dotnet workload restore ui/App/App.csproj")]
    [InlineData("dotnet build -p:Root=\"{repo}\" Other.csproj")]
    // A sibling repository with the same layout: `ui` there holds its own solution.
    [InlineData("dotnet build ui -c Release")]
    [InlineData("dotnet restore ui")]
    [InlineData("dotnet test ui")]
    // An option's value, or the program's own arguments, is not the project being built.
    [InlineData("dotnet publish src/Other.csproj -o ui")]
    [InlineData("dotnet test tests/AOBMaker.Core.Tests --filter Ui -v q")]
    [InlineData("dotnet run --project D:/Other/Tool/Tool.csproj -- ui/App/App.csproj")]
    // Under this checkout, but another repository's clone that git here does not track.
    [InlineData("dotnet build \"{repo}\\vendor\\Lib\\Lib.csproj\"")]
    // Ends with our path's characters but not at a folder boundary.
    [InlineData("dotnet build D:\\x\\notui\\App\\App.csproj")]
    [InlineData("dotnet build D:/Github/AOBMaker/tools/AobScriptProbe/AobScriptProbe.csproj")]
    public void AnotherReposOrAnUnattributableCommand_IsKept(string command)
    {
        var (log, before, after) = Build(_old, command.Replace("{repo}", _repo));

        var plan = Sweep();

        Assert.True(File.Exists(log));
        Assert.True(Directory.Exists(before));
        Assert.True(Directory.Exists(after));
        Assert.Empty(plan.Folders);
        Assert.Empty(plan.Logs);
    }

    [Fact]
    public void ABuildStillRunning_IsKept()
    {
        var (log, before, after) = Build(_old, OurBuild, ended: false);

        Sweep();

        Assert.True(File.Exists(log));
        Assert.True(Directory.Exists(before));
        Assert.True(Directory.Exists(after));
    }

    [Fact]
    public void ABuildUnderADayOld_IsKept()
    {
        var (log, before, after) = Build(_now - SdkTempLeftovers.MinAge + TimeSpan.FromHours(1), OurBuild);

        Sweep();

        Assert.True(File.Exists(log));
        Assert.True(Directory.Exists(before));
        Assert.True(Directory.Exists(after));
    }

    [Fact]
    public void ALogThatRecordedWork_IsKept_AndSoAreItsFolders()
    {
        var (log, before, after) = Build(_old, OurBuild, extraLines: new[] { "ExtractManifestAsync: Extracting manifest" });

        Sweep();

        Assert.True(File.Exists(log));
        Assert.True(Directory.Exists(before));
        Assert.True(Directory.Exists(after));
    }

    [Fact]
    public void AFolderBesideAnotherReposBuild_IsKept_AndOurLogStaysWithIt()
    {
        var ours = Log(_old, OurBuild);
        var theirs = Log(_old + TimeSpan.FromMilliseconds(300), TheirBuild);
        var shared = Folder(_old + TimeSpan.FromMilliseconds(100));

        Sweep();

        Assert.True(Directory.Exists(shared));
        Assert.True(File.Exists(theirs));
        Assert.True(File.Exists(ours));   // a log goes only once no folder it may have made is left
    }

    // ---- a folder is ours only when the group it is in is exactly what our builds make -----------------------------

    /// <summary>Folders with no log of their own beside one of our builds: another build's whose log its own cleanup
    /// removed (two), `dotnet --info` or `dotnet new`, which write no log (two), or another program's
    /// Directory.CreateTempSubdirectory() (one). None can be told from ours, so the whole group stays, our log too.</summary>
    [Theory]
    [InlineData(1, 300)]
    [InlineData(2, 300)]
    [InlineData(2, -1500)]
    public void FoldersWithNoLogOfTheirOwn_BesideOurBuild_AllStay(int extra, int offsetMs)
    {
        var (log, before, after) = Build(_old, OurBuild);
        var others = Enumerable.Range(0, extra)
            .Select(i => Folder(_old + TimeSpan.FromMilliseconds(offsetMs + i))).ToList();

        var plan = Sweep();

        Assert.True(File.Exists(log));
        Assert.True(Directory.Exists(before));
        Assert.True(Directory.Exists(after));
        Assert.All(others, f => Assert.True(Directory.Exists(f)));
        Assert.Empty(plan.Folders);
    }

    [Fact]
    public void OurBuildWithOneFolderGone_IsKept()
    {
        var (log, before, after) = Build(_old, OurBuild);
        Directory.Delete(after);

        Sweep();

        Assert.True(File.Exists(log));
        Assert.True(Directory.Exists(before));
    }

    [Fact]
    public void TwoOfOurBuildsAtOnce_AllGo()
    {
        var (log1, b1, a1) = Build(_old, OurBuild);
        var (log2, b2, a2) = Build(_old + TimeSpan.FromMilliseconds(10), "dotnet test ui/App.Tests/App.Tests.csproj");

        Sweep();

        foreach (var f in new[] { b1, a1, b2, a2 }) Assert.False(Directory.Exists(f));
        Assert.False(File.Exists(log1));
        Assert.False(File.Exists(log2));
    }

    [Fact]
    public void OurLogWithNoFolderBesideIt_Goes()
    {
        var log = Log(_old, OurBuild);

        Sweep();

        Assert.False(File.Exists(log));
    }

    [Fact]
    public void AFolderWithNoLogBesideIt_IsKept()
    {
        Log(_old, OurBuild);
        var far = Folder(_old + TimeSpan.FromSeconds(10));
        var farBefore = Folder(_old - TimeSpan.FromSeconds(10));

        Sweep();

        Assert.True(Directory.Exists(far));
        Assert.True(Directory.Exists(farBefore));
    }

    [Fact]
    public void AFolderWithSomethingInIt_KeepsItsWholeGroup()
    {
        var (log, before, after) = Build(_old, OurBuild);
        File.WriteAllText(Path.Combine(after, "in-use.txt"), "x");
        Directory.SetLastWriteTimeUtc(after, _old);

        Sweep();

        Assert.True(Directory.Exists(after));
        Assert.True(Directory.Exists(before));   // its empty sibling is not ours to tell apart either
        Assert.True(File.Exists(log));
    }

    /// <summary>An SDK folder is never written after it is made; one written since was someone's, a moment ago.</summary>
    [Fact]
    public void AFolderUsedRecently_KeepsItsWholeGroup()
    {
        var (log, before, after) = Build(_old, OurBuild);
        Directory.SetLastWriteTimeUtc(after, _now - TimeSpan.FromMinutes(1));

        Sweep();

        Assert.True(Directory.Exists(after));
        Assert.True(Directory.Exists(before));
        Assert.True(File.Exists(log));
    }

    /// <summary>A folder planned to go that will not (a read-only folder refuses RemoveDirectory) keeps the log that
    /// says whose it is.</summary>
    [Fact]
    public void AFolderThatWillNotGo_KeepsTheLog()
    {
        var (log, before, after) = Build(_old, OurBuild);
        new DirectoryInfo(after).Attributes |= FileAttributes.ReadOnly;

        Sweep();

        Assert.True(Directory.Exists(after));
        Assert.True(File.Exists(log));
    }

    [Fact]
    public void NamesThatAreNotARandomFileName_AreKept()
    {
        Log(_old, OurBuild);
        string[] names = { "ABCDEFGH.IJK", "abcdefg6.ijk", "abcdefgh.ij", "abcdefghi.jkl", "abcdefgh_ijk", "abcdefgh" };
        foreach (var n in names) Folder(_old, n);
        var file = Path.Combine(_temp, "mnopqrst.uvw");
        File.WriteAllText(file, "");
        File.SetCreationTimeUtc(file, _old);

        Sweep();

        foreach (var n in names) Assert.True(Directory.Exists(Path.Combine(_temp, n)), n);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void AJunctionWithARandomName_IsKept_AndSoIsItsGroup()
    {
        var log = Log(_old, OurBuild);
        var folder = Folder(_old - TimeSpan.FromMilliseconds(100));
        var target = Directory.CreateDirectory(Path.Combine(_sandbox, "elsewhere")).FullName;
        var link = Path.Combine(_temp, "zzzzzzzz.zzz");
        Assert.True(TestJunction.Make(link, target));
        Directory.SetCreationTimeUtc(link, _old);

        Sweep();

        Assert.True(Directory.Exists(link));
        Assert.True(Directory.Exists(target));
        Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(log));
        Directory.Delete(link);
    }

    /// <summary>A log someone holds is a build not done; the rest of the sweep goes on, and it is not remembered.</summary>
    [Fact]
    public void ALogHeldOpen_IsKept_AndTheRestOfTheSweepGoesOn()
    {
        var (log, before, after) = Build(_old, OurBuild);
        var (other, ob, oa) = Build(_old + TimeSpan.FromSeconds(30), OurBuild);
        using var held = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        Sweep(Kept);

        Assert.True(File.Exists(log));
        Assert.True(Directory.Exists(before));
        Assert.True(Directory.Exists(after));
        Assert.False(File.Exists(other));
        Assert.False(Directory.Exists(ob));
        Assert.False(Directory.Exists(oa));
        Assert.DoesNotContain(Path.GetFileName(log), Remembered());
    }

    [Theory]
    [InlineData("Microsoft.NET.Workload_1.log")]
    [InlineData("Microsoft.NET.Workload_1_20261109_120000_000.txt")]
    [InlineData("Microsoft.NET.Workload_x_20261109_120000_000.log")]
    [InlineData("Microsoft.NET.Workload_1_20261109_120000_000_payload_install.log")]
    [InlineData("Setup-1-1.log")]
    public void AFileNotNamedAsTheSdkNamesItsLog_IsKept(string name)
    {
        var path = Path.Combine(_temp, name);
        File.WriteAllText(path, Lines(_old, OurBuild, ended: true, extra: null));
        File.SetCreationTimeUtc(path, _old);
        File.SetLastWriteTimeUtc(path, _old);
        var before = Folder(_old - TimeSpan.FromMilliseconds(100));
        var after = Folder(_old + TimeSpan.FromMilliseconds(10));

        Sweep();

        Assert.True(File.Exists(path));
        Assert.True(Directory.Exists(before));
        Assert.True(Directory.Exists(after));
    }

    [Fact]
    public void FindDeletesNothing()
    {
        var (log, before, after) = Build(_old, OurBuild);

        var plan = SdkTempLeftovers.Find(_temp, _repo, _now, null, Ours);

        Assert.Equal(new[] { before, after }.Order(), plan.Folders.Order());
        Assert.Equal(new[] { log }, plan.Logs);
        Assert.True(Directory.Exists(before));
        Assert.True(Directory.Exists(after));
        Assert.True(File.Exists(log));
    }

    [Fact]
    public void ATempFolderThatIsGone_IsNothingToSweep()
    {
        var plan = SdkTempLeftovers.Sweep(Path.Combine(_sandbox, "absent"), _repo, _now, null, Ours);
        Assert.Empty(plan.Folders);
        Assert.Empty(plan.Logs);
    }

    // ---- the logs kept for good are remembered, so a run reads only what is new ----------------------------------

    private string Kept => Path.Combine(_sandbox, "kept.txt");

    private List<string> Remembered(string? file = null)
    {
        var f = file ?? Kept;
        return File.Exists(f) ? File.ReadAllLines(f).Where(l => !l.StartsWith('#')).ToList() : new();
    }

    [Fact]
    public void AnotherReposFinishedLog_IsRemembered_AndNeverReadAgain()
    {
        var log = Log(_old, TheirBuild);

        Sweep(Kept);
        Assert.Contains(Path.GetFileName(log), Remembered());

        // Read again, it would now be ours and go: remembered, it is not read, and stays.
        File.WriteAllText(log, Lines(_old, OurBuild, ended: true, extra: null));
        File.SetCreationTimeUtc(log, _old);
        File.SetLastWriteTimeUtc(log, _old);
        Sweep(Kept);
        Assert.True(File.Exists(log));
    }

    /// <summary>A remembered log is not read again, but it is still there: a folder beside it may be its build's.</summary>
    [Fact]
    public void ARememberedLog_StillKeepsTheFoldersBesideIt()
    {
        var theirs = Log(_old, TheirBuild);
        var ours = Log(_old + TimeSpan.FromMilliseconds(10), OurBuild);
        var folders = Enumerable.Range(0, 4).Select(i => Folder(_old + TimeSpan.FromMilliseconds(i * 5 - 300))).ToList();

        Sweep(Kept);
        Assert.Contains(Path.GetFileName(theirs), Remembered());
        Sweep(Kept);

        Assert.All(folders, f => Assert.True(Directory.Exists(f)));
        Assert.True(File.Exists(theirs));
        Assert.True(File.Exists(ours));
    }

    [Fact]
    public void ALogThatRecordedWork_IsRemembered()
    {
        var log = Log(_old, OurBuild, extraLines: new[] { "ExtractManifestAsync: Extracting manifest" });

        Sweep(Kept);

        Assert.Contains(Path.GetFileName(log), Remembered());
    }

    [Fact]
    public void ALogStillRunning_OrUnderADayOld_IsNotRemembered()
    {
        var running = Log(_old, TheirBuild, ended: false);
        var young = Log(_now - TimeSpan.FromHours(1), TheirBuild);

        Sweep(Kept);

        Assert.DoesNotContain(Path.GetFileName(running), Remembered());
        Assert.DoesNotContain(Path.GetFileName(young), Remembered());
    }

    [Fact]
    public void OurLog_IsNotRemembered_ItGoes()
    {
        var log = Log(_old, OurBuild);

        Sweep(Kept);

        Assert.False(File.Exists(log));
        Assert.DoesNotContain(Path.GetFileName(log), Remembered());
    }

    [Fact]
    public void ARememberedLogThatIsGone_IsForgotten_AndALineThatIsNoLogNameIsDropped()
    {
        var log = Log(_old, TheirBuild);
        File.WriteAllLines(Kept, new[]
        {
            SdkTempLeftovers.KeptHeader(Ours),
            "Microsoft.NET.Workload_9_20261101_120000_000.log",   // no such file any more
            @"..\..\Windows\win.ini", "", "not a log name",
        });

        Sweep(Kept);

        Assert.Equal(new[] { Path.GetFileName(log) }, Remembered());
    }

    /// <summary>The first run on a machine, or the first after the shared folder was removed: the memory's folder does
    /// not exist yet, and the memory is written all the same.</summary>
    [Fact]
    public void TheMemoryIsWritten_WhenItsFolderDoesNotExistYet()
    {
        var log = Log(_old, TheirBuild);
        var kept = Path.Combine(_sandbox, "not-yet", "kept.txt");

        Sweep(kept);

        Assert.Contains(Path.GetFileName(log), Remembered(kept));
    }

    /// <summary>"Not ours" holds only for the projects it was judged against: a checkout with other projects (an older
    /// worktree, a project added since) judges the log again.</summary>
    [Fact]
    public void AVerdictRememberedForOtherProjects_IsNotReused()
    {
        const string probe = "dotnet build tools/Probe/Probe.csproj -v q";
        var (log, before, after) = Build(_old, probe);
        Sweep(Kept);
        Assert.Contains(Path.GetFileName(log), Remembered());

        Sweep(Kept, Ours.Append("tools/Probe/Probe.csproj").ToArray());

        Assert.False(File.Exists(log));
        Assert.False(Directory.Exists(before));
        Assert.False(Directory.Exists(after));
    }

    [Fact]
    public void FindReadsWhatIsRemembered_ButNeverWritesIt()
    {
        var (log, _, _) = Build(_old, OurBuild);
        var their = Log(_old + TimeSpan.FromMinutes(5), TheirBuild);
        File.WriteAllLines(Kept, new[] { SdkTempLeftovers.KeptHeader(Ours), Path.GetFileName(log) });
        var before = File.ReadAllText(Kept);

        var plan = SdkTempLeftovers.Find(_temp, _repo, _now, Kept, Ours);

        Assert.DoesNotContain(log, plan.Logs);   // remembered: kept, though read it would be ours
        Assert.Equal(before, File.ReadAllText(Kept));
        Assert.DoesNotContain(Path.GetFileName(their), Remembered());
    }

    [Fact]
    public void AMemoryHeldByAnother_IsNoMemory_AndTheSweepGoesOn()
    {
        File.WriteAllLines(Kept, new[] { SdkTempLeftovers.KeptHeader(Ours) });
        using var held = new FileStream(Kept, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        var (log, _, _) = Build(_old, OurBuild);

        Sweep(Kept);

        Assert.False(File.Exists(log));
    }

    [Fact]
    public void AMemoryThatIsAFolder_IsNoMemory()
    {
        Directory.CreateDirectory(Kept);
        var (log, _, _) = Build(_old, OurBuild);

        Sweep(Kept);

        Assert.False(File.Exists(log));
    }

    // ---- fixtures: the shapes measured in %TEMP% on 2026-11-11 ----------------------------------------------------

    /// <summary>One build as the SDK leaves it: a folder made before the log, the log, a folder made after it.</summary>
    private (string Log, string Before, string After) Build(DateTime createdUtc, string command, bool ended = true,
        string[]? extraLines = null)
    {
        var before = Folder(createdUtc - TimeSpan.FromMilliseconds(120));
        var log = Log(createdUtc, command, ended, extraLines);
        var after = Folder(createdUtc + TimeSpan.FromMilliseconds(15));
        return (log, before, after);
    }

    private string Log(DateTime createdUtc, string command, bool ended = true, string[]? extraLines = null)
    {
        var local = createdUtc.ToLocalTime();
        var name = string.Create(CultureInfo.InvariantCulture,
            $"Microsoft.NET.Workload_{_pid++}_{local:yyyyMMdd}_{local:HHmmss}_{local:fff}.log");
        var path = Path.Combine(_temp, name);
        File.WriteAllText(path, Lines(createdUtc, command, ended, extraLines));
        File.SetCreationTimeUtc(path, createdUtc);
        File.SetLastWriteTimeUtc(path, createdUtc + TimeSpan.FromSeconds(8));
        return path;
    }

    private static string Lines(DateTime createdUtc, string command, bool ended, string[]? extra)
    {
        var t = createdUtc.ToLocalTime();
        string L(string body) => string.Create(CultureInfo.InvariantCulture, $"[{t:yyyy-MM-dd HH:mm:ss.fff}] [0000BE50] {body}\r\n");
        var s = L("=== Logging started ===")
              + L($"Executing: {command}, PID: 48720, PPID: 5760")
              + L("IsElevated: False") + L("Is64BitProcess: True") + L("RebootPending: False")
              + L("ProcessorArchitecture: amd64") + L("HostArchitecture: x64") + L("SdkDirectory: 10.0.403")
              + L("VerifySignatures: False") + L("SDK feature band: 10.0.400");
        foreach (var e in extra ?? Array.Empty<string>()) s += L(e);
        if (ended) s += L("Shutting down") + L("Shutdown completed.") + L("Restart required: False") + L("=== Logging ended ===");
        return s;
    }

    // A random file name in the base32 alphabet Path.GetRandomFileName uses (a-z, 0-5), unique per test.
    private string Folder(DateTime createdUtc, string? name = null)
    {
        const string b32 = "abcdefghijklmnopqrstuvwxyz012345";
        if (name == null)
        {
            var n = _folder++;
            var chars = new char[11];
            for (var i = 0; i < 11; i++) { chars[i] = b32[(n + i * 7) % 32]; n /= 2; }
            name = new string(chars, 0, 8) + "." + new string(chars, 8, 3);
        }
        var dir = Directory.CreateDirectory(Path.Combine(_temp, name)).FullName;
        Directory.SetCreationTimeUtc(dir, createdUtc);
        Directory.SetLastWriteTimeUtc(dir, createdUtc);
        return dir;
    }
}
