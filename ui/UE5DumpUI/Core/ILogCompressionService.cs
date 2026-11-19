using System;
using System.Threading;
using System.Threading.Tasks;
using UE5DumpUI.Models;

namespace UE5DumpUI.Core;

/// <summary>
/// Compresses archived log files in place so the logs kept for the retention window cost
/// less disk. Platform-specific by nature (NTFS transparent compression), so it lives behind
/// this interface per the repo's platform-abstraction rule — <c>Core</c> never spawns a
/// process or calls Win32.
///
/// <para><b>In place, same name, same extension.</b> The implementation must not rename,
/// re-extension or archive-container the files: <c>LoggingService</c>'s retention globs
/// <c>{prefix}-*.log</c> / <c>*.log</c> and keys on <c>LastWriteTime</c>, and every
/// diagnostic workflow in <c>docs/log-verification-checklist.md</c> greps these files as
/// plain text. A <c>.gz</c> route compresses only slightly better and breaks all of it —
/// <c>docs/log-compression-eval.md</c> §3 has the comparison.</para>
/// </summary>
public interface ILogCompressionService
{
    /// <summary>
    /// Whether the volume holding <paramref name="anyPath"/> supports the compression this
    /// service performs. False is a normal answer (ReFS / exFAT / a network share), not an
    /// error — callers report it and move on.
    /// </summary>
    bool IsSupported(string anyPath);

    /// <summary>
    /// Sweep every <c>*.log</c> under <paramref name="logRoot"/> (recursively) and compress
    /// the ones <c>LogCompressionPolicy</c> accepts, given the size floor
    /// <paramref name="minSizeBytes"/> and the idle window <paramref name="minIdle"/>.
    ///
    /// <para>Must never throw for an ordinary I/O reason — a locked or unreadable file is a
    /// counted failure. Must run at background priority: this can be invoked while a game
    /// is running.</para>
    /// </summary>
    Task<LogCompressionResult> CompressAsync(
        string logRoot, TimeSpan minIdle, long minSizeBytes, CancellationToken ct = default);
}
