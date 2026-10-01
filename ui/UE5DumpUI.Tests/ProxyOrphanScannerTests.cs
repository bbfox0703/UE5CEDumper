using UE5DumpUI.Models;
using UE5DumpUI.Services;
using Xunit;

namespace UE5DumpUI.Tests;

/// <summary>
/// The leftover-proxy ("orphan") cleanup POLICY. This is the first feature in the app that removes
/// directories, so the bar is "prove it cannot destroy user data" — and every rule below exists
/// because a measured filesystem behaviour makes the naive version dangerous.
///
/// All of this runs with no filesystem: <see cref="ProxyOrphanScanner"/> takes directory contents as
/// <see cref="DirSnapshot"/> data and probes as delegates, so the decision is tested rather than the
/// fixture. Same argument the existing undeploy tests make for the same feature area.
/// </summary>
public class ProxyOrphanScannerTests
{
    // ── helpers ──────────────────────────────────────────────────────────────

    private const string Common = @"E:\SteamLibrary\steamapps\common";

    private static DirSnapshot Dir(string path, string[]? files = null, string[]? subs = null,
                                   bool reparse = false)
        => new(path, files ?? Array.Empty<string>(), subs ?? Array.Empty<string>(), reparse);

    /// <summary>A probe over a fixed dictionary; anything absent returns null = "unreadable".</summary>
    private static DirProbe ProbeOf(params DirSnapshot[] snaps)
    {
        var map = snaps.ToDictionary(s => s.Path, s => s, StringComparer.OrdinalIgnoreCase);
        return p => map.TryGetValue(p, out var s) ? s : null;
    }

    private static OwnedFileProbe OursIf(params string[] ourFileNames)
    {
        var set = new HashSet<string>(ourFileNames, StringComparer.OrdinalIgnoreCase);
        return full =>
        {
            string name = Path.GetFileName(full);
            return set.Contains(name) ? FileOwnership.Ours : FileOwnership.Foreign;
        };
    }

    private static readonly LivenessProbe Gone =
        (_, _, _) => new LivenessResult(OrphanVerdict.Deletable, "", "no executable survives");

    private static LivenessProbe Alive(OrphanVerdict v) =>
        (_, _, _) => new LivenessResult(v, "still installed", "");

    /// <summary>The measured real case: &lt;Game&gt;\Game\Binaries\Win64 holding only version.dll.</summary>
    private static (string Leaf, DirProbe Probe) DqxiShape()
    {
        string game = Common + @"\DRAGON QUEST XI S";
        string proj = game + @"\Game";
        string bin = proj + @"\Binaries";
        string leaf = bin + @"\Win64";
        return (leaf, ProbeOf(
            Dir(leaf, files: new[] { "version.dll" }),
            Dir(bin, subs: new[] { "Win64" }),
            Dir(proj, subs: new[] { "Binaries" }),
            Dir(game, subs: new[] { "Game" }),
            Dir(Common, subs: new[] { "DRAGON QUEST XI S", "Other Game" })));
    }

    // ── Layer 1: shape ───────────────────────────────────────────────────────

