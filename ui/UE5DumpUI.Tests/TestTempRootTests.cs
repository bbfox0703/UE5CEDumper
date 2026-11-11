using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [TEST-TEMP-HYGIENE-2026-11-11] The suite's scratch lives in one folder per run, and no run leaves any in %TEMP%
/// (<see cref="TestTempRoot"/>). Before this, one run of the suite left about 220 folders there and nothing removed them.
/// </summary>
public class TestTempRootTests : IDisposable
{
    private const string Guid32 = "0123456789abcdef0123456789abcdef";

    // Each test's fake %TEMP% and run root, inside this run's own folder.
    private readonly string _sandbox = Path.Combine(Path.GetTempPath(), "TestTempRootTests_" + Guid.NewGuid().ToString("N"));

    public TestTempRootTests() => Directory.CreateDirectory(_sandbox);

    public void Dispose()
    {
        if (!Directory.Exists(_sandbox)) return;
        TestTempRoot.ClearReadOnly(_sandbox);
        Directory.Delete(_sandbox, recursive: true);
    }

    [Fact]
    public void ThisRunsTempIsItsOwnFolderUnderTheSharedRoot()
    {
        Assert.False(string.IsNullOrEmpty(TestTempRoot.RunDir));
        var root = Path.Combine(TestTempRoot.SystemTemp, TestTempRoot.RootName);
        Assert.Equal(root, Path.GetDirectoryName(TestTempRoot.RunDir), ignoreCase: true);
        Assert.Equal(TestTempRoot.RunDir, Path.TrimEndingDirectorySeparator(Path.GetTempPath()), ignoreCase: true);
        // A child process a test starts inherits both, so its scratch lands here too.
        Assert.Equal(TestTempRoot.RunDir, Environment.GetEnvironmentVariable("TMP"));
        Assert.Equal(TestTempRoot.RunDir, Environment.GetEnvironmentVariable("TEMP"));
    }

    [Fact]
    public void ThisRunsOwnerFileIsHeldWithNoSharing()
    {
        var owner = Path.Combine(TestTempRoot.RunDir, TestTempRoot.OwnerFileName);
        Assert.True(File.Exists(owner));
        Assert.Throws<IOException>(() =>
            new FileStream(owner, FileMode.Open, FileAccess.ReadWrite, FileShare.None).Dispose());
    }

    [Fact]
    public void ADeadRunIsDeleted_ALiveRunAndOneStartingAreKept()
    {
        var root = Path.Combine(_sandbox, TestTempRoot.RootName);
        var now = DateTime.UtcNow;

        var dead = MakeRun(root, "dead", ownerFile: true);
        File.WriteAllText(Path.Combine(dead, "snapshots.G.db"), "x");
        var deadReadOnly = MakeRun(root, "dead-readonly", ownerFile: true);
        var ro = Path.Combine(deadReadOnly, "ro.txt");
        File.WriteAllText(ro, "x");
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        var live = MakeRun(root, "live", ownerFile: true);
        using var held = new FileStream(Path.Combine(live, TestTempRoot.OwnerFileName),
            FileMode.Open, FileAccess.ReadWrite, FileShare.None);
        // Created a moment ago and its owner file not open yet: a run that is starting.
        var starting = MakeRun(root, "starting", ownerFile: false);
        // Its exit removed the owner file but not the rest, long enough ago.
        var leftover = MakeRun(root, "leftover", ownerFile: false);
        Directory.CreateDirectory(Path.Combine(leftover, "UE5CEDumper", "Snapshots"));
        Directory.SetCreationTimeUtc(leftover, now - TestTempRoot.OwnerlessMinAge - TimeSpan.FromMinutes(1));

        var deleted = TestTempRoot.SweepDeadRuns(root, now);

        Assert.False(Directory.Exists(dead));
        Assert.False(Directory.Exists(deadReadOnly));
        Assert.False(Directory.Exists(leftover));
        Assert.True(Directory.Exists(live));
        Assert.True(Directory.Exists(starting));
        Assert.Equal(3, deleted.Count);
    }

    [Fact]
    public void ARootThatDoesNotExistIsNothingToSweep()
        => Assert.Empty(TestTempRoot.SweepDeadRuns(Path.Combine(_sandbox, "absent"), DateTime.UtcNow));

