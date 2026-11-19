using System.Text;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Pure policy for the Proxy Deploy tab's "leftover proxy" cleanup: decide whether a directory
/// holding one of our proxy DLLs is an ORPHAN (its game is gone) and, if so, which files may be
/// recycled and which directories may then be removed.
///
/// <para><b>This file contains no <c>System.IO</c> on purpose.</b> Directory contents arrive as
/// <see cref="DirSnapshot"/> data and probes arrive as delegates, so every rule below is unit
/// testable without a filesystem. That is the same argument <c>PlanUndeploy</c> already makes for
/// this feature area (see its header in <c>ProxyDeployService.cs</c>): the decision is worth testing,
/// fabricating a PE with a version resource is not.</para>
///
/// <para><b>TWO RULES THAT ARE LOAD-BEARING AND MEASURED. Do not "simplify" either.</b></para>
///
/// <para>1. <b>A failed directory listing is never "empty".</b> A <see cref="DirProbe"/> returning
/// null means "could not read" and MUST refuse. Two measured routes to deleting live data:
/// <c>Directory.EnumerateFileSystemEntries</c>'s <c>EnumerationOptions</c> overload defaults to
/// <c>AttributesToSkip = Hidden | System</c>, so a folder holding a hidden save file enumerates as
/// holding only our DLL; and the <c>Enumerate*</c> family is LAZY, so a <c>foreach</c> wrapped in
/// <c>catch { }</c> yields a PARTIAL list that is indistinguishable from a complete short one.
/// The surrounding file's established idiom IS <c>catch { }</c> — correct for a scan, catastrophic
/// for a delete predicate. The impure shell must use the BARE overload and materialise inside the
/// try.</para>
///
/// <para>2. <b>Never <c>Directory.Delete(path, recursive: true)</c>.</b> The prune walks up one
/// level at a time with the NON-recursive overload, which throws unless the directory is truly
/// empty. That is a kernel-enforced emptiness check the plan cannot fake, it closes the
/// scan→confirm→delete race (anything created in between makes the delete throw and the walk
/// stops), and it is why <see cref="PlanPrune"/> does not need to assert emptiness of ancestors.
/// Measured: recursive delete on a folder containing an unvalidated junction removes the junction
/// and then throws, leaving a half-completed operation the caller believes did nothing.</para>
/// </summary>
internal static class ProxyOrphanScanner
{
    /// <summary>Directory name that must hold the proxy for a chain prune to be allowed.</summary>
    private const string LeafDirName = "Win64";

    /// <summary>Required parent of <see cref="LeafDirName"/>.</summary>
    private const string LeafParentName = "Binaries";

    /// <summary>
    /// Minimum path segments between the ceiling (a Steam library's <c>common</c>) and the leaf.
    /// <c>common\Binaries\Win64</c> is 2 and must be refused: there is no game folder to remove,
    /// so a prune there would climb straight at the ceiling.
    /// </summary>
    private const int MinSegmentsBelowCeiling = 3;

    /// <summary>Hard cap on the upward walk so a pathological path cannot loop.</summary>
    private const int MaxChainWalk = 16;