    [Theory]
    [InlineData(@"C:\g\Proj\Binaries\Win64", true)]
    [InlineData(@"C:\g\Proj\binaries\win64", true)]          // OrdinalIgnoreCase
    [InlineData(@"C:\g\Proj\BINARIES\WIN64", true)]
    [InlineData(@"C:\g\Proj\Binaries\Win64\", true)]          // trailing separator must not blank it
    [InlineData(@"C:\g\Proj\Binaries\Win32", false)]          // we never deploy there
    [InlineData(@"C:\g\Proj\Binaries\WinGDK", false)]         // signed package layout
    [InlineData(@"C:\g\Proj\Binaries\Arm64", false)]
    [InlineData(@"C:\g\Proj\Content\Win64", false)]           // parent is not Binaries
    [InlineData(@"C:\g\Proj\Binaries", false)]
    [InlineData(@"C:\Win64", false)]                          // at a drive root, must not throw
    [InlineData(@"Win64", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsPrunableLeafShape_OnlyBinariesWin64(string? path, bool expected)
        => Assert.Equal(expected, ProxyOrphanScanner.IsPrunableLeafShape(path));

    [Theory]
    [InlineData(@"C:\a\b", @"C:\a")]
    [InlineData(@"C:\a\b\", @"C:\a")]
    [InlineData(@"C:\a", null)]            // parent is the root -> stop
    [InlineData(@"C:\", null)]
    [InlineData(@"\\srv\share\a", null)]   // share root -> stop
    public void ParentOf_StopsAtRoots(string path, string? expected)
        => Assert.Equal(expected, ProxyOrphanScanner.ParentOf(path));

    [Theory]
    [InlineData(@"E:\SteamLibrary\x", true)]
    [InlineData(@"e:\steamlibrary\X", true)]                       // case-insensitive
    [InlineData(@"E:\SteamLibrary", false)]                        // equal is NOT under
    [InlineData(@"E:\SteamLibraryBackup\x", false)]                // the shared-prefix trap
    public void IsStrictlyUnder_GuardsSharedPrefix(string dir, bool expected)
        => Assert.Equal(expected, ProxyOrphanScanner.IsStrictlyUnder(dir, @"E:\SteamLibrary"));

    [Theory]
    [InlineData(@"C:\a\b", true)]
    [InlineData(@"\\srv\share\a\b", true)]
    [InlineData(@"C:\a\..\b", false)]      // untrusted log input must never be resolved
    [InlineData(@"relative\path", false)]
    [InlineData(@"C:", false)]
    [InlineData("", false)]
    public void TryNormalizeDir_RejectsTraversalAndRelative(string path, bool ok)
        => Assert.Equal(ok, ProxyOrphanScanner.TryNormalizeDir(path, out _));

    // ── Layer 1: leaf contents ───────────────────────────────────────────────

    [Fact]
    public void ClassifyLeaf_OnlyOurDll_IsDeletableAndPrunable()
    {
        var v = ProxyOrphanScanner.ClassifyLeaf(
            Dir(@"C:\x", files: new[] { "version.dll" }), OursIf("version.dll"),
            out var ours, out _, out bool prunable);

        Assert.Equal(OrphanVerdict.Deletable, v);
        Assert.Equal(new[] { "version.dll" }, ours);
        Assert.True(prunable);
    }

    [Fact]
    public void ClassifyLeaf_AllFourProxies_IsDeletable()
    {
        var v = ProxyOrphanScanner.ClassifyLeaf(
            Dir(@"C:\x", files: new[] { "version.dll", "dinput8.dll", "dxgi.dll", "winmm.dll" }),
            OursIf("version.dll", "dinput8.dll", "dxgi.dll", "winmm.dll"),
            out var ours, out _, out _);

        Assert.Equal(OrphanVerdict.Deletable, v);
        Assert.Equal(4, ours.Count);
    }

    // ── the shared-folder case: our litter still goes, the folder stays ──────

    [Theory]
    [InlineData("steam_appid.txt")]
    [InlineData("ue4ss.log")]          // a dead tool's leftover log
    [InlineData("dxgi.ini")]           // ReShade's config — invisible to a *.dll glob
    [InlineData("version.dll.bak")]
    [InlineData("desktop.ini")]
    public void ClassifyLeaf_ForeignFilePresent_StillRecyclesOursButKeepsTheFolder(string foreign)
    {
        // The behaviour the repo owner asked for explicitly: removing OUR DLL is unconditional, the
        // FOLDER is what is negotiable. Refusing the whole row here used to leave our own litter on
        // disk forever whenever anything else shared the folder.
        var v = ProxyOrphanScanner.ClassifyLeaf(
            Dir(@"C:\x", files: new[] { "version.dll", foreign }), OursIf("version.dll"),
            out var ours, out var blockers, out bool prunable);

        Assert.Equal(OrphanVerdict.Deletable, v);
        Assert.Equal(new[] { "version.dll" }, ours);   // ours is still collected
        Assert.False(prunable);                         // but the folder cannot come away
        Assert.Contains(foreign, string.Join(" ", blockers), StringComparison.Ordinal);
    }

    [Fact]
    public void ClassifyLeaf_ReshadeSharingTheFolder_RemovesOnlyOurs()
    {
        // The measured real-world shape: ReShade owns dxgi.dll, we own winmm.dll, same folder.
        var v = ProxyOrphanScanner.ClassifyLeaf(
            Dir(@"C:\x", files: new[] { "dxgi.dll", "winmm.dll", "ReShade.ini" }),
            OursIf("winmm.dll"),
            out var ours, out _, out bool prunable);

        Assert.Equal(OrphanVerdict.Deletable, v);
        Assert.Equal(new[] { "winmm.dll" }, ours);      // ReShade's dxgi.dll is untouched
        Assert.False(prunable);
    }

    [Theory]
    [InlineData("D3D12")]
    [InlineData("EmptyFolder")]        // even an EMPTY subdirectory keeps the folder
    public void ClassifyLeaf_SubdirectoryKeepsTheFolderButNotTheFile(string sub)
    {
        var v = ProxyOrphanScanner.ClassifyLeaf(
            Dir(@"C:\x", files: new[] { "version.dll" }, subs: new[] { sub }),
            OursIf("version.dll"), out var ours, out _, out bool prunable);

        Assert.Equal(OrphanVerdict.Deletable, v);
        Assert.Single(ours);
        Assert.False(prunable);
    }

    // ── things that refuse EVERYTHING, including the file ────────────────────

    [Fact]
    public void ClassifyLeaf_NothingOfOursHere_IsNotOurBusiness()
    {
        // A mod loader's version.dll must never be recycled just because of its NAME.
        var v = ProxyOrphanScanner.ClassifyLeaf(
            Dir(@"C:\x", files: new[] { "version.dll" }), _ => FileOwnership.Foreign,
            out var ours, out _, out _);

        Assert.Equal(OrphanVerdict.ForeignFilePresent, v);
        Assert.Empty(ours);
    }

    [Fact]
    public void ClassifyLeaf_EmptyFolder_IsNotOurBusiness()
        => Assert.Equal(OrphanVerdict.NoFilesAtAll,
            ProxyOrphanScanner.ClassifyLeaf(Dir(@"C:\x"), OursIf(), out _, out _, out _));

    [Fact]
    public void ClassifyLeaf_ExecutableStillPresent_MeansTheGameIsAlive()
    {
        // The only liveness signal available for a NON-Steam folder, where there is no appmanifest.
        // A live game's Binaries\Win64 holds its own shipping exe.
        var v = ProxyOrphanScanner.ClassifyLeaf(
            Dir(@"C:\x", files: new[] { "version.dll", "Game-Win64-Shipping.exe" }),
            OursIf("version.dll"), out var ours, out _, out _);

        Assert.Equal(OrphanVerdict.LiveContentPresent, v);
        Assert.Empty(ours);          // a working deployment must NOT be recycled
    }

    [Fact]
    public void ClassifyLeaf_OnlyUnreadableFiles_FailsClosed()
        => Assert.Equal(OrphanVerdict.UnreadableDll,
            ProxyOrphanScanner.ClassifyLeaf(
                Dir(@"C:\x", files: new[] { "version.dll" }), _ => FileOwnership.Unreadable,
                out _, out _, out _));

    [Fact]
    public void ClassifyLeaf_NullProbe_IsUnreadableNotEmpty()
    {
        // THE most important single assertion in this file. A failed enumeration must never read as
        // "the folder is empty" — that is the measured route to deleting live data, because
        // Directory.Enumerate* is lazy and the surrounding file's idiom is catch { }.
        var v = ProxyOrphanScanner.ClassifyLeaf(null, OursIf("version.dll"), out _, out _, out _);
        Assert.Equal(OrphanVerdict.UnreadableDirectory, v);
        Assert.NotEqual(OrphanVerdict.NoFilesAtAll, v);
    }

    [Fact]
    public void ClassifyLeaf_ReparsePointLeaf_RefusesEvenTheFile()
    {
        // Measured: File.Delete through a junction destroys the TARGET's file, so "our DLL" seen
        // inside a junction may not be our DLL at all.
        var v = ProxyOrphanScanner.ClassifyLeaf(
            Dir(@"C:\x", files: new[] { "version.dll" }, reparse: true),
            OursIf("version.dll"), out var ours, out _, out _);

        Assert.Equal(OrphanVerdict.ReparsePointInChain, v);
        Assert.Empty(ours);
    }

    // ── Layer 2: the plan ────────────────────────────────────────────────────

    [Fact]
    public void PlanPrune_MeasuredDqxiShape_RemovesFourDirsDeepestFirst()
    {
        var (leaf, probe) = DqxiShape();

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("version.dll"), Gone);

        Assert.Equal(OrphanVerdict.Deletable, plan.Verdict);
        Assert.Equal(new[]
        {
            Common + @"\DRAGON QUEST XI S\Game\Binaries\Win64",
            Common + @"\DRAGON QUEST XI S\Game\Binaries",
            Common + @"\DRAGON QUEST XI S\Game",
            Common + @"\DRAGON QUEST XI S",
        }, plan.DirsToRemove);
        Assert.Equal(Common, plan.CeilingRoot);
        Assert.DoesNotContain(Common, plan.DirsToRemove);   // the ceiling is NEVER removed
        Assert.Single(plan.FilesToRecycle);
    }

    [Fact]
    public void PlanPrune_NoProjectLevel_StillWorks()
    {
        // The GalGun shape: <Game>\Binaries\Win64, with no project folder in between.
        string game = Common + @"\SomeGame";
        string bin = game + @"\Binaries";
        string leaf = bin + @"\Win64";
        var probe = ProbeOf(
            Dir(leaf, files: new[] { "dxgi.dll" }),
            Dir(bin, subs: new[] { "Win64" }),
            Dir(game, subs: new[] { "Binaries" }));

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("dxgi.dll"), Gone);

        Assert.Equal(OrphanVerdict.Deletable, plan.Verdict);
        Assert.Equal(3, plan.DirsToRemove.Count);
        Assert.Equal(game, plan.DirsToRemove[^1]);
    }

    [Fact]
    public void PlanPrune_ExtraWrapperLevel_StillReachesTheGameFolder()
    {
        // NEKOPALIVE's measured shape: <Game>\Package\<Proj>\Binaries\Win64.
        string game = Common + @"\NEKOPALIVE";
        string pkg = game + @"\Package";
        string proj = pkg + @"\Nekopara";
        string bin = proj + @"\Binaries";
        string leaf = bin + @"\Win64";
        var probe = ProbeOf(
            Dir(leaf, files: new[] { "version.dll" }),
            Dir(bin, subs: new[] { "Win64" }),
            Dir(proj, subs: new[] { "Binaries" }),
            Dir(pkg, subs: new[] { "Nekopara" }),
            Dir(game, subs: new[] { "Package" }));

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("version.dll"), Gone);

        Assert.Equal(OrphanVerdict.Deletable, plan.Verdict);
        Assert.Equal(game, plan.DirsToRemove[^1]);
    }

    [Fact]
    public void PlanPrune_OutsideAnySteamLibrary_IsFileOnly()
    {
        // The normal, CORRECT outcome for a non-Steam install — and it must be surfaced rather than
        // hidden, or the log-derived candidates look broken.
        string leaf = @"D:\UE_Analyze_Data\Build\Proj\Binaries\Win64";
        var probe = ProbeOf(Dir(leaf, files: new[] { "version.dll" }));

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("version.dll"), Gone);

        Assert.Equal(OrphanVerdict.FileOnly, plan.Verdict);
        Assert.Single(plan.FilesToRecycle);
        Assert.Empty(plan.DirsToRemove);
    }

    [Fact]
    public void PlanPrune_TooShallow_IsFileOnly()
    {
        // common\Binaries\Win64 — there is no game folder, so a prune would climb at the ceiling.
        string leaf = Common + @"\Binaries\Win64";
        var probe = ProbeOf(Dir(leaf, files: new[] { "version.dll" }));

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("version.dll"), Gone);

        Assert.Equal(OrphanVerdict.FileOnly, plan.Verdict);
        Assert.Empty(plan.DirsToRemove);
    }

    [Theory]
    [InlineData(OrphanVerdict.SteamManifestPresent)]
    [InlineData(OrphanVerdict.LiveContentPresent)]
    public void PlanPrune_LivenessVeto_RemovesNothingAtAll(OrphanVerdict veto)
    {
        var (leaf, probe) = DqxiShape();

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("version.dll"), Alive(veto));

        Assert.Equal(veto, plan.Verdict);
        Assert.Empty(plan.FilesToRecycle);   // not even the file
        Assert.Empty(plan.DirsToRemove);
    }

    [Fact]
    public void PlanPrune_ForeignFileInLeaf_RecyclesOursAndRemovesNoFolder()
    {
        // The end-to-end form of the shared-folder rule: ReShade (or any leftover .log/.ini) keeps
        // every folder, and our DLL still goes.
        string game = Common + @"\Shared";
        string proj = game + @"\Game";
        string bin = proj + @"\Binaries";
        string leaf = bin + @"\Win64";
        var probe = ProbeOf(
            Dir(leaf, files: new[] { "winmm.dll", "dxgi.dll", "ReShade.ini" }),
            Dir(bin, subs: new[] { "Win64" }),
            Dir(proj, subs: new[] { "Binaries" }),
            Dir(game, subs: new[] { "Game" }));

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("winmm.dll"), Gone);

        Assert.Equal(OrphanVerdict.FileOnly, plan.Verdict);
        Assert.Equal(new[] { leaf + @"\winmm.dll" }, plan.FilesToRecycle);
        Assert.Empty(plan.DirsToRemove);
        Assert.Contains("dxgi.dll", string.Join(" ", plan.Blockers), StringComparison.Ordinal);
    }

    [Fact]
    public void PlanPrune_ReparsePointMidChain_RecyclesOursButRemovesNoFolder()
    {
        // The LEAF is real, so our DLL there is genuinely ours and goes. What must not happen is
        // removing the junction directory — measured, Directory.Delete(recursive) eats it and then
        // throws, and the file walk through it hits the target.
        string game = Common + @"\G";
        string proj = game + @"\P";
        string bin = proj + @"\Binaries";
        string leaf = bin + @"\Win64";
        var probe = ProbeOf(
            Dir(leaf, files: new[] { "version.dll" }),
            Dir(bin, subs: new[] { "Win64" }, reparse: true),   // <- junction here
            Dir(proj, subs: new[] { "Binaries" }),
            Dir(game, subs: new[] { "P" }));

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("version.dll"), Gone);

        Assert.Equal(OrphanVerdict.FileOnly, plan.Verdict);
        Assert.Single(plan.FilesToRecycle);
        Assert.Empty(plan.DirsToRemove);
        Assert.Contains("Junction", string.Join(" ", plan.Blockers), StringComparison.Ordinal);
    }

