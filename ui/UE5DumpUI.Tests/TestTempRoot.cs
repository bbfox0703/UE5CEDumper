using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;

namespace UE5DumpUI.Tests;

/// <summary>
/// [TEST-TEMP-HYGIENE-2026-11-11] Every run of this suite writes its scratch under one folder of its own, and none of
/// it outlives the run.
///
/// The suite used to leave about 220 folders straight in %TEMP% per run (2,743 there on 2026-11-11, ~120 MB in a day):
/// each test class deleted its folder in Dispose and swallowed the failure, and the delete fails whenever
/// Microsoft.Data.Sqlite's connection pool still holds the .db open. Fixing each Dispose would leave the next new test
/// free to leak again, so the process's TEMP itself is moved: GetTempPath reads TMP / TEMP on every call, so every test,
/// library and child process that asks for a temp folder gets this run's, with no test written for it.
///
/// The run's folder goes when the process exits. A run that could not clean up -- killed, or a file still held -- is
/// deleted by the next run: its owner file is held open with no sharing for the life of the process, so the OS lets go
/// of it the moment the owner dies, whatever its PID is reused for. The shared folder %TEMP%\UE5CEDumper.Tests\ itself
/// stays, holding SdkTempLeftovers' memory: a run that removed it when empty could do so between another run's look at
/// it and that run making its folder in it, and that run's TEMP would never move.
/// </summary>
internal static class TestTempRoot
{
    internal const string RootName = SdkTempLeftovers.SharedFolderName;
    internal const string OwnerFileName = ".owner";

    // A run folder with no owner file is a run starting (between creating the folder and opening the file), or one
    // whose exit removed the file and not the rest. Only the second may go, and a start takes milliseconds.
    internal static readonly TimeSpan OwnerlessMinAge = TimeSpan.FromMinutes(10);

    // Builds before this one wrote straight into %TEMP%. A folder of theirs lives for seconds, so one an hour old is
    // nobody's -- even with a run of an older checkout going on beside this one.
    internal static readonly TimeSpan LegacyMinAge = TimeSpan.FromHours(1);

    // What those builds left: a test class's prefix and a {Guid:N} (one test's folder), or a fixed parent that every
    // run of such a build shares, its tests' folders inside. Frozen: a test written since writes under the run's folder,
    // whatever name it picks.
    private static readonly Regex LegacyName = new(
        "^(?:UE5Dump(?:AobRows|ArrPivot|DiscTest|GroupTest|PivotTest|PivotVm|SnapTest|SnapVm|SpcGroupTest"
        + "|SpcGroupVm|SpcTest|SpcVm)_|ue5cd-vmtest-)[0-9a-f]{32}$",
        RegexOptions.CultureInvariant);

    private static readonly Regex LegacyParentName = new(
        "^ue5-(?:backstop|ours-present|proxy-conc|qmark|qmark-pose|unreadable)$", RegexOptions.CultureInvariant);

    private static FileStream? s_owner;   // a static, so nothing collects it before the process ends

    /// <summary>%TEMP% as the process found it.</summary>
    internal static string SystemTemp { get; private set; } = "";

    /// <summary>This run's folder, and the process's TMP / TEMP; "" when it could not be made, which
    /// TestTempRootTests then fails on rather than letting the suite write into %TEMP% unnoticed.</summary>
    internal static string RunDir { get; private set; } = "";

    [ModuleInitializer]
    internal static void Install()
    {
        try
        {
            SystemTemp = Path.GetTempPath();
            var root = Path.Combine(SystemTemp, RootName);
            var now = DateTime.UtcNow;
            Housekeeping(() => SweepDeadRuns(root, now));
            Housekeeping(() => SweepLegacy(SystemTemp, now));
            Housekeeping(() =>
            {
                if (SdkTempLeftovers.FindRepoRoot() is { } repo) SdkTempLeftovers.Sweep(SystemTemp, repo, now, root);
            });

            var run = Path.Combine(root, $"{Environment.ProcessId}-{now:yyyyMMddHHmmss}");
            Directory.CreateDirectory(run);
            s_owner = new FileStream(Path.Combine(run, OwnerFileName), FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None);
            Environment.SetEnvironmentVariable("TMP", run);
            Environment.SetEnvironmentVariable("TEMP", run);
            RunDir = run;
            AppDomain.CurrentDomain.ProcessExit += (_, _) =>
            {
                s_owner?.Dispose();
                s_owner = null;
                RemoveRun(run);
            };
        }
        catch (Exception e) when (Expected(e))
        {
            // The suite still runs; the pin tests say why its scratch is not where it should be.
        }
    }

    // Each sweep promises not to throw. This holds the promise for them in the one place a throw costs the whole run:
    // an exception out of a module initializer stops the test process before any test, and TEMP never moves.
    private static void Housekeeping(Action sweep)
    {
        try { sweep(); }
        catch (Exception e) when (Expected(e)) { }
    }

    // What a look at %TEMP% can meet: I/O and access, and a time or path .NET cannot represent (anyone can stamp a
    // folder with a time past the year 9999, and reading it throws).
    private static bool Expected(Exception e)
        => e is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException
            or System.Security.SecurityException;

