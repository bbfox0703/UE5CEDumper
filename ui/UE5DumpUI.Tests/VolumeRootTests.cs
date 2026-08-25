using System;
using System.IO;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// [VOLUMEROOT-2026-08-19] — the volume a path actually lives on, and the two numeric rules
/// that go with asking Win32 about it.
///
/// <para>⚠ <b>What these can and cannot settle.</b> The defect only becomes VISIBLE on a real
/// mount point, and no test can conjure one (it needs a spare volume and elevation). So these
/// pin the parts that are decidable here: that resolution goes through the filesystem rather
/// than through string surgery, that the root is shaped the way every Win32 volume API demands,
/// and that the <c>ulong</c> → <c>long</c> narrowing cannot flip a huge volume negative. The
/// mount-point behaviour itself is recorded as owed in <c>docs/todo.md</c>, not claimed here.</para>
/// </summary>
public class VolumeRootTests
{
    // ---- ClampToInt64 -------------------------------------------------------------
    // The trap the rewrite introduced and this removes: Win32 reports ULONGLONG, the callers
    // are long, and a plain cast wraps NEGATIVE. A negative free-space reading does not read
    // as "unknown" to the snapshot guard — it reads as catastrophically full, so the guard
    // would refuse to write on the LARGEST volumes. Saturating fails in the harmless direction.

    [Fact]
    public void ClampToInt64_LeavesOrdinarySizesExactlyAlone()
    {
        Assert.Equal(0L, VolumeRoot.ClampToInt64(0));
        Assert.Equal(1L, VolumeRoot.ClampToInt64(1));
        // 4 TB, an ordinary drive — must be byte-exact, not merely "positive".
        Assert.Equal(4_000_000_000_000L, VolumeRoot.ClampToInt64(4_000_000_000_000UL));
        Assert.Equal(long.MaxValue, VolumeRoot.ClampToInt64((ulong)long.MaxValue));
    }

    [Fact]
    public void ClampToInt64_SaturatesInsteadOfWrappingNegative()
    {
        // One past the boundary is the whole point: this is the smallest input a plain cast
        // gets wrong, and it gets it wrong by the maximum possible amount.
        Assert.Equal(long.MaxValue, VolumeRoot.ClampToInt64((ulong)long.MaxValue + 1));
        Assert.Equal(long.MaxValue, VolumeRoot.ClampToInt64(ulong.MaxValue));
    }

    [Fact]
    public void ClampToInt64_TheNaivePlainCastIsShownToBeWrong()
    {
        // Negative control for the test above: without the clamp these are catastrophic, and
        // ulong.MaxValue in particular becomes -1 — "the disk is minus one byte free".
        unchecked
        {
            Assert.True((long)((ulong)long.MaxValue + 1) < 0);
            Assert.Equal(-1L, (long)ulong.MaxValue);
        }
        // And the clamp disagrees with the cast on exactly those inputs, not on the ordinary ones.
        Assert.NotEqual(unchecked((long)ulong.MaxValue), VolumeRoot.ClampToInt64(ulong.MaxValue));
        Assert.Equal(unchecked((long)123456UL), VolumeRoot.ClampToInt64(123456UL));
    }

    // ---- EnsureTrailingSeparator --------------------------------------------------
    // GetDriveTypeW / GetVolumeInformationW / SHQueryRecycleBinW all require it and misbehave
    // without it. GetVolumePathNameW supplies it, so this is belt-and-braces on that route —
    // but load-bearing for a UNC root, where Path.GetPathRoot returns no trailing separator.