    [Fact]
    public void PlanPrune_UnreadableAncestor_RecyclesOursButRemovesNoFolder()
    {
        string game = Common + @"\G";
        string proj = game + @"\P";
        string bin = proj + @"\Binaries";
        string leaf = bin + @"\Win64";
        // `proj` deliberately absent from the probe map => null => unreadable.
        var probe = ProbeOf(
            Dir(leaf, files: new[] { "version.dll" }),
            Dir(bin, subs: new[] { "Win64" }),
            Dir(game, subs: new[] { "P" }));

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("version.dll"), Gone);

        Assert.Equal(OrphanVerdict.FileOnly, plan.Verdict);
        Assert.Single(plan.FilesToRecycle);
        Assert.Empty(plan.DirsToRemove);
    }

    [Fact]
    public void PlanPrune_KnownLiveBinariesDir_Refused()
    {
        var (leaf, probe) = DqxiShape();
        var live = new HashSet<string>(new[] { leaf }, StringComparer.OrdinalIgnoreCase);

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, live, probe, OursIf("version.dll"), Gone);

        Assert.Equal(OrphanVerdict.LiveGameFolder, plan.Verdict);
        Assert.Empty(plan.FilesToRecycle);
    }

    [Fact]
    public void PlanPrune_CaseDifferingCeiling_StillMatches()
    {
        var (leaf, probe) = DqxiShape();

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { @"e:\steamlibrary\STEAMAPPS\Common" }, Empty(),
            probe, OursIf("version.dll"), Gone);

        Assert.Equal(OrphanVerdict.Deletable, plan.Verdict);
    }

    [Fact]
    public void PlanPrune_SharedPrefixCeiling_DoesNotMatch()
    {
        var (leaf, probe) = DqxiShape();

        // E:\SteamLibraryBackup must not claim a path under E:\SteamLibrary.
        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { @"E:\SteamLibraryBackup\steamapps\common" }, Empty(),
            probe, OursIf("version.dll"), Gone);

        Assert.Equal(OrphanVerdict.FileOnly, plan.Verdict);
        Assert.Empty(plan.DirsToRemove);
    }

    // ── Layer 3: source parsers ──────────────────────────────────────────────

    [Fact]
    public void ParseDllLoadedProcessPath_MeasuredRealLine()
    {
        // Verbatim from a real init-0.log. The line has three " | " separators of its own and the
        // path contains spaces, which is why the parser is index-based rather than a split.
        const string line =
            @"[2026-07-27 09:50:44.881] [INFO] [INIT] UE5Dumper DLL loaded | build: 1.0.0.2432 " +
            @"871ed78-dirty (Release, 2026-07-26T13:41:27) | process: E:\SteamLibrary\steamapps\" +
            @"common\DRAGON QUEST XI S\Game\Binaries\Win64\DRAGON QUEST XI S.exe [PID=86276]";

        Assert.Equal(
            @"E:\SteamLibrary\steamapps\common\DRAGON QUEST XI S\Game\Binaries\Win64\DRAGON QUEST XI S.exe",
            ProxyOrphanScanner.ParseDllLoadedProcessPath(line));
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    [InlineData("no marker here")]
    [InlineData("... | process: ")]                 // truncated, no [PID=
    [InlineData("... | process: C:\\a.exe")]        // missing tail
    public void ParseDllLoadedProcessPath_MalformedReturnsNull(string? line)
        => Assert.Null(ProxyOrphanScanner.ParseDllLoadedProcessPath(line));

    [Fact]
    public void ParseDeployedTargetPath_KeepsNonAsciiIntact()
    {
        // The managed deploy log writes real Unicode, unlike the DLL banner which went through the
        // ANSI code page and turned a trademark sign into '?'.
        const string line =
            @"[2026-07-30 12:00:00.000] [INFO] [ProxyDeploy] Deployed Version to EVERSPACE™ 2: " +
            @"D:\SteamLibrary\steamapps\common\EVERSPACE™ 2\ES2\Binaries\Win64\version.dll";

        Assert.Equal(
            @"D:\SteamLibrary\steamapps\common\EVERSPACE™ 2\ES2\Binaries\Win64\version.dll",
            ProxyOrphanScanner.ParseDeployedTargetPath(line));
    }

    [Fact]
    public void ParseDeployedTargetPath_NonDllIsIgnored()
        => Assert.Null(ProxyOrphanScanner.ParseDeployedTargetPath("Deployed something to X: C:\\a.txt"));

    [Fact]
    public void CandidateDirsFrom_DedupesAndTakesTheDirectory()
    {
        string[] lines =
        {
            @"x | process: C:\g\P\Binaries\Win64\a.exe [PID=1]",
            @"x | process: C:\g\P\Binaries\Win64\b.exe [PID=2]",   // same folder -> one candidate
            @"x | process: C:\other\Binaries\Win64\c.exe [PID=3]",
            @"garbage",
            @"x | process: ..\evil\Binaries\Win64\d.exe [PID=4]",  // traversal -> dropped
        };

        var dirs = ProxyOrphanScanner.CandidateDirsFrom(lines, isDllLog: true);

        Assert.Equal(new[] { @"C:\g\P\Binaries\Win64", @"C:\other\Binaries\Win64" }, dirs);
    }

    // ── Outcome wording ──────────────────────────────────────────────────────

    [Fact]
    public void ResolveRemovalOutcome_LockedFileIsAFailureAndNamesIt()
    {
        var (ok, msg) = ProxyOrphanScanner.ResolveRemovalOutcome(
            0, 0, 0, 4, new[] { "version.dll" }, Array.Empty<string>(), Array.Empty<string>());

        Assert.False(ok);
        Assert.Contains("version.dll", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveRemovalOutcome_ReadOnlyIsReportedDistinctly()
    {
        var (ok, msg) = ProxyOrphanScanner.ResolveRemovalOutcome(
            0, 0, 0, 4, Array.Empty<string>(), new[] { "version.dll" }, Array.Empty<string>());

        Assert.False(ok);
        Assert.Contains("Read-only", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveRemovalOutcome_PartialPruneIsStillSuccessButSaysSo()
    {
        var (ok, msg) = ProxyOrphanScanner.ResolveRemovalOutcome(
            1, 0, 2, 4, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(ok);
        Assert.Contains("2 of 4", msg, StringComparison.Ordinal);
    }

    [Fact]
    public void ResolveRemovalOutcome_FileOnlyRowDoesNotMentionFolders()
    {
        var (ok, msg) = ProxyOrphanScanner.ResolveRemovalOutcome(
            1, 0, 0, 0, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(ok);
        Assert.Contains("Recycle Bin", msg, StringComparison.Ordinal);
        Assert.DoesNotContain("folder", msg, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// B12 — the exact counts from the finding. <c>ProxyDeployService</c> counts a
    /// vanished file into <c>allFilesGone</c> and then still prunes the chain, so this
    /// combination is reachable: the file was already gone, and four directories really
    /// were removed. The old wording put "Already gone — nothing left to remove." in
    /// success green over a run that had just deleted four folders.
    /// </summary>
    [Fact]
    public void ResolveRemovalOutcome_AlreadyGoneStillReportsFoldersThatWereRemoved()
    {
        var (ok, msg) = ProxyOrphanScanner.ResolveRemovalOutcome(
            filesRecycled: 0, filesAlreadyGone: 1, dirsRemoved: 4, dirsPlanned: 4,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(ok);
        Assert.Contains("4", msg, StringComparison.Ordinal);
        Assert.Contains("folder", msg, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("nothing left to remove", msg, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>The other half: with no directories removed the original wording is
    /// correct and must be kept.</summary>
    [Fact]
    public void ResolveRemovalOutcome_AlreadyGoneWithNoFoldersKeepsTheShortMessage()
    {
        var (ok, msg) = ProxyOrphanScanner.ResolveRemovalOutcome(
            filesRecycled: 0, filesAlreadyGone: 1, dirsRemoved: 0, dirsPlanned: 4,
            Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        Assert.True(ok);
        Assert.Contains("Already gone", msg, StringComparison.Ordinal);
    }

    // ── The dry-run report ───────────────────────────────────────────────────

    private static OrphanProxy SampleRow(bool selected = true) => new()
    {
        DllPath = Common + @"\G\Game\Binaries\Win64\version.dll",
        DllDirectory = Common + @"\G\Game\Binaries\Win64",
        DllNames = "version.dll",
        AuthorisedFiles = new[] { Common + @"\G\Game\Binaries\Win64\version.dll" },
        SizeBytes = 2_789_376,
        FileVersion = "1.0.0.2518",
        ChainDirs = new[]
        {
            Common + @"\G\Game\Binaries\Win64",
            Common + @"\G\Game\Binaries",
            Common + @"\G\Game",
            Common + @"\G",
        },
        TopmostRemovableDir = Common + @"\G",
        Source = OrphanScanSources.SteamShapeScan | OrphanScanSources.DllLoadLog,
        Verdict = OrphanVerdict.Deletable,
        EvidenceText = "no executable survives anywhere under the game folder",
        IsSelected = selected,
    };

    [Fact]
    public void BuildReport_ListsEveryPathAndTheFileDetail()
    {
        string txt = ProxyOrphanScanner.BuildReport(
            new[] { SampleRow() }, "2026-07-30 20:00:00", "2519");

        foreach (string d in SampleRow().ChainDirs)
            Assert.Contains(d, txt, StringComparison.Ordinal);
        Assert.Contains("version.dll", txt, StringComparison.Ordinal);
        Assert.Contains("1.0.0.2518", txt, StringComparison.Ordinal);
        Assert.Contains("2,789,376", txt, StringComparison.Ordinal);
        Assert.Contains("Steam library scan", txt, StringComparison.Ordinal);
        Assert.Contains("our DLL load log", txt, StringComparison.Ordinal);
        // The ceiling must be named as explicitly NOT touched — stating the boundary is half the
        // reassurance, and its absence is what makes a delete list unauditable.
        Assert.Contains("NOT touched: " + Common, txt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReport_StatesItChangesNothingAndCanShrinkButNeverGrow()
    {
        // These three sentences are the entire reason a separate Report action exists. If a refactor
        // drops them the button becomes a list with no context, which is worse than no button.
        string txt = ProxyOrphanScanner.BuildReport(
            new[] { SampleRow() }, "2026-07-30 20:00:00", "2519");

        Assert.Contains("DRY RUN", txt, StringComparison.Ordinal);
        Assert.Contains("RECYCLE BIN", txt, StringComparison.Ordinal);
        Assert.Contains("ONE LEVEL AT A TIME", txt, StringComparison.Ordinal);
        Assert.Contains("never be larger", txt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReport_MarksTickedRowsAndCountsThem()
    {
        string ticked = ProxyOrphanScanner.BuildReport(
            new[] { SampleRow(selected: true) }, "t", "1");
        string unticked = ProxyOrphanScanner.BuildReport(
            new[] { SampleRow(selected: false) }, "t", "1");

        Assert.Contains("[x] 1.", ticked, StringComparison.Ordinal);
        Assert.Contains("1 currently ticked", ticked, StringComparison.Ordinal);
        Assert.Contains("[ ] 1.", unticked, StringComparison.Ordinal);
        Assert.Contains("0 currently ticked", unticked, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReport_EmptyResultSaysSoWithoutAnEmptyTable()
    {
        string txt = ProxyOrphanScanner.BuildReport(
            Array.Empty<OrphanProxy>(), "2026-07-30 20:00:00", "2519");

        Assert.Contains("No leftover proxy DLLs were found", txt, StringComparison.Ordinal);
        Assert.DoesNotContain("currently ticked", txt, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReport_FileOnlyRowSaysNoFolderWouldGo()
    {
        var row = new OrphanProxy
        {
            DllPath = @"D:\Tools\Game\Binaries\Win64\winmm.dll",
            DllDirectory = @"D:\Tools\Game\Binaries\Win64",
            DllNames = "winmm.dll",
            AuthorisedFiles = new[] { @"D:\Tools\Game\Binaries\Win64\winmm.dll" },
            ChainDirs = Array.Empty<string>(),
            Verdict = OrphanVerdict.FileOnly,
            Blockers = new[] { "Left in place because other files are here: dxgi.dll" },
        };

        string txt = ProxyOrphanScanner.BuildReport(new[] { row }, "t", "1");

        Assert.Contains("No folder would be removed", txt, StringComparison.Ordinal);
        Assert.Contains("dxgi.dll", txt, StringComparison.Ordinal);
    }

    // ── Report retention ─────────────────────────────────────────────────────

    private static readonly DateTime Now = new(2026, 7, 30, 12, 0, 0);

    [Fact]
    public void SelectExpiredReports_KeepsTheNewestWhateverItsAge()
    {
        // The one case where this cleanup could destroy something wanted: a single report made months
        // ago and kept deliberately. One guard removes it.
        var files = new[] { (@"C:\r\old.txt", Now.AddDays(-400)) };

        Assert.Empty(ProxyOrphanScanner.SelectExpiredReports(files, Now, 30));
    }

    [Fact]
    public void SelectExpiredReports_KeepsTheNewestEvenWhenEverythingIsExpired()
    {
        var files = new[]
        {
            (@"C:\r\a.txt", Now.AddDays(-100)),
            (@"C:\r\b.txt", Now.AddDays(-90)),
            (@"C:\r\newest.txt", Now.AddDays(-80)),
        };

        var expired = ProxyOrphanScanner.SelectExpiredReports(files, Now, 30);

        Assert.Equal(2, expired.Count);
        Assert.DoesNotContain(@"C:\r\newest.txt", expired);
    }

    [Fact]
    public void SelectExpiredReports_KeepsEverythingInsideTheWindow()
    {
        // A burst of reports in one before/after session must all survive — that is exactly the case
        // a "keep the last N" rule would get wrong.
        var files = Enumerable.Range(0, 8)
            .Select(i => ($@"C:\r\{i}.txt", Now.AddMinutes(-i)))
            .ToArray();

        Assert.Empty(ProxyOrphanScanner.SelectExpiredReports(files, Now, 30));
    }

    [Fact]
    public void SelectExpiredReports_PrunesOnlyPastTheCutoff()
    {
        var files = new[]
        {
            (@"C:\r\fresh.txt", Now.AddDays(-1)),
            (@"C:\r\edge.txt", Now.AddDays(-30).AddMinutes(1)),   // just inside
            (@"C:\r\stale.txt", Now.AddDays(-31)),
        };

        var expired = ProxyOrphanScanner.SelectExpiredReports(files, Now, 30);

        Assert.Equal(new[] { @"C:\r\stale.txt" }, expired);
    }

    [Fact]
    public void DescribeSources_NamesEachFlag()
    {
        Assert.Equal("Steam library scan",
            ProxyOrphanScanner.DescribeSources(OrphanScanSources.SteamShapeScan));
        Assert.Equal("Steam library scan, our deploy log",
            ProxyOrphanScanner.DescribeSources(
                OrphanScanSources.SteamShapeScan | OrphanScanSources.DeployLog));
        Assert.Equal("(unknown)", ProxyOrphanScanner.DescribeSources(OrphanScanSources.None));
    }

    // ── Verdict coverage guard ───────────────────────────────────────────────

    [Fact]
    public void EveryVerdictIsDistinct()
    {
        // Cheap guard that a future edit does not collapse two verdicts onto one value, which would
        // silently make the UI show the wrong explanation for a refusal.
        var values = Enum.GetValues<OrphanVerdict>();
        Assert.Equal(values.Length, values.Distinct().Count());
    }

    private static IReadOnlySet<string> Empty()
        => new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// A CLEAN scan must still produce a report. Without the file, "scanned everything and
    /// found nothing" and "the scan never ran, or looked in the wrong place, or failed
    /// silently" are indistinguishable a week later — and the VM used to make the empty text
    /// unreachable by gating Report on `Orphans.Count > 0`.
    /// </summary>
    [Fact]
    public void BuildReport_EmptyScanStillReportsWhatItExamined()
    {
        string txt = ProxyOrphanScanner.BuildReport(
            Array.Empty<OrphanProxy>(), "2026-08-04 22:00:00", "2633", foldersExamined: 1487);

        Assert.Contains("No leftover proxy DLLs were found.", txt, StringComparison.Ordinal);
        // The evidence half: it has to say what it LOOKED AT.
        Assert.Contains("1487 folder(s) were examined", txt, StringComparison.Ordinal);
        Assert.Contains("DRY RUN", txt, StringComparison.Ordinal);
    }

    /// <summary>Unknown count (-1) omits the line rather than printing a misleading "0".</summary>
    [Fact]
    public void BuildReport_UnknownExaminedCountOmitsTheLine()
    {
        string txt = ProxyOrphanScanner.BuildReport(
            Array.Empty<OrphanProxy>(), "t", "1");
        Assert.Contains("No leftover proxy DLLs were found.", txt, StringComparison.Ordinal);
        Assert.DoesNotContain("folder(s) were examined", txt, StringComparison.Ordinal);
    }

    // ── The Recycle-Bin refusal (B13/B41), measured end to end 2026-08-12 ─────
    //
    // The rig: a real UE game copied to a fixed 10 GB volume with NukeOnDelete=1, a proxy deployed
    // to it, then the game deleted so only our version.dll survived. With the bin DISABLED the scan
    // reported "No leftover proxy DLLs found (23 folder(s) examined)" while the file sat there; with
    // ONLY that registry value flipped, the same folder surfaced as a row. Two separate defects made
    // that happen, and both are pinned below.

    private static RecyclerProbe NoBin => _ => false;
    private static RecyclerProbe HasBin => _ => true;

    [Fact]
    public void PlanPrune_NoRecycleBin_RefusesButStillNamesOurFile()
    {
        // A refusal that cannot say WHICH file it is about is not actionable by hand either, so the
        // plan carries the file list even though the verdict authorises nothing.
        var (leaf, probe) = DqxiShape();

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("version.dll"), Gone, NoBin);

        Assert.Equal(OrphanVerdict.NotOnFixedDrive, plan.Verdict);
        Assert.Single(plan.FilesToRecycle);
        Assert.Equal(leaf + @"\version.dll", plan.FilesToRecycle[0]);
        Assert.Empty(plan.DirsToRemove);              // nothing is authorised
        Assert.Contains(plan.Blockers, b => b.Contains("PERMANENT", StringComparison.Ordinal));
    }

    [Fact]
    public void PlanPrune_WorkingRecycleBin_SameShapeIsStillDeletable()
    {
        // The NEGATIVE CONTROL for the test above: identical inputs, bin present. Without this a
        // probe that refused everything would look like a correct refusal.
        var (leaf, probe) = DqxiShape();

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("version.dll"), Gone, HasBin);

        Assert.Equal(OrphanVerdict.Deletable, plan.Verdict);
        Assert.Equal(4, plan.DirsToRemove.Count);
    }

    [Fact]
    public void PlanPrune_NoRecycleBin_FolderWithNothingOfOurs_IsNotARecyclerRefusal()
    {
        // ORDERING. The recycler question used to be asked BEFORE the folder was even read, so a
        // volume with no bin manufactured a refusal for folders this feature has no business in —
        // and, after the surface fix, would have shown the user a row about someone else's DLL.
        string leaf = Common + @"\SomeGame\Proj\Binaries\Win64";
        var probe = ProbeOf(Dir(leaf, files: new[] { "d3d12.dll" }));

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("version.dll"), Gone, NoBin);

        Assert.Equal(OrphanVerdict.ForeignFilePresent, plan.Verdict);
        Assert.NotEqual(OrphanVerdict.NotOnFixedDrive, plan.Verdict);
        Assert.Empty(plan.FilesToRecycle);
    }

    [Fact]
    public void PlanPrune_NoRecycleBin_UnreadableFolder_StillReportsTheFolder()
    {
        // Same ordering rule at the other extreme: "could not read it" outranks "no bin here",
        // because the unreadable-directory refusal is the one that must never read as "empty".
        string leaf = Common + @"\SomeGame\Proj\Binaries\Win64";

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), ProbeOf(), OursIf("version.dll"), Gone, NoBin);

        Assert.Equal(OrphanVerdict.UnreadableDirectory, plan.Verdict);
    }

    [Fact]
    public void PlanPrune_NoRecyclerProbeSupplied_IsUnchanged()
    {
        // The probe is optional; omitting it must not start refusing things.
        var (leaf, probe) = DqxiShape();

        var plan = ProxyOrphanScanner.PlanPrune(
            leaf, new[] { Common }, Empty(), probe, OursIf("version.dll"), Gone);

        Assert.Equal(OrphanVerdict.Deletable, plan.Verdict);
    }

    // ── The surface/actionable split — the defect the measurement exposed ─────

    [Fact]
    public void ShouldSurface_KeepsTheNoRecycleBinRefusal()
    {
        // THE regression guard. Dropping this verdict is what made B13/B41's own PASS criterion
        // unobservable: the refusal was computed, then discarded, and the user was told "No leftover
        // proxy DLLs found" on the one kind of volume where a hand-delete cannot be undone.
        Assert.True(OrphanVerdictRules.ShouldSurface(OrphanVerdict.NotOnFixedDrive));
        Assert.False(OrphanVerdictRules.IsActionable(OrphanVerdict.NotOnFixedDrive));
    }

    [Theory]
    [InlineData(OrphanVerdict.Deletable)]
    [InlineData(OrphanVerdict.FileOnly)]
    public void ShouldSurface_KeepsEverythingActionable(OrphanVerdict v)
    {
        Assert.True(OrphanVerdictRules.IsActionable(v));
        Assert.True(OrphanVerdictRules.ShouldSurface(v));
    }

    [Theory]
    [InlineData(OrphanVerdict.ForeignFilePresent)]   // none of our files
    [InlineData(OrphanVerdict.UnreadableDll)]
    [InlineData(OrphanVerdict.UnreadableDirectory)]
    [InlineData(OrphanVerdict.NoFilesAtAll)]
    [InlineData(OrphanVerdict.ReparsePointInChain)]
    [InlineData(OrphanVerdict.LiveGameFolder)]       // the game is installed, not a leftover
    [InlineData(OrphanVerdict.LiveContentPresent)]
    [InlineData(OrphanVerdict.SteamManifestPresent)]
    [InlineData(OrphanVerdict.NotUeShape)]
    public void ShouldSurface_DropsRefusalsThatHoldNothingOfOurs(OrphanVerdict v)
    {
        // The other direction, and it matters just as much: "surface the refusals too" must not
        // become "show a row for every folder we looked at".
        Assert.False(OrphanVerdictRules.ShouldSurface(v));
        Assert.False(OrphanVerdictRules.IsActionable(v));
    }

    [Fact]
    public void OrphanRow_NoRecycleBin_IsNotSelectableAndSaysWhy()
    {
        var row = new OrphanProxy
        {
            DllPath = @"T:\Light Maze\LightMaze\Binaries\Win64\version.dll",
            DllDirectory = @"T:\Light Maze\LightMaze\Binaries\Win64",
            DllNames = "version.dll",
            AuthorisedFiles = Array.Empty<string>(),
            ChainDirs = Array.Empty<string>(),
            Verdict = OrphanVerdict.NotOnFixedDrive,
            Blockers = new[] { "This volume has no working Recycle Bin … would be PERMANENT." },
        };

        Assert.False(row.IsActionable);                       // checkbox is bound to this
        Assert.Empty(row.AuthorisedFiles);                    // authorises nothing, even if ticked
        Assert.Contains("PERMANENT", row.ActionSummary, StringComparison.Ordinal);
    }

    [Fact]
    public void BuildReport_BlockedRow_DoesNotClaimTheFileWillBeRecycled()
    {
        var row = new OrphanProxy
        {
            DllPath = @"T:\G\P\Binaries\Win64\version.dll",
            DllDirectory = @"T:\G\P\Binaries\Win64",
            DllNames = "version.dll",
            AuthorisedFiles = Array.Empty<string>(),
            ChainDirs = Array.Empty<string>(),
            Verdict = OrphanVerdict.NotOnFixedDrive,
            Blockers = new[] { "This volume has no working Recycle Bin, so a delete would be PERMANENT." },
            IsSelected = true,   // even ticked, it is not authorised
        };

        string txt = ProxyOrphanScanner.BuildReport(new[] { row }, "t", "1", foldersExamined: 23);

        Assert.Contains("NOT removable", txt, StringComparison.Ordinal);
        Assert.DoesNotContain("to be recycled", txt, StringComparison.Ordinal);
        Assert.Contains("[ ]", txt, StringComparison.Ordinal);          // never [x]
        Assert.Contains("0 currently ticked", txt, StringComparison.Ordinal);
        Assert.Contains("PERMANENT", txt, StringComparison.Ordinal);    // the reason survives
    }
}
