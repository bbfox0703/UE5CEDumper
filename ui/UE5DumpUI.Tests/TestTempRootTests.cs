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
        Directory.CreateDirectory(Path.Combine(old, "UE5CEDumper", "Snapshots"));
        Directory.SetLastWriteTimeUtc(old, now - TestTempRoot.LegacyMinAge - TimeSpan.FromMinutes(1));
        var youngTemp = Path.Combine(_sandbox, "young");
        var young = Path.Combine(youngTemp, name);
        Directory.CreateDirectory(young);

        Assert.Equal(new[] { old }, TestTempRoot.SweepLegacy(oldTemp, now));
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
        Assert.True(MakeJunction(Path.Combine(dead, "link"), target));

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
        Assert.True(MakeJunction(link, target));

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
        Assert.True(MakeJunction(link, target));
        Directory.SetLastWriteTimeUtc(link, now - TimeSpan.FromDays(30));

        Assert.Empty(TestTempRoot.SweepLegacy(temp, now));
        Assert.True(Directory.Exists(link));
        Assert.True(File.Exists(Path.Combine(target, "precious.txt")));
        Directory.Delete(link);   // unlinks; the target stays
    }

    // `mklink /J` as VolumeRootTests makes its junction: unprivileged, unlike a symlink.
    private static bool MakeJunction(string link, string target)
    {
        var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe")
        {
            Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        using var p = System.Diagnostics.Process.Start(psi);
        if (p is null) return false;
        p.WaitForExit(20_000);
        return p.HasExited && p.ExitCode == 0
            && File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint);
    }

    private static string MakeRun(string root, string name, bool ownerFile)
    {
        var dir = Directory.CreateDirectory(Path.Combine(root, name)).FullName;
        if (ownerFile) File.WriteAllText(Path.Combine(dir, TestTempRoot.OwnerFileName), "");
        return dir;
    }
}