    /// <summary>Deletes every run folder under <paramref name="root"/> whose owner is gone; returns those deleted.
    /// Never throws: the sweeps are housekeeping, and one that threw would stop Install before TMP / TEMP move.</summary>
    internal static List<string> SweepDeadRuns(string root, DateTime nowUtc)
    {
        var deleted = new List<string>();
        try
        {
            // A root that is a junction (test scratch moved to another drive) holds folders that are not ours to judge.
            if (!Directory.Exists(root) || File.GetAttributes(root).HasFlag(FileAttributes.ReparsePoint)) return deleted;
            foreach (var dir in Directory.EnumerateDirectories(root))
            {
                try
                {
                    if (OwnerGone(dir, nowUtc) && TryDelete(dir)) deleted.Add(dir);
                }
                catch (Exception e) when (Expected(e)) { }   // that one entry, not the walk
            }
        }
        catch (Exception e) when (Expected(e))
        {
            // Gone from under the walk, or unreadable: nothing more to judge this time.
        }
        return deleted;
    }

    /// <summary>Deletes, in <paramref name="temp"/>, what an older build of the suite left there and has not changed
    /// for <see cref="LegacyMinAge"/>; returns what it deleted. A test's own folder goes whole. A shared parent loses
    /// only its children that old -- a child made a moment ago is an older checkout's run in progress, and the parent's
    /// time read a moment before says nothing of it -- and goes itself only once empty. Never throws, as
    /// SweepDeadRuns.</summary>
    internal static List<string> SweepLegacy(string temp, DateTime nowUtc)
    {
        var deleted = new List<string>();
        try
        {
            foreach (var dir in new DirectoryInfo(temp).EnumerateDirectories())
            {
                try
                {
                    SweepLegacyEntry(dir, nowUtc, deleted);
                }
                catch (Exception e) when (Expected(e)) { }   // that one entry, not the walk
            }
        }
        catch (Exception e) when (Expected(e)) { }
        return deleted;
    }

    private static void SweepLegacyEntry(DirectoryInfo dir, DateTime nowUtc, List<string> deleted)
    {
        if (LegacyName.IsMatch(dir.Name))
        {
            if (nowUtc - dir.LastWriteTimeUtc >= LegacyMinAge && TryDelete(dir.FullName)) deleted.Add(dir.FullName);
            return;
        }
        if (!LegacyParentName.IsMatch(dir.Name) || dir.Attributes.HasFlag(FileAttributes.ReparsePoint)) return;

        // The parent's time as listed, before this sweep empties it: an hour old, no child came since.
        var parentIdle = nowUtc - dir.LastWriteTimeUtc >= LegacyMinAge;
        foreach (var child in dir.EnumerateDirectories())
        {
            try
            {
                var touched = child.LastWriteTimeUtc > child.CreationTimeUtc ? child.LastWriteTimeUtc : child.CreationTimeUtc;
                if (nowUtc - touched >= LegacyMinAge && TryDelete(child.FullName)) deleted.Add(child.FullName);
            }
            catch (Exception e) when (Expected(e)) { }   // that one child
        }
        if (!parentIdle) return;
        try
        {
            Directory.Delete(dir.FullName, recursive: false);   // only when empty
            deleted.Add(dir.FullName);
        }
        catch (Exception e) when (Expected(e)) { }
    }

    private static bool OwnerGone(string dir, DateTime nowUtc)
    {
        var owner = Path.Combine(dir, OwnerFileName);
        // A folder deleted between the listing and here reads as created in 1601: TryDelete then finds nothing to do.
        if (!File.Exists(owner)) return nowUtc - Directory.GetCreationTimeUtc(dir) >= OwnerlessMinAge;
        try
        {
            using var probe = new FileStream(owner, FileMode.Open, FileAccess.ReadWrite, FileShare.None);
            return true;
        }
        catch (IOException) { return false; }                 // held: its run is alive
        catch (UnauthorizedAccessException) { return false; }
    }

    private static bool TryDelete(string dir)
    {
        // Never a reparse point itself: a junction named like ours points at something that is not ours.
        try
        {
            if (File.GetAttributes(dir).HasFlag(FileAttributes.ReparsePoint)) return false;
        }
        catch (FileNotFoundException) { return true; }
        catch (DirectoryNotFoundException) { return true; }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }

        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                return true;
            }
            catch (DirectoryNotFoundException) { return true; }   // another run's sweep took it first
            catch (Exception e) when (attempt == 0 && e is IOException or UnauthorizedAccessException)
            {
                // A read-only file refuses the delete with UnauthorizedAccessException, a read-only folder with an
                // IOException; a test may have set either. Clear them once and try again.
                try { ClearReadOnly(dir); }
                catch (Exception e2) when (Expected(e2)) { return false; }
            }
            catch (Exception e) when (Expected(e)) { return false; }
        }
        return false;
    }

    /// <summary>Clears read-only on every file and folder under <paramref name="dir"/> and never enters a reparse
    /// point: a junction in a run's folder may point at another volume's ROOT (VolumeRootTests makes one). Measured: a
    /// walk with SearchOption.AllDirectories goes through a junction and changes the files it points at.</summary>
    internal static void ClearReadOnly(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir))
            if (!File.GetAttributes(f).HasFlag(FileAttributes.ReparsePoint))
                File.SetAttributes(f, FileAttributes.Normal);
        foreach (var d in new DirectoryInfo(dir).EnumerateDirectories())
        {
            if (d.Attributes.HasFlag(FileAttributes.ReparsePoint)) continue;
            if (d.Attributes.HasFlag(FileAttributes.ReadOnly)) d.Attributes &= ~FileAttributes.ReadOnly;
            ClearReadOnly(d.FullName);
        }
    }

    internal static void RemoveRun(string run)
    {
        // The pool keeps a closed connection's .db open; nothing else stops the folder going.
        SqliteConnection.ClearAllPools();
        TryDelete(run);
    }
}