    [Fact]
    public void EnsureTrailingSeparator_AddsOneOnlyWhenItIsMissing()
    {
        Assert.Equal(@"C:\", VolumeRoot.EnsureTrailingSeparator(@"C:\"));
        Assert.Equal(@"C:\", VolumeRoot.EnsureTrailingSeparator(@"C:"));
        Assert.Equal(@"C:\Mount\Games\", VolumeRoot.EnsureTrailingSeparator(@"C:\Mount\Games"));
        Assert.Equal(@"C:\Mount\Games\", VolumeRoot.EnsureTrailingSeparator(@"C:\Mount\Games\"));
        // Never doubles up — a "C:\\" root would make the Win32 calls fail.
        Assert.Equal(@"C:\", VolumeRoot.EnsureTrailingSeparator(
            VolumeRoot.EnsureTrailingSeparator(@"C:")));
    }

    [Fact]
    public void EnsureTrailingSeparator_TheUncCaseIsWhyThisExists()
    {
        // This is not hypothetical shaping: Path.GetPathRoot really does hand back a UNC root
        // with no trailing separator, which is the input that would reach Win32 unfixed.
        string uncRoot = Path.GetPathRoot(@"\\server\share\dir\file.log")!;
        Assert.False(uncRoot.EndsWith(Path.DirectorySeparatorChar));
        Assert.EndsWith(@"\", VolumeRoot.EnsureTrailingSeparator(uncRoot));
    }

    [Fact]
    public void EnsureTrailingSeparator_AcceptsAnAltSeparatorAsAlreadyTerminated()
    {
        // Appending a backslash after a forward slash would produce "C:/\", which is worse
        // than either. Win32 accepts a forward slash here.
        Assert.Equal("C:/", VolumeRoot.EnsureTrailingSeparator("C:/"));
        Assert.Equal(string.Empty, VolumeRoot.EnsureTrailingSeparator(string.Empty));
    }

    // ---- Resolve ------------------------------------------------------------------

    [Fact]
    public void Resolve_ReturnsAMountRootShapedForWin32ForARealLocalPath()
    {
        // A path that certainly exists on this machine, and one that certainly does not yet —
        // the second matters because the snapshot guard asks about a DB before creating it.
        string existing = Path.GetTempPath();
        string notYet = Path.Combine(existing, "ue5-volumeroot-does-not-exist", "snap.db");

        foreach (string p in new[] { existing, notYet })
        {
            string? root = VolumeRoot.Resolve(p);
            Assert.NotNull(root);
            Assert.NotEqual(string.Empty, root);
            Assert.EndsWith(@"\", root);
            // The resolved root must be a PREFIX of the full path — that is what makes it the
            // mount point of the volume holding it rather than an unrelated volume.
            Assert.StartsWith(root, Path.GetFullPath(p), StringComparison.OrdinalIgnoreCase);
        }
    }

    [Fact]
    public void Resolve_AgreesWithGetPathRootOnAnOrdinaryDriveLetterPath()
    {
        // The regression control. On a NON-mounted path the new resolution must produce the
        // same volume the old Path.GetPathRoot code did, or this fix would have changed the
        // numbers everywhere instead of only on mount points. (On a mounted path they differ
        // — that is the fix — but this machine has no mount point to demonstrate it.)
        string p = Path.GetFullPath(Path.GetTempPath());
        Assert.Equal(Path.GetPathRoot(p), VolumeRoot.Resolve(p));
    }

    // ---- the defect itself, demonstrated ------------------------------------------
    //
    // ⭐ [VOLUMEROOT] was filed as "only a real mount point can verify it", which is why it sat
    // deferred. That turned out to be false, and the correction is the useful part: a plain
    // CROSS-VOLUME DIRECTORY JUNCTION separates the two answers just as well, needs no spare
    // volume and no elevation, and `mklink /J` is unprivileged. Measured 2026-08-21 —
    // GetVolumePathNameW resolves THROUGH a junction to the target's volume, while
    // Path.GetPathRoot only ever reads the leading drive letter of the string it was handed.
    //
    // What this does NOT claim: a junction is not literally a mounted volume. But the code
    // under test contains no branch that could tell them apart — it asks Win32 one question
    // and uses the answer — so the distinction is not one the fix can be wrong about.

    /// <summary>Fixed NTFS volume other than the one holding <paramref name="notThis"/>, or null.</summary>
    private static DriveInfo? OtherFixedVolume(string notThis)
    {
        string? mine = Path.GetPathRoot(Path.GetFullPath(notThis));
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (!d.IsReady || d.DriveType != DriveType.Fixed) continue;
                if (!string.Equals(d.DriveFormat, "NTFS", StringComparison.OrdinalIgnoreCase)) continue;
                if (string.Equals(d.Name, mine, StringComparison.OrdinalIgnoreCase)) continue;
                return d;
            }
            catch { /* an unreadable drive is simply not a candidate */ }
        }
        return null;
    }

    [Fact]
    public void Resolve_FollowsACrossVolumeJunctionWhereGetPathRootCannot()
    {
        string tempRoot = Path.GetPathRoot(Path.GetFullPath(Path.GetTempPath()))!;
        DriveInfo? other = OtherFixedVolume(Path.GetTempPath());
        // One-volume machine (a plausible CI runner): nothing to demonstrate, and pretending
        // otherwise would be worse than skipping.
        if (other is null) return;

        string junction = Path.Combine(Path.GetTempPath(), "ue5-volroot-" + Guid.NewGuid().ToString("N"));
        if (!TryMakeJunction(junction, other.RootDirectory.FullName)) return;
        try
        {
            string inside = Path.Combine(junction, "probe.dat");

            // The old code's answer: the junction's OWN volume, because it is string surgery.
            Assert.Equal(tempRoot, Path.GetPathRoot(inside));
            // The new code's answer: the volume the path actually reaches.
            Assert.Equal(other.Name, VolumeRoot.Resolve(inside));
            // ⭐ And they differ. Without this the test would pass on a machine where the two
            // volumes happen to coincide, proving nothing.
            Assert.NotEqual(Path.GetPathRoot(inside), VolumeRoot.Resolve(inside));

            var svc = new WindowsPlatformService();
            Assert.Equal(other.TotalSize, svc.GetTotalDiskSpaceBytes(inside));
            long delta = Math.Abs(other.AvailableFreeSpace - svc.GetFreeDiskSpaceBytes(inside));
            Assert.True(delta < 512L * 1024 * 1024, $"free-space delta {delta} bytes");

            // The negative control, and the whole point: the code this replaced reported the
            // host volume's size here. Only assert it when the two volumes are genuinely
            // different sizes — otherwise the old code would have been accidentally right.
            var host = new DriveInfo(tempRoot);
            if (host.TotalSize != other.TotalSize)
                Assert.NotEqual(host.TotalSize, svc.GetTotalDiskSpaceBytes(inside));
        }
        finally
        {
            RemoveJunction(junction);
        }
    }

    private static bool TryMakeJunction(string link, string target)
    {
        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo("cmd.exe")
            {
                // /J is a directory junction: unprivileged, unlike a symlink.
                Arguments = $"/c mklink /J \"{link}\" \"{target}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var p = System.Diagnostics.Process.Start(psi);
            if (p is null) return false;
            p.WaitForExit(20_000);
            return p.HasExited && p.ExitCode == 0 && Directory.Exists(link);
        }
        catch { return false; }
    }

    private static void RemoveJunction(string link)
    {
        // ⚠⚠ NON-RECURSIVE, and only after confirming this really is a reparse point. The
        // target here is another volume's ROOT; a recursive delete through the junction would
        // erase that volume. RemoveDirectory on a junction unlinks it and leaves the target
        // untouched, which is precisely why the recursive overload must never appear here.
        try
        {
            if (!Directory.Exists(link)) return;
            if (!File.GetAttributes(link).HasFlag(FileAttributes.ReparsePoint)) return;
            Directory.Delete(link);
        }
        catch { /* leaked into %TEMP%; harmless and self-evident */ }
    }

    // ---- the numbers the rewrite must not have moved --------------------------------

    [Fact]
    public void DiskSpace_MatchesTheDriveInfoNumbersItReplacedOnANonMountedPath()
    {
        // The regression control for the GetDiskFreeSpaceExW rewrite, and the reason it is
        // safe: lpFreeBytesAvailableToCaller / lpTotalNumberOfBytes are exactly what
        // DriveInfo.AvailableFreeSpace / .TotalSize wrap, so on a path with no mount point in
        // it the two must agree. Measured rather than asserted from the docs.
        string probe = Path.GetFullPath(Path.GetTempPath());
        string? resolved = VolumeRoot.Resolve(probe);
        Assert.NotNull(resolved);

        // ⚠ Skip the comparison if this machine's temp DOES sit on a mount point: there the
        // two are SUPPOSED to differ, and asserting equality would pin the defect instead of
        // the fix. (Not the case on the maintainer's machines, hence the assert below runs.)
        if (!string.Equals(resolved, Path.GetPathRoot(probe), StringComparison.OrdinalIgnoreCase))
            return;

        var svc = new WindowsPlatformService();
        var di = new DriveInfo(resolved!);
        Assert.True(di.IsReady);
        Assert.Equal(di.TotalSize, svc.GetTotalDiskSpaceBytes(probe));
        // Free space moves under us between the two reads, so compare within a tolerance
        // wide enough for ordinary churn and far narrower than a wrong-volume answer, which
        // would be off by whole terabytes.
        long delta = Math.Abs(di.AvailableFreeSpace - svc.GetFreeDiskSpaceBytes(probe));
        Assert.True(delta < 512L * 1024 * 1024, $"free-space delta {delta} bytes");
    }

    [Fact]
    public void DiskSpace_FailsOpenInOppositeDirectionsAndThatAsymmetryIsDeliberate()
    {
        // Free returns MaxValue so the snapshot guard does not block on a measurement it
        // could not make; total returns 0 so the percentage term collapses instead of
        // dividing by a guess. Swapping them would make an unmeasurable volume look full.
        var svc = new WindowsPlatformService();
        Assert.Equal(long.MaxValue, svc.GetFreeDiskSpaceBytes(""));
        Assert.Equal(0L, svc.GetTotalDiskSpaceBytes(""));
    }

    [Fact]
    public void LogCompression_IsSupported_StillAnswersNtfsForTheRealLogVolume()
    {
        // GetVolumeInformationW replaced DriveInfo.DriveFormat; the answer for an ordinary
        // local NTFS path must be unchanged. Cross-checked against DriveInfo so this cannot
        // pass by returning false for the wrong reason.
        string probe = Path.GetFullPath(Path.GetTempPath());
        string? resolved = VolumeRoot.Resolve(probe);
        Assert.NotNull(resolved);
        if (!string.Equals(resolved, Path.GetPathRoot(probe), StringComparison.OrdinalIgnoreCase))
            return;   // mount point — see the note above

        bool ntfsPerBcl = string.Equals(new DriveInfo(resolved!).DriveFormat, "NTFS",
                                        StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ntfsPerBcl, new WindowsLogCompressionService().IsSupported(probe));
    }

    [Fact]
    public void Resolve_ReturnsNullRatherThanGuessingWhenThereIsNothingToResolve()
    {
        // ⚠ Null must NOT be "fall back to the host volume". Every caller turns it into its
        // own fail-open sentinel; substituting Path.GetPathRoot here would quietly restore
        // the defect on exactly the paths where resolution is hardest.
        Assert.Null(VolumeRoot.Resolve(""));
        Assert.Null(VolumeRoot.Resolve("   "));
        Assert.Null(VolumeRoot.Resolve(null!));
        // Invalid characters make GetFullPath throw; that is caught, not propagated.
        Assert.Null(VolumeRoot.Resolve("C:\\bad\0path"));
    }
}
