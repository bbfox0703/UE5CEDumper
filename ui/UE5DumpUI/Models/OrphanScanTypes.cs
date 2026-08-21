namespace UE5DumpUI.Models;

/// <summary>
/// Why a candidate leftover-proxy folder may or may not be cleaned up.
///
/// <para>Only <see cref="Deletable"/> authorises removing the folder CHAIN.
/// <see cref="FileOnly"/> authorises recycling the DLL but not touching any directory — that is the
/// correct, expected outcome for a non-Steam install and must be shown to the user, not hidden.
/// Everything else is a refusal that carries a blocker message.</para>
/// </summary>
public enum OrphanVerdict
{
    /// <summary>Files are ours, the shape is right, the game is gone: recycle files + prune chain.</summary>
    Deletable,

    /// <summary>Files are ours and removable, but no directory may be removed (outside a Steam
    /// library, or the chain is too shallow to prune safely).</summary>
    FileOnly,

    /// <summary>Not a <c>...\Binaries\Win64</c> folder.</summary>
    NotUeShape,

    /// <summary>The folder holds no files at all, so there is nothing of ours to remove.</summary>
    NoFilesAtAll,

    /// <summary>At least one file in the folder is not ours. Universal quantifier — one foreign
    /// file blocks the whole folder.</summary>
    ForeignFilePresent,

    /// <summary>The folder has a subdirectory (even an empty one).</summary>
    HasSubdirectories,

    /// <summary>A file's version info could not be read, so ownership is unknown. Fails closed.</summary>
    UnreadableDll,

    /// <summary>The directory listing failed. NEVER treated as "empty".</summary>
    UnreadableDirectory,

    /// <summary>A junction, symlink or other reparse point sits somewhere in the chain. Refused
    /// outright with an EMPTY plan — measured to destroy the junction target otherwise.</summary>
    ReparsePointInChain,

    /// <summary>The candidate is the Steam library root itself.</summary>
    AtCeilingRoot,

    /// <summary>Too few folders between the library root and the leaf to prune safely.</summary>
    BelowMinimumDepth,

    /// <summary>The chain left the known library root before reaching the game folder.</summary>
    OutsideKnownCeiling,

    /// <summary>A Steam <c>appmanifest</c> still names this install directory — the game is
    /// installed (or mid-update). Refused, full stop.</summary>
    SteamManifestPresent,

    /// <summary>Live cooked game content (a shipping exe, or <c>Content\Paks</c>) exists under the
    /// game root, so the game is not gone.</summary>
    LiveContentPresent,

    /// <summary>The folder is the binaries directory of a game the panel already lists as installed.</summary>
    LiveGameFolder,

    /// <summary>Not on a fixed drive, so the Recycle Bin is unavailable and a delete would be
    /// permanent. Refused rather than silently hard-deleting.</summary>
    NotOnFixedDrive,
}

/// <summary>
/// The two questions asked about a verdict, in ONE place so they cannot drift apart.
///
/// <para>They are deliberately DIFFERENT questions and were previously two hand-written copies of
/// the same expression — the scan's surface filter, the row's <c>IsActionable</c>, and the removal
/// path's re-check. "Can this be acted on?" and "should the user be told about it?" are not the same
/// predicate, and the measured B13/B41 defect was exactly that gap: a
/// <see cref="OrphanVerdict.NotOnFixedDrive"/> refusal is not actionable, but it is the one refusal
/// the user most needs to see, because our DLL really is sitting there and the volume would destroy
/// it rather than recycle it.</para>
///
/// <para>Lives beside the enum rather than in a service so the model, the scan and the report can
/// all ask the same function without a layering inversion.</para>
/// </summary>
public static class OrphanVerdictRules
{
    /// <summary>May anything actually be removed for this verdict? The authorisation gate.</summary>
    public static bool IsActionable(OrphanVerdict v) =>
        v is OrphanVerdict.Deletable or OrphanVerdict.FileOnly;

    /// <summary>
    /// Should a row be shown at all? Everything actionable, plus the refusals that hold something of
    /// ours and are blocked for a reason worth telling the user about. Every verdict NOT listed here
    /// means the folder held none of our files, which is not this feature's business.
    /// </summary>
    public static bool ShouldSurface(OrphanVerdict v) =>
        IsActionable(v) || v is OrphanVerdict.NotOnFixedDrive;
}

/// <summary>Where a candidate directory came from. Flags so a row can credit several sources.</summary>
[Flags]
public enum OrphanScanSources
{
    None = 0,

