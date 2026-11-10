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
/// of it the moment the owner dies, whatever its PID is reused for.
/// </summary>
internal static class TestTempRoot
{
    internal const string RootName = "UE5CEDumper.Tests";
    internal const string OwnerFileName = ".owner";

    // A run folder with no owner file is a run starting (between creating the folder and opening the file), or one
    // whose exit removed the file and not the rest. Only the second may go, and a start takes milliseconds.
    internal static readonly TimeSpan OwnerlessMinAge = TimeSpan.FromMinutes(10);

    // Builds before this one wrote straight into %TEMP%. A folder of theirs lives for seconds, so one an hour old is
    // nobody's -- even with a run of an older checkout going on beside this one.
    internal static readonly TimeSpan LegacyMinAge = TimeSpan.FromHours(1);

    // What those builds left: a test class's prefix and a {Guid:N}, or a fixed parent of GUID-named folders. Frozen:
    // a test written since writes under the run's folder, whatever name it picks.
    private static readonly Regex LegacyName = new(
        "^(?:(?:UE5Dump(?:AobRows|ArrPivot|DiscTest|GroupTest|PivotTest|PivotVm|SnapTest|SnapVm|SpcGroupTest"
        + "|SpcGroupVm|SpcTest|SpcVm)_|ue5cd-vmtest-)[0-9a-f]{32}"
        + "|ue5-(?:backstop|ours-present|proxy-conc|qmark|qmark-pose|unreadable))$",
        RegexOptions.CultureInvariant);

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
            SweepDeadRuns(root, now);
            SweepLegacy(SystemTemp, now);

            var run = Path.Combine(root, $"{Environment.ProcessId}-{now:yyyyMMddHHmmss}");
            Directory.CreateDirectory(run);
            s_owner = new FileStream(Path.Combine(run, OwnerFileName), FileMode.CreateNew, FileAccess.ReadWrite,
                FileShare.None);
            Environment.SetEnvironmentVariable("TMP", run);
            Environment.SetEnvironmentVariable("TEMP", run);
            RunDir = run;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => RemoveRun(root, run);
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            // The suite still runs; the pin tests say why its scratch is not where it should be.
        }
    }

    /// <summary>Deletes every run folder under <paramref name="root"/> whose owner is gone; returns those deleted.</summary>
    internal static List<string> SweepDeadRuns(string root, DateTime nowUtc)
    {
        var deleted = new List<string>();
        if (!Directory.Exists(root)) return deleted;
        foreach (var dir in Directory.EnumerateDirectories(root))
            if (OwnerGone(dir, nowUtc) && TryDelete(dir)) deleted.Add(dir);
        return deleted;
    }

    /// <summary>Deletes, in <paramref name="temp"/>, every folder an older build of the suite left there and that has
    /// not changed for <see cref="LegacyMinAge"/>; returns those deleted.</summary>
    internal static List<string> SweepLegacy(string temp, DateTime nowUtc)
    {
        var deleted = new List<string>();
        foreach (var dir in Directory.EnumerateDirectories(temp))
        {
            if (!LegacyName.IsMatch(Path.GetFileName(dir))) continue;
            if (nowUtc - Directory.GetLastWriteTimeUtc(dir) < LegacyMinAge) continue;
            if (TryDelete(dir)) deleted.Add(dir);
        }
        return deleted;
    }

    private static bool OwnerGone(string dir, DateTime nowUtc)
    {
        var owner = Path.Combine(dir, OwnerFileName);
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
        for (var attempt = 0; attempt < 2; attempt++)
        {
            try
            {
                Directory.Delete(dir, recursive: true);
                return true;
            }
            catch (DirectoryNotFoundException) { return true; }   // another run's sweep took it first
            catch (UnauthorizedAccessException) when (attempt == 0)
            {
                // A read-only file refuses the delete; a test may have set one. Clear them once and try again.
                try { ClearReadOnly(dir); }
                catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
            }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return false; }
        }
        return false;
    }

    internal static void ClearReadOnly(string dir)
    {
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
            File.SetAttributes(f, FileAttributes.Normal);
    }

    private static void RemoveRun(string root, string run)
    {
        // The pool keeps a closed connection's .db open; nothing else stops the folder going.
        SqliteConnection.ClearAllPools();
        s_owner?.Dispose();
        s_owner = null;
        TryDelete(run);
        try { Directory.Delete(root); }                         // only when empty: another run may be in it
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { }
    }
}
