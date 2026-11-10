using System.Globalization;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [TEST-TEMP-HYGIENE-2026-11-11] <see cref="SdkTempLeftovers"/>: what a finished `dotnet build` of THIS repository
/// left in %TEMP% a day ago goes, and nothing else does -- not another repository's (another session builds beside
/// this one), not a build still running, not a log that recorded real work, not another program's folder.
/// Measured on 2026-11-11 (SDK 10.0.403): each build leaves one Microsoft.NET.Workload_*.log and two empty folders
/// named like Path.GetRandomFileName(), made from 581 ms before the log to 99 ms after it.
/// </summary>
public class SdkTempLeftoversTests : IDisposable
{
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "SdkTempLeftoversTests_" + Guid.NewGuid().ToString("N"));
    private readonly string _temp;
    private readonly string _repo;
    private readonly DateTime _now = DateTime.UtcNow;
    private readonly DateTime _old;
    private int _pid = 1000;

    public SdkTempLeftoversTests()
    {
        _temp = Directory.CreateDirectory(Path.Combine(_sandbox, "temp")).FullName;
        _repo = Directory.CreateDirectory(Path.Combine(_sandbox, "repo")).FullName;
        Directory.CreateDirectory(Path.Combine(_repo, "ui", "App"));
        File.WriteAllText(Path.Combine(_repo, "ui", "App", "App.csproj"), "<Project />");
        Directory.CreateDirectory(Path.Combine(_repo, "tools", "verify"));   // a folder with no project in it
        _old = _now - SdkTempLeftovers.MinAge - TimeSpan.FromHours(1);
    }

    public void Dispose()
    {
        if (!Directory.Exists(_sandbox)) return;
        TestTempRoot.ClearReadOnly(_sandbox);
        Directory.Delete(_sandbox, recursive: true);
    }

    private const string OurBuild = "dotnet build ui/App/App.csproj -c Release -v q";

    [Fact]
    public void OurFinishedBuild_ADayOld_ItsLogAndBothFoldersGo()
    {
        var log = Log(_old, OurBuild);
        var before = Folder("abcdefgh.ijk", _old - TimeSpan.FromMilliseconds(581));
        var same = Folder("lmnopqrs.tuv", _old);

        var plan = SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.False(Directory.Exists(before));
        Assert.False(Directory.Exists(same));
        Assert.False(File.Exists(log));
        Assert.Equal(2, plan.Folders.Count);
        Assert.Single(plan.Logs);
    }

    [Theory]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" test --project {repo}\\ui\\App\\App.csproj -c Release")]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" publish {repo}/ui/App/App.csproj -c Release -r win-x64")]
    [InlineData("dotnet build ui/App -v q -nologo")]
    [InlineData("dotnet test ui\\App\\App.csproj -c Release --filter FullyQualifiedName~X")]
    public void OurCommandLines_AreOurs(string command)
    {
        var log = Log(_old, command.Replace("{repo}", _repo));
        var folder = Folder("abcdefgh.ijk", _old);

        SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.False(File.Exists(log));
        Assert.False(Directory.Exists(folder));
    }

    [Theory]
    [InlineData("dotnet build tests/AOBMaker.Core.Tests -v q -nologo")]
    [InlineData("dotnet build Atk.csproj -c Debug -nologo -v q")]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" build src/AOBMaker.Core -v q -nologo")]
    [InlineData("dotnet build D:/Github/AOBMaker/.claude/worktrees/wf_1\\tests/AOBMaker.Core.Tests -v q -nologo")]
    [InlineData("\"C:\\Program Files\\dotnet\\dotnet.exe\" build -v q -nologo")]
    [InlineData("dotnet build tools/verify -v q")]
    [InlineData("dotnet build . -v q")]
    [InlineData("dotnet workload restore ui/App/App.csproj")]
    [InlineData("dotnet build -p:Root={repo} Other.csproj")]
    public void AnotherReposOrAnUnattributableCommand_IsKept(string command)
    {
        var log = Log(_old, command.Replace("{repo}", _repo));
        var folder = Folder("abcdefgh.ijk", _old);

        var plan = SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.True(File.Exists(log));
        Assert.True(Directory.Exists(folder));
        Assert.Empty(plan.Folders);
        Assert.Empty(plan.Logs);
    }

    [Fact]
    public void ABuildStillRunning_IsKept()
    {
        var log = Log(_old, OurBuild, ended: false);
        var folder = Folder("abcdefgh.ijk", _old);

        SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.True(File.Exists(log));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void ABuildUnderADayOld_IsKept()
    {
        var young = _now - SdkTempLeftovers.MinAge + TimeSpan.FromHours(1);
        var log = Log(young, OurBuild);
        var folder = Folder("abcdefgh.ijk", young);

        SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.True(File.Exists(log));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void ALogThatRecordedWork_IsKept_AndSoAreItsFolders()
    {
        var log = Log(_old, OurBuild, extraLines: new[] { "ExtractManifestAsync: Extracting manifest" });
        var folder = Folder("abcdefgh.ijk", _old);

        SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.True(File.Exists(log));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void AFolderBesideAnotherReposBuild_IsKept_AndOurLogStaysWithIt()
    {
        var ours = Log(_old, OurBuild);
        var theirs = Log(_old + TimeSpan.FromMilliseconds(300), "dotnet build tests/AOBMaker.Core.Tests -v q -nologo");
        var shared = Folder("abcdefgh.ijk", _old + TimeSpan.FromMilliseconds(100));

        SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.True(Directory.Exists(shared));
        Assert.True(File.Exists(theirs));
        Assert.True(File.Exists(ours));   // a log goes only once no folder it may have made is left
    }

    [Fact]
    public void AFolderWithNoLogBesideIt_IsKept()
    {
        Log(_old, OurBuild);
        var far = Folder("abcdefgh.ijk", _old + TimeSpan.FromSeconds(10));
        var farBefore = Folder("lmnopqrs.tuv", _old - TimeSpan.FromSeconds(10));

        SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.True(Directory.Exists(far));
        Assert.True(Directory.Exists(farBefore));
    }

    [Fact]
    public void AFolderWithSomethingInIt_IsKept_AndSoIsTheLog()
    {
        var log = Log(_old, OurBuild);
        var folder = Folder("abcdefgh.ijk", _old);
        File.WriteAllText(Path.Combine(folder, "in-use.txt"), "x");
        Directory.SetCreationTimeUtc(folder, _old);

        SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(log));
    }

    [Fact]
    public void NamesThatAreNotARandomFileName_AreKept()
    {
        Log(_old, OurBuild);
        string[] names = { "ABCDEFGH.IJK", "abcdefg6.ijk", "abcdefgh.ij", "abcdefghi.jkl", "abcdefgh_ijk", "abcdefgh" };
        foreach (var n in names) Folder(n, _old);
        var file = Path.Combine(_temp, "mnopqrst.uvw");
        File.WriteAllText(file, "");
        File.SetCreationTimeUtc(file, _old);

        SdkTempLeftovers.Sweep(_temp, _repo, _now);

        foreach (var n in names) Assert.True(Directory.Exists(Path.Combine(_temp, n)), n);
        Assert.True(File.Exists(file));
    }

    [Fact]
    public void AJunctionWithARandomName_IsKept_AndSoIsTheLog()
    {
        var log = Log(_old, OurBuild);
        var target = Directory.CreateDirectory(Path.Combine(_sandbox, "elsewhere")).FullName;
        var link = Path.Combine(_temp, "abcdefgh.ijk");
        Assert.True(TestJunction.Make(link, target));
        Directory.SetCreationTimeUtc(link, _old);

        SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.True(Directory.Exists(link));
        Assert.True(Directory.Exists(target));
        Assert.True(File.Exists(log));
        Directory.Delete(link);
    }

    [Fact]
    public void ALogHeldOpen_IsKept()
    {
        var log = Log(_old, OurBuild);
        var folder = Folder("abcdefgh.ijk", _old);
        using var held = new FileStream(log, FileMode.Open, FileAccess.ReadWrite, FileShare.None);

        SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(log));
    }

    [Theory]
    [InlineData("Microsoft.NET.Workload_1.log")]
    [InlineData("Microsoft.NET.Workload_1_20261109_120000_000.txt")]
    [InlineData("Microsoft.NET.Workload_x_20261109_120000_000.log")]
    [InlineData("Setup-1-1.log")]
    public void AFileNotNamedAsTheSdkNamesItsLog_IsKept(string name)
    {
        var path = Path.Combine(_temp, name);
        File.WriteAllText(path, Lines(_old, OurBuild, ended: true, extra: null));
        File.SetCreationTimeUtc(path, _old);
        File.SetLastWriteTimeUtc(path, _old);
        var folder = Folder("abcdefgh.ijk", _old);

        SdkTempLeftovers.Sweep(_temp, _repo, _now);

        Assert.True(File.Exists(path));
        Assert.True(Directory.Exists(folder));
    }

    [Fact]
    public void FindDeletesNothing()
    {
        var log = Log(_old, OurBuild);
        var folder = Folder("abcdefgh.ijk", _old);

        var plan = SdkTempLeftovers.Find(_temp, _repo, _now);

        Assert.Equal(new[] { folder }, plan.Folders);
        Assert.Equal(new[] { log }, plan.Logs);
        Assert.True(Directory.Exists(folder));
        Assert.True(File.Exists(log));
    }

    [Fact]
    public void ATempFolderThatIsGone_IsNothingToSweep()
    {
        var plan = SdkTempLeftovers.Sweep(Path.Combine(_sandbox, "absent"), _repo, _now);
        Assert.Empty(plan.Folders);
        Assert.Empty(plan.Logs);
    }

    // ---- the logs kept for good are remembered, so a run reads only what is new ----------------------------------

    private string Kept => Path.Combine(_sandbox, "kept.txt");

    private HashSet<string> Remembered()
        => File.Exists(Kept) ? File.ReadAllLines(Kept).ToHashSet(StringComparer.OrdinalIgnoreCase) : new();

    [Fact]
    public void AnotherReposFinishedLog_IsRemembered_AndNeverReadAgain()
    {
        var log = Log(_old, "dotnet build tests/AOBMaker.Core.Tests -v q -nologo");

        SdkTempLeftovers.Sweep(_temp, _repo, _now, Kept);
        Assert.Contains(Path.GetFileName(log), Remembered());

        // Read again, it would now be ours and go: remembered, it is not read, and stays.
        File.WriteAllText(log, Lines(_old, OurBuild, ended: true, extra: null));
        File.SetCreationTimeUtc(log, _old);
        File.SetLastWriteTimeUtc(log, _old);
        SdkTempLeftovers.Sweep(_temp, _repo, _now, Kept);
        Assert.True(File.Exists(log));
    }

    [Fact]
    public void ALogThatRecordedWork_IsRemembered()
    {
        var log = Log(_old, OurBuild, extraLines: new[] { "ExtractManifestAsync: Extracting manifest" });

        SdkTempLeftovers.Sweep(_temp, _repo, _now, Kept);

        Assert.Contains(Path.GetFileName(log), Remembered());
    }

    [Fact]
    public void ALogStillRunning_OrUnderADayOld_IsNotRemembered()
    {
        var running = Log(_old, "dotnet build tests/AOBMaker.Core.Tests -v q -nologo", ended: false);
        var young = Log(_now - TimeSpan.FromHours(1), "dotnet build tests/AOBMaker.Core.Tests -v q -nologo");

        SdkTempLeftovers.Sweep(_temp, _repo, _now, Kept);

        Assert.DoesNotContain(Path.GetFileName(running), Remembered());
        Assert.DoesNotContain(Path.GetFileName(young), Remembered());
    }

    [Fact]
    public void OurLog_IsNotRemembered_ItGoes()
    {
        var log = Log(_old, OurBuild);

        SdkTempLeftovers.Sweep(_temp, _repo, _now, Kept);

        Assert.False(File.Exists(log));
        Assert.DoesNotContain(Path.GetFileName(log), Remembered());
    }

    [Fact]
    public void ARememberedLogThatIsGone_IsForgotten_AndALineThatIsNoLogNameIsDropped()
    {
        var log = Log(_old, "dotnet build tests/AOBMaker.Core.Tests -v q -nologo");
        File.WriteAllLines(Kept, new[]
        {
            "Microsoft.NET.Workload_9_20261101_120000_000.log",   // no such file any more
            @"..\..\Windows\win.ini", "", "not a log name",
        });

        SdkTempLeftovers.Sweep(_temp, _repo, _now, Kept);

        Assert.Equal(new[] { Path.GetFileName(log) }, File.ReadAllLines(Kept));
    }

    [Fact]
    public void FindReadsWhatIsRemembered_ButNeverWritesIt()
    {
        Log(_old, "dotnet build tests/AOBMaker.Core.Tests -v q -nologo");

        SdkTempLeftovers.Find(_temp, _repo, _now, Kept);

        Assert.False(File.Exists(Kept));
    }

    [Fact]
    public void AMemoryThatCannotBeRead_IsNoMemory()
    {
        Directory.CreateDirectory(Kept);   // a folder where the file should be
        var log = Log(_old, OurBuild);

        SdkTempLeftovers.Sweep(_temp, _repo, _now, Kept);

        Assert.False(File.Exists(log));
    }

    // ---- fixtures: the shapes measured in %TEMP% on 2026-11-11 ----------------------------------------------------

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

    private string Folder(string name, DateTime createdUtc)
    {
        var dir = Directory.CreateDirectory(Path.Combine(_temp, name)).FullName;
        Directory.SetCreationTimeUtc(dir, createdUtc);
        Directory.SetLastWriteTimeUtc(dir, createdUtc);
        return dir;
    }
}
