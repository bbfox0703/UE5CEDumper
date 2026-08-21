using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace UE5DumpUI.Services;

/// <summary>
/// Resolve a path to the volume that ACTUALLY holds it, and the two numeric rules that go
/// with asking Win32 about that volume. One implementation, deliberately: this is the third
/// time the same mistake has been made in this codebase, and the reason it kept coming back
/// is that every site rolled its own resolution.
///
/// <para><b>The mistake.</b> <c>Path.GetPathRoot(@"C:\Mount\Games\file.db")</c> is
/// <c>C:\</c> — always, because it is pure string surgery on the path and knows nothing
/// about the filesystem. If <c>C:\Mount\Games</c> is a mounted volume with no drive letter
/// of its own, every question then gets answered about <b>C:</b>, the host, instead of about
/// the disk the file is really on. <c>DriveInfo</c> is the same trap wearing a different
/// hat: its constructor normalizes through <c>Path.GetPathRoot</c>, so handing it a correct
/// mount root silently converts it back into the wrong one.</para>
///
/// <para><b>The fix.</b> <c>GetVolumePathNameW</c> asks the filesystem, walking up until it
/// finds the real mount point, and returns it with the trailing backslash that every Win32
/// volume API (<c>GetDriveTypeW</c>, <c>GetVolumeInformationW</c>, <c>SHQueryRecycleBinW</c>)
/// requires. Pass its output to a Win32 call, never back through a BCL type.</para>
///
/// <para>audit #5 <b>AC17</b> found and fixed this in <c>VolumeHasRecycleBin</c>;
/// <c>[VOLUMEROOT-2026-08-19]</c> is the sibling grep that found the other three sites
/// still doing it the old way. Both now go through here.</para>
/// </summary>
internal static class VolumeRoot
{
    /// <summary>
    /// The mount root of the volume holding <paramref name="path"/>, with a trailing
    /// separator, or <c>null</c> when it cannot be determined.
    ///
    /// <para>⚠ <b>Null is not "use the host volume".</b> Every caller must treat it as
    /// "unknown" and fall back to its own safe sentinel. Substituting
    /// <c>Path.GetPathRoot</c> here as a "better than nothing" fallback would silently
    /// restore the exact defect this type exists to remove — on precisely the paths where
    /// resolution is hardest, which are the mounted ones.</para>
    ///
    /// <para>The path need not exist. <c>GetVolumePathNameW</c> walks up to the longest
    /// existing prefix, so a snapshot DB that has not been created yet still resolves to the
    /// volume it is about to be created on — which is what the disk-space guard needs.</para>
    /// </summary>
    internal static string? Resolve(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            // MAX_PATH is not enough for a long mount path; the API wants a buffer that can
            // hold the mount point, not the input.
            var buf = new StringBuilder(1024);
            if (!GetVolumePathNameW(Path.GetFullPath(path), buf, buf.Capacity)) return null;
            string root = buf.ToString();
            return root.Length == 0 ? null : EnsureTrailingSeparator(root);
        }
        catch { return null; }
    }

    /// <summary>
    /// Win32's volume APIs require a trailing separator on a root path and misbehave without
    /// one. <c>GetVolumePathNameW</c> supplies it, so this is belt-and-braces for that route
    /// — but it is load-bearing for a UNC root, where <c>Path.GetPathRoot</c> hands back
    /// <c>\\server\share</c> with no trailing separator at all.
    /// </summary>
    internal static string EnsureTrailingSeparator(string root)
    {
        if (root.Length == 0) return root;
        char last = root[root.Length - 1];
        return last == Path.DirectorySeparatorChar || last == Path.AltDirectorySeparatorChar
            ? root
            : root + Path.DirectorySeparatorChar;
    }

    /// <summary>
    /// Win32 reports volume sizes as <c>ULONGLONG</c>; the callers are <c>long</c>.
    ///
    /// <para>⚠ A plain cast is wrong in the one direction that matters. <c>(long)</c> of a
    /// value above <c>long.MaxValue</c> wraps to a NEGATIVE number, and a negative
    /// free-space reading does not read as "unknown" to a guard — it reads as "catastrophically
    /// full", so the snapshot guard would refuse to write on the largest volumes instead of the
    /// smallest. Saturating keeps the failure in the harmless direction. 8 EiB is not reachable
    /// today, but neither is it the caller's business to know that.</para>
    /// </summary>
    internal static long ClampToInt64(ulong v) => v > long.MaxValue ? long.MaxValue : (long)v;

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "GetVolumePathNameW",
               SetLastError = true, ExactSpelling = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetVolumePathNameW(
        string lpszFileName, StringBuilder lpszVolumePathName, int cchBufferLength);
}
