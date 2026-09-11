namespace UE5DumpUI.Core;

/// <summary>
/// Is the Windows Recycle Bin actually going to accept a delete on a given volume?
///
/// <para>Pure policy, deliberately: it takes the raw registry values as parameters and
/// contains no registry access, no P/Invoke and no I/O, so the decision can be unit
/// tested for every combination instead of only for whatever this machine happens to be
/// configured as. The reading of those values lives in the platform service, which is
/// where the "Platform Abstraction" rule requires it.</para>
///
/// <para><b>Why this exists at all — the measurement that forced it (2026-08-12).</b>
/// <see cref="IPlatformService.VolumeHasRecycleBin"/> used to answer the question with
/// <c>SHQueryRecycleBin(root) == S_OK</c> alone. That call reports on the bin's
/// <i>contents</i>, not on the <i>policy</i>: on a fixed volume whose bin had been turned
/// off via <c>NukeOnDelete=1</c> it still returned <c>S_OK</c> (with the stale
/// <c>$RECYCLE.BIN</c> folder's leftover items), so the probe answered <b>true</b> and the
/// refusal never fired. Measured end to end on that volume, with a throwaway file:
/// <c>SHFileOperation</c> with <c>FOF_ALLOWUNDO</c> returned <c>rc=0</c>,
/// <c>fAnyOperationsAborted=false</c>, the bin's item count did not change, and the file
/// was gone. The caller then reported <i>"moved to the Recycle Bin"</i> for a file it had
/// permanently destroyed — the exact lie the B13/B41 fix was written to prevent, still
/// reachable because its detector could not see the condition it was named after.</para>
///
/// <para><b>Do not "simplify" this by looking at the bin's item count.</b> An enabled bin
/// that happens to be EMPTY is the common case and must read as present; keying off items
/// would refuse every clean machine. Emptiness and disabled-ness are different facts and
/// only the registry distinguishes them.</para>
/// </summary>
public static class RecycleBinPolicy
{
    /// <summary>Registry path (under both HKLM and HKCU) of the Explorer policy key.</summary>
    public const string PoliciesExplorerKey =
        @"Software\Microsoft\Windows\CurrentVersion\Policies\Explorer";

    /// <summary>Registry path (under HKCU) of the Recycle Bin settings key.</summary>
    public const string BitBucketKey =
        @"Software\Microsoft\Windows\CurrentVersion\Explorer\BitBucket";

    /// <summary>
    /// True when deletes on this volume will NOT go to the Recycle Bin.
    ///
    /// <para>Every parameter is nullable because <b>absent is the normal state</b> and is
    /// not the same as zero for the caller's purposes — a machine that has never had these
    /// touched has none of them, and must come out as "the bin works". Windows' own
    /// defaults are encoded here: absent <c>NoRecycleFiles</c> = no policy, absent
    /// <c>UseGlobalSettings</c> = per-volume, absent <c>NukeOnDelete</c> = bin enabled.</para>
    /// </summary>
    /// <param name="policyNoRecycleFilesMachine">HKLM <c>NoRecycleFiles</c>, or null if absent.</param>
    /// <param name="policyNoRecycleFilesUser">HKCU <c>NoRecycleFiles</c>, or null if absent.</param>
    /// <param name="useGlobalSettings">HKCU <c>BitBucket\UseGlobalSettings</c>, or null.</param>
    /// <param name="globalNukeOnDelete">HKCU <c>BitBucket\NukeOnDelete</c>, or null.</param>
    /// <param name="volumeNukeOnDelete">HKCU <c>BitBucket\Volume\{guid}\NukeOnDelete</c>, or null.</param>
    /// <param name="volumeLookupFailed">[A3-RECYCLE-GUID-FAILOPEN] True when the volume-GUID lookup failed, so
    /// <paramref name="volumeNukeOnDelete"/> could not be read at all -- a different fact from "absent".</param>
    public static bool IsDisabled(
        int? policyNoRecycleFilesMachine,
        int? policyNoRecycleFilesUser,
        int? useGlobalSettings,
        int? globalNukeOnDelete,
        int? volumeNukeOnDelete,
        bool volumeLookupFailed = false)
    {
        // 1. Group Policy ("Do not move deleted files to the Recycle Bin") outranks
        //    everything and applies to every volume. Machine before user: an admin-set
        //    machine policy is not overridable by the user hive, so checking HKCU first
        //    could let a stale user value mask a live machine one.
        if (policyNoRecycleFilesMachine == 1) return true;
        if (policyNoRecycleFilesUser == 1) return true;

        // 2. "Use one setting for all locations" in the Recycle Bin property sheet. When
        //    set, the per-volume flags are IGNORED by Explorer, so reading them here would
        //    produce a verdict the shell does not share -- in either direction.
        if (useGlobalSettings == 1) return globalNukeOnDelete == 1;

        // 3. Otherwise the per-volume flag decides. This is the case the original probe
        //    missed entirely.
        //
        //    [A3-RECYCLE-GUID-FAILOPEN] ...and when that flag could not be READ -- the volume-GUID lookup failed
        //    (SUBST, RAM-disk-style volumes) -- fail CLOSED. Null means "absent", and absent reads as "bin enabled",
        //    which left the verdict to SHQueryRecycleBin: measured blind to NukeOnDelete. Refusing a delete that
        //    would have worked is recoverable; a permanent delete reported as "moved to the Recycle Bin" is not.
        //    Only here: a policy or the global setting above decides without the volume flag.
        if (volumeLookupFailed) return true;
        return volumeNukeOnDelete == 1;
    }

    /// <summary>
    /// Extract the <c>{guid}</c> that keys the per-volume Recycle Bin settings from the
    /// volume name <c>GetVolumeNameForVolumeMountPoint</c> returns
    /// (<c>\\?\Volume{...}\</c>). Returns "" when the input is not that shape. The caller must
    /// then pass <c>volumeLookupFailed</c> to <see cref="IsDisabled"/>: an ABSENT value reads as
    /// "enabled", so treating a failed lookup as absent is exactly how a disabled volume slipped
    /// back through [A3-RECYCLE-GUID-FAILOPEN].
    /// </summary>
    public static string VolumeGuidFromVolumeName(string? volumeName)
    {
        if (string.IsNullOrEmpty(volumeName)) return "";
        int open = volumeName!.IndexOf('{');
        int close = volumeName.IndexOf('}', open + 1);
        if (open < 0 || close < 0) return "";
        return volumeName.Substring(open, close - open + 1);
    }
}