    /// <summary>A sweep is housekeeping: a %TEMP% that is not there, or a folder gone from under the walk (another
    /// cleanup), must not stop this run from taking a folder of its own.</summary>
    [Fact]
    public void ASweepWhoseFolderIsGoneThrowsNothing()
        => Assert.Empty(TestTempRoot.SweepLegacy(Path.Combine(_sandbox, "absent"), DateTime.UtcNow));

    /// <summary>Every name an older build of the suite left straight in %TEMP% (counted there on 2026-11-11).</summary>
    public static TheoryData<string> LegacyNames => new()
    {
        "UE5DumpAobRows_" + Guid32, "UE5DumpArrPivot_" + Guid32, "UE5DumpDiscTest_" + Guid32,
        "UE5DumpGroupTest_" + Guid32, "UE5DumpPivotTest_" + Guid32, "UE5DumpPivotVm_" + Guid32,
        "UE5DumpSnapTest_" + Guid32, "UE5DumpSnapVm_" + Guid32, "UE5DumpSpcGroupTest_" + Guid32,
        "UE5DumpSpcGroupVm_" + Guid32, "UE5DumpSpcTest_" + Guid32, "UE5DumpSpcVm_" + Guid32,
        "ue5cd-vmtest-" + Guid32,
        "ue5-backstop", "ue5-ours-present", "ue5-proxy-conc", "ue5-qmark", "ue5-qmark-pose", "ue5-unreadable",
    };

    [Theory]
    [MemberData(nameof(LegacyNames))]
    public void ALegacyFolderAnHourOldIsDeleted_AYoungOneIsKept(string name)
    {
        var now = DateTime.UtcNow;
        var oldTemp = Path.Combine(_sandbox, "old");
        var old = Path.Combine(oldTemp, name);
        var child = Directory.CreateDirectory(Path.Combine(old, "UE5CEDumper", "Snapshots")).Parent!.FullName;
        var aged = now - TestTempRoot.LegacyMinAge - TimeSpan.FromMinutes(1);
        Directory.SetCreationTimeUtc(child, aged);   // a shared parent's child is judged by its own age
        Directory.SetLastWriteTimeUtc(child, aged);
        Directory.SetLastWriteTimeUtc(old, aged);
        var youngTemp = Path.Combine(_sandbox, "young");
        var young = Path.Combine(youngTemp, name);
        Directory.CreateDirectory(young);

        Assert.Contains(old, TestTempRoot.SweepLegacy(oldTemp, now));
        Assert.False(Directory.Exists(old));
        Assert.Empty(TestTempRoot.SweepLegacy(youngTemp, now));
        Assert.True(Directory.Exists(young));
    }

    [Fact]
    public void OnlyThoseNamesAreLegacy_AndOnlyFolders()
    {
        var now = DateTime.UtcNow;
        var temp = Path.Combine(_sandbox, "temp");
        string[] notOurs =
        {
            "UE5DumpSnapTest_" + Guid32 + "0", "UE5DumpSnapTest_" + Guid32.ToUpperInvariant(), "UE5DumpSnapTest_x",
            "UE5DumpOther_" + Guid32, "UE5DumpUI", "UE5CEDumper", "ue5-qmark2", "xue5-qmark", "ue5cd-vmtest-",
        };
        foreach (var n in notOurs)
        {
            var d = Directory.CreateDirectory(Path.Combine(temp, n)).FullName;
            Directory.SetLastWriteTimeUtc(d, now - TimeSpan.FromDays(30));
        }
        var file = Path.Combine(temp, "UE5DumpSnapVm_" + Guid32);
        File.WriteAllText(file, "a file, not a folder");
        File.SetLastWriteTimeUtc(file, now - TimeSpan.FromDays(30));

        Assert.Empty(TestTempRoot.SweepLegacy(temp, now));
        foreach (var n in notOurs) Assert.True(Directory.Exists(Path.Combine(temp, n)), n);
        Assert.True(File.Exists(file));
    }