    // ─────────────────────────────────────────────────────────────────────────
    // Shape
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Is this directory shaped like a UE binaries folder we would have deployed into — i.e.
    /// <c>...\Binaries\Win64</c>? Pure string test; the game's content is gone by definition, so
    /// shape is the only UE evidence left and <c>LooksLikeUeGameRoot</c> cannot be reused.
    ///
    /// <para><c>Win32</c>, <c>WinGDK</c> and <c>Arm64</c> are deliberately REFUSED. Deploy only ever
    /// forms <c>Path.Combine(root, "Binaries", "Win64")</c>, so a proxy anywhere else was placed by
    /// hand and no shape rule can justify walking a chain over it. WinGDK is additionally a signed
    /// package layout where removing directories breaks integrity checks.</para>
    /// </summary>
    internal static bool IsPrunableLeafShape(string? dirPath)
    {
        if (string.IsNullOrWhiteSpace(dirPath)) return false;

        string leaf = LastSegment(dirPath);
        if (!string.Equals(leaf, LeafDirName, StringComparison.OrdinalIgnoreCase)) return false;

        string? parent = ParentOf(dirPath);
        if (parent == null) return false;

        return string.Equals(LastSegment(parent), LeafParentName, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Last path segment, tolerating a trailing separator. <c>Path.GetFileName</c> returns "" for
    /// <c>"C:\a\b\"</c>, which would silently fail the shape test for a perfectly good directory.
    /// </summary>
    internal static string LastSegment(string path)
    {
        string p = path.TrimEnd('\\', '/');
        int i = p.LastIndexOfAny(new[] { '\\', '/' });
        return i < 0 ? p : p[(i + 1)..];
    }

    /// <summary>
    /// Parent directory as a normalised string, or null at a root. Deliberately NOT
    /// <c>Directory.GetParent</c> (that is IO) and NOT <c>Path.GetDirectoryName</c> alone (it
    /// returns "" rather than null in some shapes and does not strip a trailing separator).
    /// </summary>
    internal static string? ParentOf(string path)
    {
        string p = path.TrimEnd('\\', '/');
        int i = p.LastIndexOfAny(new[] { '\\', '/' });
        if (i <= 0) return null;

        // "C:\x" -> parent is the root "C:\", which is never a prune target. Return null so the
        // walk stops rather than handing a root to the caller.
        string parent = p[..i];
        if (parent.Length == 2 && parent[1] == ':') return null;   // bare "C:" — a root
        if (parent.EndsWith(":", StringComparison.Ordinal)) return null;
        if (IsUncRootish(parent)) return null;
        return parent;
    }

    /// <summary>
    /// Is this a UNC path with nothing below the share (<c>\\server</c> or <c>\\server\share</c>)?
    /// Those are roots; pruning at or above one is never allowed.
    /// </summary>
    internal static bool IsUncRootish(string path)
    {
        if (!path.StartsWith(@"\\", StringComparison.Ordinal)) return false;
        // Count segments after the leading "\\": server, share, ...
        string rest = path[2..].Trim('\\');
        if (rest.Length == 0) return true;
        int segs = rest.Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
        return segs <= 2;
    }

    /// <summary>
    /// Normalise a directory path for comparison: trim a trailing separator, collapse nothing else.
    /// Returns false when the input is not usable as a comparison key — empty, relative, or
    /// containing a <c>..</c> segment. Candidate directories can come from a LOG FILE, which is
    /// untrusted observed data, so a traversal segment must be rejected rather than resolved.
    /// </summary>
    internal static bool TryNormalizeDir(string? path, out string normalized)
    {
        normalized = "";
        if (string.IsNullOrWhiteSpace(path)) return false;

        string p = path.Trim().TrimEnd('\\', '/');
        if (p.Length == 0) return false;

        // Must be fully qualified: "X:\..." or a UNC "\\server\share\...".
        bool drive = p.Length >= 3 && char.IsLetter(p[0]) && p[1] == ':' && (p[2] == '\\' || p[2] == '/');
        bool unc = p.StartsWith(@"\\", StringComparison.Ordinal);
        if (!drive && !unc) return false;

        foreach (string seg in p.Split('\\', '/'))
        {
            if (seg == "..") return false;
        }

        normalized = p.Replace('/', '\\');
        return true;
    }

    /// <summary>
    /// Is <paramref name="dir"/> strictly below <paramref name="ceiling"/>? The trailing-separator
    /// guard is what stops <c>E:\SteamLibrary</c> from matching <c>E:\SteamLibraryBackup</c> — the
    /// same trap the existing Steam-exclusion test already pins.
    /// </summary>
    internal static bool IsStrictlyUnder(string dir, string ceiling)
    {
        if (!TryNormalizeDir(dir, out string d) || !TryNormalizeDir(ceiling, out string c)) return false;
        if (string.Equals(d, c, StringComparison.OrdinalIgnoreCase)) return false;
        return d.StartsWith(c + '\\', StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Path segments between <paramref name="ceiling"/> and <paramref name="dir"/>.</summary>
    internal static int SegmentsBelow(string dir, string ceiling)
    {
        if (!IsStrictlyUnder(dir, ceiling)) return -1;
        TryNormalizeDir(dir, out string d);
        TryNormalizeDir(ceiling, out string c);
        return d[(c.Length + 1)..].Split('\\', StringSplitOptions.RemoveEmptyEntries).Length;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Leaf classification
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Classify the leaf directory's CONTENTS and collect the files that are OURS.
    ///
    /// <para><b>Removing our own DLL is unconditional; removing FOLDERS is not.</b> Those are two
    /// separate authorisations: a folder shared with a third party — ReShade's <c>dxgi.dll</c> next
    /// to our <c>winmm.dll</c>, or a dead tool's leftover <c>.log</c>/<c>.ini</c> — must not refuse
    /// the whole row and leave OUR litter on disk forever, which is the opposite of what the feature
    /// is for. A foreign file does not block the recycle; it just means no directory will come away,
    /// and the non-recursive <c>Directory.Delete</c> enforces that by itself with no pre-check.</para>
    ///
    /// <para>Two things refuse everything, including the file:</para>
    /// <list type="bullet">
    /// <item>a null probe — a listing that FAILED is never "empty"; we cannot know what is in there,
    /// so we cannot know which files are ours;</item>
    /// <item>a reparse-point leaf — measured, <c>File.Delete</c> through a junction destroys the
    /// TARGET's file, so "our DLL" in a junction may not be our DLL at all.</item>
    /// </list>
    ///
    /// <para><paramref name="prunableLeaf"/> reports whether the leaf could itself come away (nothing
    /// left in it but our files). It is advisory — used for the row's summary text — never a gate.</para>
    /// </summary>
    internal static OrphanVerdict ClassifyLeaf(
        DirSnapshot? leaf,
        OwnedFileProbe isOurs,
        out List<string> ourFiles,
        out List<string> blockers,
        out bool prunableLeaf)
    {
        ourFiles = new List<string>();
        blockers = new List<string>();
        prunableLeaf = false;

        // null = the listing failed. NEVER treat that as "empty" — see the class remarks.
        if (leaf == null)
        {
            blockers.Add("The folder could not be read (permissions, or it disappeared).");
            return OrphanVerdict.UnreadableDirectory;
        }

        if (leaf.IsReparsePoint)
        {
            blockers.Add("The folder is a junction or symbolic link, so nothing here is touched.");
            return OrphanVerdict.ReparsePointInChain;
        }

        if (leaf.FileNames.Count == 0)
        {
            blockers.Add("The folder holds no files, so there is nothing of ours to remove.");
            return OrphanVerdict.NoFilesAtAll;
        }

        // A live game's Binaries\Win64 holds its own executable. If one is here the game is not gone,
        // whatever else the folder looks like — and this is the ONLY liveness signal available for a
        // non-Steam location, where there is no appmanifest to consult.
        foreach (string name in leaf.FileNames)
        {
            if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase))
            {
                blockers.Add($"An executable is still here ({name}), so the game is not gone.");
                return OrphanVerdict.LiveContentPresent;
            }
        }

        var foreign = new List<string>();
        var unreadable = new List<string>();
        foreach (string name in leaf.FileNames)
        {
            string full = Combine(leaf.Path, name);
            switch (isOurs(full))
            {
                case FileOwnership.Ours: ourFiles.Add(name); break;
                case FileOwnership.Foreign: foreign.Add(name); break;
                default: unreadable.Add(name); break;
            }
        }

        if (ourFiles.Count == 0)
        {
            // Nothing of ours here at all — not this feature's business, whatever else is present.
            blockers.Add(unreadable.Count > 0
                ? $"Nothing here is ours, and these could not be read: {string.Join(", ", Take(unreadable, 4))}"
                : "Nothing in this folder is ours.");
            return unreadable.Count > 0 ? OrphanVerdict.UnreadableDll : OrphanVerdict.ForeignFilePresent;
        }

        // Our files go regardless. Anything else present simply means the folder stays.
        if (foreign.Count > 0)
            blockers.Add($"Left in place because other files are here: {string.Join(", ", Take(foreign, 4))}");
        if (unreadable.Count > 0)
            blockers.Add($"Left alone (could not read): {string.Join(", ", Take(unreadable, 4))}");
        if (leaf.SubDirNames.Count > 0)
            blockers.Add($"Left in place because of subfolder(s): {string.Join(", ", Take(leaf.SubDirNames, 4))}");

        prunableLeaf = foreign.Count == 0 && unreadable.Count == 0 && leaf.SubDirNames.Count == 0;
        return OrphanVerdict.Deletable;
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The plan
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Build the full removal plan for one candidate leaf directory.
    ///
    /// <para>Outcomes worth knowing before reading the code:
    /// <see cref="OrphanVerdict.Deletable"/> = recycle the files AND prune the chain;
    /// <see cref="OrphanVerdict.FileOnly"/> = the files are ours and removable but the chain is NOT
    /// (no Steam ceiling, or too shallow) — that is the normal, correct outcome for a non-Steam
    /// install and it must be surfaced rather than hidden, or the log-derived candidates look
    /// broken. Everything else is a refusal with a populated blocker list.</para>
    /// </summary>
    internal static PrunePlan PlanPrune(
        string leafDir,
        IReadOnlyList<string> ceilingRoots,
        IReadOnlySet<string> liveBinariesDirs,
        DirProbe probe,
        OwnedFileProbe isOurs,
        LivenessProbe liveness,
        RecyclerProbe? hasRecycler = null)
    {
        var noFiles = Array.Empty<string>();
        var noDirs = Array.Empty<string>();

        // These two refuse OUTRIGHT — unlike the folder-only refusals further down, they mean we must
        // not touch even the file: an unusable path cannot be reasoned about, and a live game's
        // binaries folder holds a deployment the user made on purpose (Undeploy is for that).
        if (!TryNormalizeDir(leafDir, out string leaf))
            return new PrunePlan(OrphanVerdict.NotUeShape, noFiles, noDirs, null,
                new List<string> { "The path is not a usable absolute directory." }, "");

        if (liveBinariesDirs.Contains(leaf))
            return new PrunePlan(OrphanVerdict.LiveGameFolder, noFiles, noDirs, null,
                new List<string> { "This is the binaries folder of a game that is currently installed." }, "");

        DirSnapshot? leafSnap = probe(leaf);
        OrphanVerdict content = ClassifyLeaf(leafSnap, isOurs, out var ourNames, out var blockers,
                                             out bool prunableLeaf);
        if (content != OrphanVerdict.Deletable)
            return new PrunePlan(content, noFiles, noDirs, null, blockers, "");

        var filesToRecycle = ourNames.Select(n => Combine(leaf, n)).ToArray();

        // Ask about the RECYCLE BIN here, at scan time, so the confirm dialog never offers a
        // recycle this volume cannot perform, and ask it of the volume, not as a drive-LETTER
        // test: a fixed volume with NukeOnDelete=1 passes that test while FOF_ALLOWUNDO silently
        // hard-deletes and returns success. NotOnFixedDrive keeps its name (it is in the shipped
        // enum) but means "no usable Recycle Bin here".
        //
        // AFTER ClassifyLeaf, not before, for two reasons:
        //   * a volume with no bin must not manufacture a refusal for a folder holding nothing of
        //     ours — that folder is not this feature's business whatever the volume does; and
        //   * the plan carries filesToRecycle so the surfaced row can NAME the file it found. A
        //     refusal that cannot say which file it is about is not actionable by hand either.
        // The verdict is still not actionable, so nothing is offered for removal: the caller's
        // surface filter and RemoveOrphanProxyAsync both gate on Verdict before touching this list.
        if (hasRecycler != null && !hasRecycler(leaf))
            return new PrunePlan(OrphanVerdict.NotOnFixedDrive, filesToRecycle, noDirs, null,
                new List<string>
                {
                    "This volume has no working Recycle Bin (removable/network, or the bin is " +
                    "disabled for it), so a delete here would be PERMANENT. Refused — remove the " +
                    "file by hand if that is what you want.",
                }, "");

        // ---- chain authorisation, SEPARATE from the file authorisation above ----
        // From here on, every early return still recycles the files. Removing our own litter is
        // unconditional; only the folders are negotiable.

        if (!prunableLeaf)
            return FileOnly(filesToRecycle, blockers);

        if (!IsPrunableLeafShape(leaf))
            return FileOnly(filesToRecycle, blockers,
                            "Not a Binaries\\Win64 folder, so the folders are left in place.");

        string? ceiling = ceilingRoots.FirstOrDefault(c => IsStrictlyUnder(leaf, c));
        if (ceiling == null)
            return FileOnly(filesToRecycle, blockers,
                            "Outside a Steam library, so the folder chain is left in place.");

        int depth = SegmentsBelow(leaf, ceiling);
        if (depth < MinSegmentsBelowCeiling)
            return FileOnly(filesToRecycle, blockers,
                            $"Only {depth} folder(s) below the Steam library root — too shallow to prune safely.");

        // The game folder is the FIRST segment below the ceiling; it is the topmost thing we may
        // ever remove, and the ceiling itself is never touched.
        TryNormalizeDir(ceiling, out string c2);
        string gameFolderName = leaf[(c2.Length + 1)..].Split('\\')[0];
        string gameRoot = Combine(c2, gameFolderName);

        // ---- liveness: two independent signals, either one vetoes ----
        LivenessResult live = liveness(c2, gameFolderName, gameRoot);
        if (live.Verdict != OrphanVerdict.Deletable)
            return new PrunePlan(live.Verdict, noFiles, noDirs, null,
                                 new List<string> { live.Reason }, "");

        // ---- walk up building the ATTEMPT list ----
        // These are candidates, not promises. Each is removed at execution time with the NON-recursive
        // Directory.Delete, which throws unless the directory is genuinely empty — so the walk stops
        // by itself at the first level that still holds anything, which is exactly the rule we want
        // and needs no emptiness pre-check here. A problem found while walking downgrades to
        // file-only rather than refusing: our own litter still goes.
        var chain = new List<string>();
        string cursor = leaf;
        for (int i = 0; i < MaxChainWalk; ++i)
        {
            DirSnapshot? snap = probe(cursor);
            if (snap == null)
                return FileOnly(filesToRecycle, blockers, $"Could not read {cursor}, so folders are left alone.");
            if (snap.IsReparsePoint)
                return FileOnly(filesToRecycle, blockers,
                                $"Junction or symbolic link in the path ({cursor}), so no folder is removed.");

            chain.Add(cursor);
            if (string.Equals(cursor, gameRoot, StringComparison.OrdinalIgnoreCase)) break;

            string? next = ParentOf(cursor);
            if (next == null || !IsStrictlyUnder(next, c2))
                return FileOnly(filesToRecycle, blockers,
                                "The folder chain left the Steam library before reaching the game folder.");
            cursor = next;
        }

        if (!string.Equals(chain[^1], gameRoot, StringComparison.OrdinalIgnoreCase))
            return FileOnly(filesToRecycle, blockers,
                            "Could not reach the game folder within the chain limit.");

        // chain is deepest-first (leaf ... gameRoot), which is the removal order.
        string evidence = BuildEvidence(c2, gameFolderName, gameRoot, filesToRecycle, live.Evidence);
        return new PrunePlan(OrphanVerdict.Deletable, filesToRecycle, chain, c2, blockers, evidence);

        PrunePlan FileOnly(IReadOnlyList<string> files, List<string> why, string? extra = null)
        {
            var reasons = new List<string>(why);
            if (extra != null) reasons.Add(extra);
            if (reasons.Count == 0) reasons.Add("Only the file is removed; the folders are left in place.");
            return new PrunePlan(OrphanVerdict.FileOnly, files, noDirs, null, reasons, "");
        }
    }

    /// <summary>
    /// Human-readable justification shown in the confirmation window. An unexplained delete list
    /// cannot be audited by the person clicking it, so this is a required output, not a nicety.
    /// </summary>
    private static string BuildEvidence(
        string ceiling, string gameFolderName, string gameRoot,
        IReadOnlyList<string> files, string livenessEvidence)
    {
        var sb = new StringBuilder();
        // Name the steamapps folder directly rather than "common\..\steamapps" — this string is read
        // by the person authorising a deletion, so it must not contain a path they cannot paste.
        string? steamapps = ParentOf(ceiling);
        sb.Append("No Steam appmanifest in ").Append(steamapps ?? ceiling)
          .Append(" names installdir '").Append(gameFolderName).Append('\'');
        // ONLY facts the code actually established: the sole content signal is the executable
        // walk, whose own wording arrives via livenessEvidence. An unevaluated claim on the screen
        // that authorises a deletion is the worst place for one.
        if (!string.IsNullOrEmpty(livenessEvidence)) sb.Append("; ").Append(livenessEvidence);
        sb.Append("; the folder holds ").Append(string.Join(", ", files.Select(LastSegment)));
        sb.Append('.');
        return sb.ToString();
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Outcome resolution (sibling of ResolveUndeployOutcome)
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Turn the raw counts from an execution into a verdict + user-facing message. Pure, so the
    /// wording is testable. Mirrors <c>ResolveUndeployOutcome</c>'s shape.
    /// </summary>
    internal static (bool Success, string Message) ResolveRemovalOutcome(
        int filesRecycled, int filesAlreadyGone, int dirsRemoved, int dirsPlanned,
        IReadOnlyList<string> locked, IReadOnlyList<string> readOnly, IReadOnlyList<string> failed,
        string? pruneStopReason = null)
    {
        if (locked.Count > 0)
            return (false, $"In use by a running program: {string.Join(", ", Take(locked, 3))}. " +
                           "Close the game or Cheat Engine and try again.");
        if (readOnly.Count > 0)
            return (false, $"Read-only, left alone deliberately: {string.Join(", ", Take(readOnly, 3))}. " +
                           "Remove it by hand if you meant to protect it.");
        if (failed.Count > 0)
            return (false, $"Could not remove: {string.Join(", ", Take(failed, 3))}.");
        // "Already gone" is only the whole truth when nothing else happened. ProxyDeployService
        // counts a vanished file into allFilesGone and then still prunes the chain, so up to four
        // directories can be removed while a bare "nothing left to remove" shows in success green.
        // Report the directories the plan actually executed.
        if (filesRecycled == 0 && filesAlreadyGone > 0)
            return dirsRemoved > 0
                ? (true, $"File was already gone; {dirsRemoved} empty folder(s) removed.")
                : (true, "Already gone — nothing left to remove.");
        if (filesRecycled == 0)
            return (false, "Nothing was removed.");

        string files = filesRecycled == 1 ? "1 file" : $"{filesRecycled} files";
        if (dirsPlanned == 0)
            return (true, $"{files} moved to the Recycle Bin.");
        if (dirsRemoved == dirsPlanned)
            return (true, $"{files} moved to the Recycle Bin, {dirsRemoved} empty folder(s) removed.");

        // A partial prune is a SUCCESS — the file went, which is the point — but the reason the walk
        // stopped must be visible. "Still had something in it" and "permission denied" are different
        // facts and reporting the second as the first would send the user hunting for a file that
        // is not there.
        string why = pruneStopReason ?? "a folder could not be removed";
        return (true, $"{files} moved to the Recycle Bin; {dirsRemoved} of {dirsPlanned} folders " +
                      $"removed — stopped because {why}.");
    }

    // ─────────────────────────────────────────────────────────────────────────
    // The REPORT — a dry run the user can read before authorising anything
    // ─────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Render the scan result as plain text for the user to read outside the app.
    ///
    /// <para>Pure, and built from the SAME <see cref="OrphanProxy"/> rows the Execute path acts on —
    /// so the report cannot describe a different plan from the one that will run. What it CANNOT
    /// promise is that the world will not change in between, and it says so in its own words rather
    /// than leaving the reader to assume: a folder can gain a file, a junction can appear, a game can
    /// be reinstalled. Execute re-evaluates everything from disk for exactly that reason.</para>
    ///
    /// <para><paramref name="generatedAt"/> is passed in rather than read from the clock so the output
    /// is deterministic under test.</para>
    /// </summary>
    /// <param name="foldersExamined">How many folders the scan looked at. Reported so an EMPTY
    /// report is evidence rather than an assertion — "0 found" and "never looked" have to be
    /// distinguishable, and a week later the file is the only thing left to tell them apart.
    /// Pass -1 when the count is unknown (older callers / tests) and the line is omitted.</param>
    internal static string BuildReport(
        IReadOnlyList<OrphanProxy> rows, string generatedAt, string appVersion,
        int foldersExamined = -1)
    {
        var sb = new StringBuilder();
        void Line(string s = "") => sb.Append(s).Append("\r\n");

        Line("UE5CEDumper — leftover proxy DLL report");
        Line($"Generated {generatedAt} by build {appVersion}");
        Line(new string('=', 78));
        Line();
        Line("THIS IS A DRY RUN. Nothing has been changed on disk by generating this file.");
        Line();
        Line("If you go ahead, for each entry below:");
        Line("  * our DLL is moved to the RECYCLE BIN, so it can be restored from there;");
        Line("  * folders are then removed ONE LEVEL AT A TIME, and only while each one is left");
        Line("    completely empty. The moment a folder still holds anything, that folder and");
        Line("    everything above it are kept.");
        Line("  * a file that is not ours is never touched, moved or renamed.");
        Line();
        Line("WHAT THIS REPORT CANNOT PROMISE: it is a snapshot. Between now and the moment you");
        Line("press the delete button the folders can change — a program can write a file into");
        Line("one, a junction can be created, or a game can be reinstalled. Every condition is");
        Line("therefore checked AGAIN immediately before anything is removed, so the real result");
        Line("can be smaller than this list. It will never be larger.");
        Line();

        if (rows.Count == 0)
        {
            Line("No leftover proxy DLLs were found.");
            if (foldersExamined >= 0)
            {
                Line();
                Line($"{foldersExamined} folder(s) were examined. This file is the record that the");
                Line("scan ran and covered them — a clean result and a scan that never happened look");
                Line("identical without it.");
            }
            return sb.ToString();
        }

        int selected = rows.Count(r => r.IsSelected && r.IsActionable);
        Line($"{rows.Count} leftover location(s) found; {selected} currently ticked for removal.");
        Line();

        int n = 0;
        foreach (var row in rows)
        {
            Line(new string('-', 78));
            string tick = row.IsSelected && row.IsActionable ? "[x]" : "[ ]";
            Line($"{tick} {++n}. {row.DllDirectory}");
            Line();
            // A blocked row still names its files — that is how the user finds them by hand — but
            // must not claim they are "to be recycled", because nothing will be.
            Line(row.IsActionable
                ? $"      Our file(s) to be recycled : {row.DllNames}"
                : $"      Our file(s) found here     : {row.DllNames}  (NOT removable — see Notes)");
            if (row.FileVersion.Length > 0) Line($"      Version                    : {row.FileVersion}");
            if (row.SizeText.Length > 0) Line($"      Size                       : {row.SizeText}");
            Line($"      Found by                   : {DescribeSources(row.Source)}");
            Line();

            if (row.ChainDirs.Count > 0)
            {
                Line("      Folders that would then be tried, in this order, each ONLY if empty:");
                int i = 0;
                foreach (string d in row.ChainDirs) Line($"        {++i}. {d}");
                string? kept = ParentOf(row.TopmostRemovableDir);
                if (!string.IsNullOrEmpty(kept)) Line($"      NOT touched: {kept}");
            }
            else
            {
                Line("      No folder would be removed — only the file.");
            }

            if (row.Blockers.Count > 0)
            {
                Line();
                Line("      Notes:");
                foreach (string b in row.Blockers) Line($"        - {b}");
            }

            if (row.EvidenceText.Length > 0)
            {
                Line();
                Line("      Why this is judged a leftover:");
                Line($"        {row.EvidenceText}");
            }
            Line();
        }

        Line(new string('=', 78));
        Line("End of report. To act on it, tick the rows you want in the Proxy Deploy tab and");
        Line("use the delete button; you will get one more confirmation listing the same paths.");
        return sb.ToString();
    }

    /// <summary>
    /// Which report files have aged out. Pure so the rule is testable without a clock or a filesystem.
    ///
    /// <para>Two properties that are the whole point:</para>
    /// <list type="bullet">
    /// <item><b>The NEWEST report is never returned</b>, whatever its age. Someone who made one report
    /// months ago and kept it deliberately must not have it vanish — this is the only case where the
    /// cleanup could destroy something wanted, and one guard removes it.</item>
    /// <item>Age, not a count. A count has the same flaw the log retention documents: several reports
    /// in one before/after session would evict an older useful one regardless of its date.</item>
    /// </list>
    /// </summary>
    internal static IReadOnlyList<string> SelectExpiredReports(
        IReadOnlyList<(string Path, DateTime Written)> files, DateTime now, int maxAgeDays)
    {
        if (files.Count <= 1) return Array.Empty<string>();   // never prune the only one

        var newest = files.OrderByDescending(f => f.Written).First();
        DateTime cutoff = now.AddDays(-maxAgeDays);
        return files
            .Where(f => !ReferenceEquals(f.Path, newest.Path)
                        && !string.Equals(f.Path, newest.Path, StringComparison.OrdinalIgnoreCase)
                        && f.Written < cutoff)
            .Select(f => f.Path)
            .ToArray();
    }

    /// <summary>Human-readable source list for the report.</summary>
    internal static string DescribeSources(OrphanScanSources src)
    {
        if (src == OrphanScanSources.None) return "(unknown)";
        var parts = new List<string>();
        if (src.HasFlag(OrphanScanSources.SteamShapeScan)) parts.Add("Steam library scan");
        if (src.HasFlag(OrphanScanSources.DeployLog)) parts.Add("our deploy log");
        if (src.HasFlag(OrphanScanSources.DllLoadLog)) parts.Add("our DLL load log");
        if (src.HasFlag(OrphanScanSources.Ledger)) parts.Add("deployed-path record");
        return string.Join(", ", parts);
    }

    // ─────────────────────────────────────────────────────────────────────────
    // Candidate discovery from log lines (pure parsing)
    // ─────────────────────────────────────────────────────────────────────────

    private const string DllLoadedMarker = " | process: ";
    private const string DllLoadedTail = " [PID=";

    /// <summary>
    /// Recover the host process path from the DLL's own load banner. The line has separators of its
    /// own and paths contain spaces, so this is deliberately index-based rather than a split.
    /// <para>Example: <c>... | build: 1.0.0.2432 ... | process: E:\...\Game.exe [PID=86276]</c></para>
    /// </summary>
    internal static string? ParseDllLoadedProcessPath(string? logLine)
    {
        if (string.IsNullOrEmpty(logLine)) return null;
        int a = logLine.IndexOf(DllLoadedMarker, StringComparison.Ordinal);
        if (a < 0) return null;
        a += DllLoadedMarker.Length;
        int b = logLine.IndexOf(DllLoadedTail, a, StringComparison.Ordinal);
        if (b <= a) return null;
        string path = logLine[a..b].Trim();
        return path.Length == 0 ? null : path;
    }

    private const string DeployedMarker = "Deployed ";

    /// <summary>
    /// Recover the deployed target path from the UI's own deploy log line
    /// (<c>Deployed {type} to {game}: {targetDll}</c>). Unlike the DLL banner this is written by
    /// managed code, so its Unicode is intact.
    /// </summary>
    internal static string? ParseDeployedTargetPath(string? logLine)
    {
        if (string.IsNullOrEmpty(logLine)) return null;
        if (logLine.IndexOf(DeployedMarker, StringComparison.Ordinal) < 0) return null;
        int colon = logLine.LastIndexOf(": ", StringComparison.Ordinal);
        if (colon < 0) return null;
        string path = logLine[(colon + 2)..].Trim();
        return path.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? path : null;
    }

    /// <summary>
    /// Turn a stream of log lines into deduplicated candidate DIRECTORIES. Untrusted input: every
    /// path goes through <see cref="TryNormalizeDir"/>, so a traversal segment or a relative path
    /// is dropped rather than resolved.
    /// </summary>
    internal static IReadOnlyList<string> CandidateDirsFrom(IEnumerable<string> logLines, bool isDllLog)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();
        foreach (string line in logLines)
        {
            string? p = isDllLog ? ParseDllLoadedProcessPath(line) : ParseDeployedTargetPath(line);
            if (p == null) continue;

            // Both sources yield a FILE path; the candidate is its directory.
            int i = p.TrimEnd('\\', '/').LastIndexOfAny(new[] { '\\', '/' });
            if (i <= 0) continue;
            string dir = p[..i];

            if (!TryNormalizeDir(dir, out string norm)) continue;
            if (seen.Add(norm)) ordered.Add(norm);
        }
        return ordered;
    }

    // ─────────────────────────────────────────────────────────────────────────

    private static string Combine(string dir, string name) =>
        dir.TrimEnd('\\', '/') + '\\' + name;

    private static IEnumerable<string> Take(IReadOnlyList<string> items, int n) =>
        items.Count <= n ? items : items.Take(n).Append("…");
}
