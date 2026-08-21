using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// NTFS in-place log compression via <c>compact.exe /C /EXE:LZX</c> (Windows' "Compact OS"
/// WOF algorithm — write-once/read-many, which is exactly what an archived log is).
///
/// <para>Measured on the maintainer's real 111.9 MB log folder: <b>102.05 MB → 7.98 MB
/// (12.8:1) in 2.8 s</b>, and — the property this whole feature depends on —
/// <b>LastWriteTime was unchanged on all 289 files</b>, so
/// <see cref="LoggingService"/>'s age-based retention needs no change at all. Full numbers
/// and method: <c>docs/log-compression-eval.md</c>.</para>
///
/// <para><b>Reading stays transparent.</b> The files keep their name and extension and
/// decompress on read, so <c>rg</c> / <c>Select-String</c> / Notepad / "Open Log Folder"
/// all keep working. That is the reason this is not a <c>.gz</c> pass.</para>
/// </summary>
public sealed class WindowsLogCompressionService : ILogCompressionService
{
    private readonly ILoggingService? _log;

    public WindowsLogCompressionService(ILoggingService? log = null) => _log = log;

    // Classic [DllImport] on a blittable-ish signature, matching WindowsPlatformService's
    // convention (LibraryImport's source generator would force AllowUnsafeBlocks
    // project-wide). ExactSpelling + the explicit "W" entry point: without ExactSpelling
    // the marshaller appends its own suffix and can bind the wrong export.
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, ExactSpelling = true, SetLastError = true)]
    private static extern uint GetCompressedFileSizeW(string lpFileName, out uint lpFileSizeHigh);

    private const uint INVALID_FILE_SIZE = 0xFFFFFFFF;

    /// <summary>
    /// Bytes this file actually occupies. For an LZX-compressed file this is the COMPRESSED
    /// size (verified: 335,872 against a length of 8,441,784) — which matters because LZX,
    /// unlike <c>compact /c</c>, does NOT set <c>FileAttributes.Compressed</c>, so this is
    /// the only cheap "already done?" signal available.
    ///
    /// <para>Returns 0 when the size cannot be read, which the policy treats as "unknown,
    /// so try it" rather than as "already compressed".</para>
    /// </summary>
    internal static long OnDiskBytes(string path)
    {
        try
        {
            uint low = GetCompressedFileSizeW(path, out uint high);
            if (low == INVALID_FILE_SIZE && Marshal.GetLastWin32Error() != 0) return 0;
            return ((long)high << 32) | low;
        }
        catch { return 0; }
    }

    /// <inheritdoc/>
    public bool IsSupported(string anyPath)
    {
        // [VOLUMEROOT-2026-08-19] was Path.GetPathRoot + DriveInfo.DriveFormat, i.e. the
        // filesystem of the HOST volume when anyPath lives on a mount point. Near-theoretical
        // for logs under %LOCALAPPDATA%, but it is the same defect as the disk-space pair and
        // leaving one copy behind is how the pattern survives a fix. See VolumeRoot.
        string? root = VolumeRoot.Resolve(anyPath);
        if (root is null) return false;
        try
        {
            var fs = new StringBuilder(32);
            // Only the filesystem name is wanted; every other out-param is a required
            // placeholder. A failure here is "not supported", same as a non-NTFS answer.
            if (!GetVolumeInformationW(root, null, 0, out _, out _, out _, fs, fs.Capacity))
                return false;
            // NTFS only. ReFS supports neither algorithm, and FAT/exFAT/network shares
            // have no notion of either — a normal answer, not an error.
            return string.Equals(fs.ToString(), "NTFS", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    /// <summary>
    /// Filesystem name for a volume. Requires the trailing backslash on the root, which
    /// <c>VolumeRoot.Resolve</c> guarantees; accepts a volume mount-point path, which is
    /// the reason for using it over <c>DriveInfo.DriveFormat</c>.
    /// </summary>
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetVolumeInformationW",
               SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumeInformationW(
        string lpRootPathName,
        StringBuilder? lpVolumeNameBuffer,
        int nVolumeNameSize,
        out uint lpVolumeSerialNumber,
        out uint lpMaximumComponentLength,
        out uint lpFileSystemFlags,
        StringBuilder lpFileSystemNameBuffer,
        int nFileSystemNameSize);

    /// <inheritdoc/>
    public Task<LogCompressionResult> CompressAsync(
        string logRoot, TimeSpan minIdle, long minSizeBytes, CancellationToken ct = default)
        => Task.Run(() => Run(logRoot, minIdle, minSizeBytes, ct), ct);

    private LogCompressionResult Run(
        string logRoot, TimeSpan minIdle, long minSizeBytes, CancellationToken ct)
    {
        if (!IsSupported(logRoot))
        {
            _log?.Info(Constants.LogCatInit,
                $"LogCompression: volume for '{logRoot}' is not NTFS — skipped");
            return new LogCompressionResult(Supported: false, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        // The UI's own module folder. Its -0.log files are held open by Serilog for the
        // whole session, so they are excluded on identity rather than on age — the one
        // place liveness is a fact instead of an inference.
        string ownDir;
        try { ownDir = Path.GetFullPath(Path.Combine(logRoot, Constants.LogSubfolderName)); }
        catch { ownDir = ""; }

        var entries = new List<LogFileEntry>();
        try
        {
            foreach (var p in Directory.EnumerateFiles(logRoot, "*.log", SearchOption.AllDirectories))
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(p);
                    bool ours = ownDir.Length > 0 && fi.DirectoryName != null &&
                                string.Equals(Path.GetFullPath(fi.DirectoryName), ownDir,
                                              StringComparison.OrdinalIgnoreCase);
                    entries.Add(new LogFileEntry(
                        p, fi.Name, fi.Length, OnDiskBytes(p), fi.LastWriteTimeUtc, ours));
                }
                catch { /* vanished mid-enumeration (rotation) — nothing to compress */ }
            }
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log?.Warn(Constants.LogCatInit, $"LogCompression: could not enumerate '{logRoot}': {ex.Message}");
            return new LogCompressionResult(Supported: true, 0, 0, 0, 0, 0, 0, 0, 0);
        }

        var plan = LogCompressionPolicy.Plan(
            entries, DateTime.UtcNow, minIdle, minSizeBytes,
            TimeSpan.FromDays(Constants.LogCompressLiveFileMinAgeDays));
        if (plan.ToCompress.Count == 0)
        {
            return new LogCompressionResult(true, 0, 0,
                plan.SkippedLive, plan.SkippedAlreadyCompressed,
                plan.SkippedTooSmall, plan.SkippedTooFresh, 0, 0);
        }

        long before = 0;
        foreach (var p in plan.ToCompress) before += OnDiskBytes(p);

        var batches = LogCompressionPolicy.Batch(
            plan.ToCompress, Constants.LogCompressBatchSize, Constants.LogCompressMaxArgChars);

        foreach (var batch in batches)
        {
            // Cancellation lands between batches. A batch is ~0.35 s at the measured rate,
            // so this is responsive enough without killing a compact.exe mid-rewrite.
            ct.ThrowIfCancellationRequested();
            RunCompact(batch);
        }

        // Re-measure rather than parse compact.exe's stdout. It prints "N files were
        // compressed" for a batch in which individual files failed, and exits 1 while still
        // reporting success text — the eval caught it silently skipping 23 files.
        long after = 0;
        int compressed = 0;
        foreach (var p in plan.ToCompress)
        {
            long now = OnDiskBytes(p);
            after += now;
            long was = 0;
            try { was = new FileInfo(p).Length; } catch { }
            if (now > 0 && was > 0 && now < was) compressed++;
        }

        int failed = plan.ToCompress.Count - compressed;
        _log?.Info(Constants.LogCatInit,
            $"LogCompression: {compressed} compressed, {failed} failed, " +
            $"{before / 1024}KB -> {after / 1024}KB " +
            $"(skipped: live={plan.SkippedLive} done={plan.SkippedAlreadyCompressed} " +
            $"small={plan.SkippedTooSmall} fresh={plan.SkippedTooFresh})");

        return new LogCompressionResult(true, compressed, failed,
            plan.SkippedLive, plan.SkippedAlreadyCompressed,
            plan.SkippedTooSmall, plan.SkippedTooFresh, before, after);
    }

    /// <summary>
    /// One <c>compact.exe</c> invocation over a batch of paths, at idle CPU priority.
    ///
    /// <para><b>ArgumentList, never a joined string.</b> Per-game log folders are named
    /// after the game EXE and contain spaces (<c>SEED BATTLE DESTINY REMASTERED</c>); the
    /// eval's first hand-built command line split on those and silently skipped 23 files
    /// while still reporting success. <c>ArgumentList</c> applies Windows quoting rules
    /// itself, which removes the whole class of bug.</para>
    ///
    /// <para><b>Paths only, never a directory.</b> <c>compact /c &lt;dir&gt;</c> sets
    /// "compress new files here", which would route every future log WRITE through the
    /// compressor.</para>
    ///
    /// <para>Idle priority is CPU only, and deliberately so: at 2.8 s for a 102 MB backlog
    /// there is nothing to gain from I/O throttling, and this repo has already shipped that
    /// reflex once — multipipe Phase 0 dropped a thread's priority, starved scans ~20×, and
    /// was reverted (build 1840).</para>
    /// </summary>
    private void RunCompact(IReadOnlyList<string> batch)
    {
        try
        {
            var psi = new ProcessStartInfo("compact.exe")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            psi.ArgumentList.Add("/C");
            psi.ArgumentList.Add("/EXE:LZX");
            psi.ArgumentList.Add("/Q");
            foreach (var p in batch) psi.ArgumentList.Add(p);

            using var proc = Process.Start(psi);
            if (proc == null) return;
            try { proc.PriorityClass = ProcessPriorityClass.Idle; }
            catch { /* already exited — the work was trivial, nothing to throttle */ }

            // Drain both pipes before waiting: a child that fills a redirected buffer
            // blocks forever if nobody reads it.
            proc.StandardOutput.ReadToEnd();
            proc.StandardError.ReadToEnd();
            proc.WaitForExit();
        }
        catch (Exception ex)
        {
            // Never fatal — the per-file re-measure above is what decides success, so a
            // dead batch simply shows up as failures.
            _log?.Warn(Constants.LogCatInit, $"LogCompression: compact.exe batch failed: {ex.Message}");
        }
    }
}