    /// <summary>A sweep never reaches through a junction: VolumeRootTests makes one to another volume's ROOT, and one it
    /// failed to remove lands in the run's folder. The read-only file fails the first delete, which has unlinked the
    /// junction by then (measured: Directory.Delete goes on past a file it cannot delete), so the second attempt's walk
    /// finds it gone; <see cref="TheAttributeWalkNeverEntersAJunction"/> holds that walk with the junction in place.</summary>
    [Fact]
    public void ADeadRunHoldingAJunctionGoes_AndWhatTheJunctionPointsAtIsUntouched()
    {
        var root = Path.Combine(_sandbox, TestTempRoot.RootName);
        var target = Directory.CreateDirectory(Path.Combine(_sandbox, "elsewhere")).FullName;
        var precious = Path.Combine(target, "precious.txt");
        File.WriteAllText(precious, "keep");
        File.SetAttributes(precious, FileAttributes.ReadOnly);
        var dead = MakeRun(root, "dead", ownerFile: true);
        var ro = Path.Combine(dead, "ro.txt");
        File.WriteAllText(ro, "x");
        File.SetAttributes(ro, FileAttributes.ReadOnly);
        Assert.True(TestJunction.Make(Path.Combine(dead, "link"), target));

        Assert.Single(TestTempRoot.SweepDeadRuns(root, DateTime.UtcNow));

        Assert.False(Directory.Exists(dead));
        Assert.True(File.Exists(precious));
        Assert.True(File.GetAttributes(precious).HasFlag(FileAttributes.ReadOnly));
    }

    [Fact]
    public void TheAttributeWalkNeverEntersAJunction()
    {
        var target = Directory.CreateDirectory(Path.Combine(_sandbox, "elsewhere")).FullName;
        var precious = Path.Combine(target, "precious.txt");
        File.WriteAllText(precious, "keep");
        File.SetAttributes(precious, FileAttributes.ReadOnly);
        var run = Directory.CreateDirectory(Path.Combine(_sandbox, "run", "sub")).Parent!.FullName;
        var ours = Path.Combine(run, "sub", "ro.txt");
        File.WriteAllText(ours, "x");
        File.SetAttributes(ours, FileAttributes.ReadOnly);
        var link = Path.Combine(run, "link");
        Assert.True(TestJunction.Make(link, target));

        TestTempRoot.ClearReadOnly(run);

        Assert.False(File.GetAttributes(ours).HasFlag(FileAttributes.ReadOnly));
        Assert.True(File.GetAttributes(precious).HasFlag(FileAttributes.ReadOnly));
        Directory.Delete(link);   // unlinks; the target stays
    }