    /// <summary>Bounded shape scan of the Steam libraries. The authoritative source — it is the
    /// only one that sees a leftover nobody ever logged.</summary>
    SteamShapeScan = 1,

    /// <summary>The UI's own "Deployed … : &lt;path&gt;" log lines. Covers deployed-but-never-launched
    /// and non-Steam targets, with correct Unicode.</summary>
    DeployLog = 2,

    /// <summary>The DLL's own load banner. Covers non-Steam locations the Steam scan cannot see,
    /// but only for folders a game actually RAN from.</summary>
    DllLoadLog = 4,

    /// <summary>The persisted deployed-path ledger in ui-options.json. No retention window.</summary>
    Ledger = 8,

    All = SteamShapeScan | DeployLog | DllLoadLog | Ledger,
}

/// <summary>Whether a file in a candidate folder belongs to us.</summary>
public enum FileOwnership
{
    /// <summary>Positively identified as one of our binaries.</summary>
    Ours,

    /// <summary>Readable and definitely not ours.</summary>
    Foreign,

    /// <summary>Could not be determined. Always treated as a refusal (fails closed).</summary>
    Unreadable,
}

/// <summary>
/// A directory's contents as DATA, so the policy can be tested without a filesystem.
/// <paramref name="FileNames"/> and <paramref name="SubDirNames"/> are leaf names, not full paths.
/// </summary>
public sealed record DirSnapshot(
    string Path,
    IReadOnlyList<string> FileNames,
    IReadOnlyList<string> SubDirNames,
    bool IsReparsePoint);

/// <summary>Reads a directory. Returns null for "missing or unreadable", which always refuses.</summary>
public delegate DirSnapshot? DirProbe(string path);

/// <summary>Decides whether one file is ours.</summary>
public delegate FileOwnership OwnedFileProbe(string fullPath);

/// <summary>Does the volume holding this path have a working Recycle Bin? Asked at SCAN time so a
/// row can be refused before the confirm dialog promises a recycle the volume cannot perform —
/// the question used to be asked first inside the delete call, and as a drive-LETTER test rather
/// than a recycler test. (B13/B41)</summary>
public delegate bool RecyclerProbe(string path);

/// <summary>
/// Answers "is this game actually gone?" for a Steam game folder. Two independent signals are
/// checked by the real implementation (no appmanifest naming the install dir, and no executable
/// anywhere under the game root); either one present vetoes the cleanup.
/// </summary>
public delegate LivenessResult LivenessProbe(string commonRoot, string gameFolderName, string gameRoot);

/// <summary>Outcome of a liveness check. <see cref="OrphanVerdict.Deletable"/> means "really gone".</summary>
public readonly record struct LivenessResult(OrphanVerdict Verdict, string Reason, string Evidence);

/// <summary>
/// The complete removal plan for one candidate. <paramref name="DirsToRemove"/> is DEEPEST-FIRST and
/// never includes the ceiling. A dry run and the real run share this object, so what the
/// confirmation window lists cannot diverge from what is acted on.
/// </summary>
public readonly record struct PrunePlan(
    OrphanVerdict Verdict,
    IReadOnlyList<string> FilesToRecycle,
    IReadOnlyList<string> DirsToRemove,
    string? CeilingRoot,
    IReadOnlyList<string> Blockers,
    string EvidenceText);

/// <summary>Progress from the orphan scan, for the panel's status line.</summary>
public readonly record struct OrphanScanProgress(string CurrentPath, int Examined, int Found);

/// <summary>What actually happened when a row was removed. Immutable so it can cross a thread.</summary>
/// <param name="Cancelled">
/// The pass was interrupted part-way through THIS row. [ORPHANCANCEL-2026-08-20]
///
/// <para>Cancellation used to escape as an <see cref="System.OperationCanceledException"/> from
/// inside the row, which threw away everything the row had already done: a run that recycled three
/// files reported two, the interrupted row stayed ticked and still advertised "Recycle version.dll,
/// then remove up to 4 folder(s)" for a DLL that was already in the Recycle Bin, and the
/// half-pruned folder chain it left behind was invisible. Returning the partial counts instead of
/// throwing them away is what lets the caller report what actually happened.</para>
///
/// <para>⚠ <see cref="Success"/> stays <c>false</c> when this is set. An interrupted row is not a
/// completed one — it must not be dropped from the list, because the chain it half-pruned is
/// exactly what the user needs to still see.</para>
/// </param>
public readonly record struct OrphanRemovalResult(
    bool Success,
    string Message,
    int FilesRecycled,
    int DirsRemoved,
    bool Cancelled = false);
