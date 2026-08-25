using System.IO;

namespace UE5DumpUI.Helpers;

/// <summary>
/// Classifies a file-write exception as "the target location was not writable"
/// (audit X12). Install-CE-autorun auto-places into Cheat Engine's install
/// folder, which is commonly under <c>%ProgramFiles%</c> and needs elevation —
/// so the auto-place throws exactly when the manual save-dialog fallback is most
/// needed. This decides when to take that fallback rather than surfacing an error.
/// </summary>
internal static class FileWriteFault
{
    /// <summary>True when <paramref name="ex"/> indicates the destination could not
    /// be written (permission denied, path/directory problem, sharing/disk error) —
    /// as opposed to an unrelated programming error we should not swallow.</summary>
    internal static bool IsPlacementDenied(Exception ex) =>
        ex is UnauthorizedAccessException
           or System.Security.SecurityException
           or IOException;   // DirectoryNotFoundException, sharing violation, disk full, etc.
}