    [Fact]
    public void ALegacyNamedJunctionIsNeverTouched()
    {
        var now = DateTime.UtcNow;
        var temp = Directory.CreateDirectory(Path.Combine(_sandbox, "temp")).FullName;
        var target = Directory.CreateDirectory(Path.Combine(_sandbox, "elsewhere")).FullName;
        File.WriteAllText(Path.Combine(target, "precious.txt"), "keep");
        var link = Path.Combine(temp, "ue5-qmark");
        Assert.True(TestJunction.Make(link, target));
        Directory.SetLastWriteTimeUtc(link, now - TimeSpan.FromDays(30));

        Assert.Empty(TestTempRoot.SweepLegacy(temp, now));
        Assert.True(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "precious.txt")));
        Directory.Delete(link);   // unlinks; the target stays
    }

    /// <summary>A shared root that is itself a junction (someone moved test scratch to another drive) is not walked:
    /// the folders behind it are not ours to judge.</summary>
    [Fact]
    public void ARootThatIsAJunction_IsNotSwept()
    {
        var target = Directory.CreateDirectory(Path.Combine(_sandbox, "elsewhere")).FullName;
        var theirs = Directory.CreateDirectory(Path.Combine(target, "someone-elses")).FullName;
        Directory.SetCreationTimeUtc(theirs, DateTime.UtcNow - TimeSpan.FromDays(3));
        var root = Path.Combine(_sandbox, TestTempRoot.RootName);
        Assert.True(TestJunction.Make(root, target));

        var deleted = TestTempRoot.SweepDeadRuns(root, DateTime.UtcNow);

        Assert.Empty(deleted);
        Assert.True(Directory.Exists(theirs));
        Directory.Delete(root);   // unlinks; the target stays
    }

    /// <summary>A fixed parent such as ue5-qmark is shared by every run of an older checkout: a child made a moment ago
    /// is a run in progress, so only children an hour old go, and the parent only once it is empty.</summary>
    [Fact]
    public void ALegacyParent_LosesItsOldChildren_KeepsAYoungOne_AndGoesOnlyWhenEmpty()
    {
        var now = DateTime.UtcNow;
        var old = now - TestTempRoot.LegacyMinAge - TimeSpan.FromMinutes(1);
        var temp = Path.Combine(_sandbox, "temp");
        var parent = Path.Combine(temp, "ue5-qmark");
        var oldChild = Directory.CreateDirectory(Path.Combine(parent, Guid32, "UE5CEDumper")).Parent!.FullName;
        Directory.SetCreationTimeUtc(oldChild, old);
        Directory.SetLastWriteTimeUtc(oldChild, old);
        var youngChild = Directory.CreateDirectory(Path.Combine(parent, Guid32.Replace('0', '9'))).FullName;
        Directory.SetLastWriteTimeUtc(parent, old);   // as read before the young child arrived

        TestTempRoot.SweepLegacy(temp, now);

        Assert.False(Directory.Exists(oldChild));
        Assert.True(Directory.Exists(youngChild));
        Assert.True(Directory.Exists(parent));

        Directory.SetCreationTimeUtc(youngChild, old);
        Directory.SetLastWriteTimeUtc(youngChild, old);
        Directory.SetLastWriteTimeUtc(parent, old);
        TestTempRoot.SweepLegacy(temp, now);

        Assert.False(Directory.Exists(parent));
    }

    /// <summary>A run's exit: the pool lets go of the .db a closed connection still holds, the run's folder goes, and
    /// the shared root stays -- another run starting at that moment may be about to make its folder in it.</summary>
    [Fact]
    public void RemoveRun_ReleasesThePool_DeletesTheRun_AndKeepsTheSharedRoot()
    {
        // Short names: SQLite cannot open a database whose journal path passes MAX_PATH, and %TEMP% may be deep
        // (measured: a 114-character TEMP gave SQLite Error 14 with the run's folder names spelled out).
        var root = Directory.CreateDirectory(Path.Combine(_sandbox, "r")).FullName;
        var run = Directory.CreateDirectory(Path.Combine(root, "1")).FullName;
        var db = Path.Combine(run, "s.db");
        using (var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={db}"))
        {
            conn.Open();
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "CREATE TABLE t (x INTEGER)";
            cmd.ExecuteNonQuery();
        }

        TestTempRoot.RemoveRun(run);

        Assert.False(Directory.Exists(run));
        Assert.True(Directory.Exists(root));
    }

    /// <summary>Install does the housekeeping before TMP / TEMP move, and hands the run's folder to the process exit.
    /// It runs once per process, before any test, so this reads the source rather than calling it.</summary>
    [Fact]
    public void Install_SweepsFirst_ThenMovesTemp_AndRemovesTheRunAtExit()
    {
        var src = File.ReadAllText(Path.Combine(SdkTempLeftovers.FindRepoRoot()!, "ui", "UE5DumpUI.Tests", "TestTempRoot.cs"))
            .Replace("\r\n", "\n");
        var body = src[src.IndexOf("internal static void Install()", StringComparison.Ordinal)..];
        body = body[..body.IndexOf("\n    }\n", StringComparison.Ordinal)];
        var move = body.IndexOf("SetEnvironmentVariable(\"TMP\"", StringComparison.Ordinal);
        Assert.True(move > 0);
        foreach (var call in new[] { "SweepDeadRuns(", "SweepLegacy(", "SdkTempLeftovers.Sweep(" })
        {
            var at = body.IndexOf(call, StringComparison.Ordinal);
            Assert.True(at > 0 && at < move, call);
        }
        Assert.Contains("ProcessExit +=", body);
        Assert.Contains("RemoveRun(run)", body);
    }

    /// <summary>A run killed while a test held a read-only folder (AFolderThatWillNotGo makes one) leaves it in its
    /// run folder; the next sweep still removes that run, clearing the folder's flag as it does a file's.</summary>
    [Fact]
    public void ADeadRunHoldingAReadOnlyFolder_IsDeleted()
    {
        var root = Path.Combine(_sandbox, TestTempRoot.RootName);
        var dead = MakeRun(root, "dead", ownerFile: true);
        var stuck = Directory.CreateDirectory(Path.Combine(dead, "t", "abcdefgh.ijk")).FullName;
        new DirectoryInfo(stuck).Attributes |= FileAttributes.ReadOnly;

        var deleted = TestTempRoot.SweepDeadRuns(root, DateTime.UtcNow);

        Assert.Single(deleted);
        Assert.False(Directory.Exists(dead));
    }

    private static string MakeRun(string root, string name, bool ownerFile)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        if (ownerFile) File.WriteAllText(Path.Combine(dir, TestTempRoot.OwnerFileName), "");
        return dir;
    }
}
