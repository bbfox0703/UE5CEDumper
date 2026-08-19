using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.Win32;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.Services;

/// <summary>
/// Detects Steam-installed UE games and manages proxy DLL deployment.
/// All file system and registry calls are encapsulated here.
/// </summary>
public sealed class ProxyDeployService : IProxyDeployService
{
    private readonly ILoggingService _log;
    private readonly IPlatformService _platform;

    public ProxyDeployService(ILoggingService log, IPlatformService platform)
    {
        _log = log;
        _platform = platform;
    }

    // ────────────────────────────────────────────────────────────────
    // Steam Library Detection
    // ────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<string>> GetSteamLibraryFoldersAsync(CancellationToken ct = default)
        => Task.Run(GetSteamLibraryFoldersCore, ct);

    /// <summary>
    /// Synchronous core of <see cref="GetSteamLibraryFoldersAsync"/>. Extracted so callers that are
    /// ALREADY on a worker thread can use it without nesting a second <c>Task.Run</c> or blocking on
    /// a task — the leftover-proxy scan needs the library list from inside its own worker.
    /// </summary>
    private IReadOnlyList<string> GetSteamLibraryFoldersCore()
    {
            var result = new List<string>();
            try
            {
                string? steamPath = GetSteamInstallPath();
                if (steamPath == null)
                {
                    _log.Warn("ProxyDeploy", "Steam installation not found");
                    return (IReadOnlyList<string>)result;
                }

                string vdfPath = Path.Combine(steamPath, Constants.SteamLibraryFoldersVdf);
                if (!File.Exists(vdfPath))
                {
                    _log.Warn("ProxyDeploy", $"libraryfolders.vdf not found: {vdfPath}");
                    // Fallback: use Steam path itself as the single library
                    result.Add(steamPath);
                    return (IReadOnlyList<string>)result;
                }

                string vdfContent = File.ReadAllText(vdfPath);
                var paths = VdfParser.ParseLibraryFolders(vdfContent, out string? vdfError);

                // A structural fault stops extraction, so "0 libraries" below could mean
                // either "Steam lists none" or "the file is broken". Say which. (AC12)
                if (vdfError != null)
                    _log.Warn("ProxyDeploy",
                        $"libraryfolders.vdf is malformed ({vdfError}) — using the {paths.Count} " +
                        $"library folder(s) read before the fault: {vdfPath}");

                if (paths.Count == 0)
                {
                    _log.Warn("ProxyDeploy", "VDF parse returned 0 libraries, using Steam path as fallback");
                    result.Add(steamPath);
                }
                else
                {
                    // Validate paths exist
                    foreach (string p in paths)
                    {
                        if (Directory.Exists(p))
                            result.Add(p);
                        else
                            _log.Warn("ProxyDeploy", $"Steam library path does not exist: {p}");
                    }
                }

                _log.Info("ProxyDeploy", $"Found {result.Count} Steam library folder(s)");
            }
            catch (Exception ex)
            {
                _log.Error("ProxyDeploy", $"GetSteamLibraryFolders failed: {ex.Message}");
            }

            return (IReadOnlyList<string>)result;
    }

    private static string? GetSteamInstallPath()
    {
        try
        {
            using var key = Registry.LocalMachine.OpenSubKey(Constants.SteamRegistryPath);
            if (key?.GetValue(Constants.SteamRegistryKey) is string path && Directory.Exists(path))
                return path;
        }
        catch
        {
            // Registry access may fail — fall through to default
        }

        // Fallback to default Steam path
        if (Directory.Exists(Constants.SteamDefaultPath))
            return Constants.SteamDefaultPath;

        return null;
    }

    // ────────────────────────────────────────────────────────────────
    // UE Game Detection
    // ────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<DetectedGame>> FindUeGamesAsync(
        IReadOnlyList<string> libraryPaths, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            var games = new List<DetectedGame>();
            var seenBinDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (string libPath in libraryPaths)
            {
                ct.ThrowIfCancellationRequested();

                string commonDir = Path.Combine(libPath, Constants.SteamAppsCommon);
                if (!Directory.Exists(commonDir))
                    continue;

                try
                {
                    foreach (string gameDir in Directory.EnumerateDirectories(commonDir))
                    {
                        ct.ThrowIfCancellationRequested();
                        ScanGameFolder(gameDir, games, seenBinDirs);
                    }
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    _log.Warn("ProxyDeploy", $"Error scanning {commonDir}: {ex.Message}");
                }
            }

            _log.Info("ProxyDeploy", $"Found {games.Count} UE game(s)");
            return (IReadOnlyList<DetectedGame>)games;
        }, ct);
    }

    private void ScanGameFolder(string gameDir, List<DetectedGame> games, HashSet<string> seenBinDirs)
    {
        string gameName = Path.GetFileName(gameDir);

        // Two-tier search to handle three different UE shipping layouts:
        //   1. Monolithic (DQ7R, Hogwarts, Stray, etc.)
        //         <Game>\<Sub>\Binaries\Win64\<Game>-Win64-Shipping.exe   ← real
        //         <Game>\Engine\Binaries\Win64\CrashReportClient.exe       ← stub only
        //
        //   2. Hybrid (StellarBlade, NMKART, Palworld, Titan Quest II)
        //         <Game>\<Sub>\Binaries\Win64\<Game>-Win64-Shipping.exe   ← real
        //         <Game>\Engine\Binaries\Win64\<Game>-Win64-Shipping.exe  ← stub launcher
        //
        //   3. Pure modular (Satisfactory)
        //         <Game>\<Sub>\Binaries\Win64\  ← no .exe at all (only .modules + DLLs)
        //         <Game>\Engine\Binaries\Win64\<Game>-Win64-Shipping.exe  ← real launcher
        //
        //   4. Wrapped (NEKOPALIVE) — an extra folder between the game root and
        //      the project, and the exe is not named *-Win64-Shipping:
        //         <Game>\Package\<Sub>\Binaries\Win64\Nekopara.exe   ← real
        //         <Game>\Package\Engine\Binaries\Win64\CrashReportClient.exe
        //
        // Walking Engine\ unconditionally produces phantom rows for layouts 1+2
        // (the user sees both rows for the same game). Skipping Engine\ kills
        // layout 3 (Satisfactory). Solution: try primary roots first; only fall
        // back to Engine\Binaries\Win64\ when primary contributed no rows for
        // this gameDir.
        //
        // Layout 4 is why this searches to a bounded DEPTH rather than exactly one
        // level down. A single level finds <Game>\<Sub>\Binaries\Win64 and misses
        // <Game>\Package\<Sub>\Binaries\Win64 entirely — that is a whole game the
        // scan never sees, and the Engine fallback misses it too because the
        // Engine folder is not a direct child either.
        var primary = new List<(string Dir, int Depth)>();
        var engineRoots = new List<(string Dir, int Depth)>();
        CollectBinariesRoots(gameDir, gameDir, 0, primary, engineRoots);

        // SHALLOWEST DEPTH WINS. Depth is scanned ascending and the first depth that yields any
        // row stops the search — the same "primary first, fallback only if empty" shape as the
        // Engine tier below, applied to nesting.
        //
        // Without this, the depth-3 search turns bonus content into phantom rows: P3R ships
        // <Game>\P3R\Binaries\Win64 (depth 1, the real game) alongside
        // <Game>\Artbook\P3R_Artbook\Binaries\Win64 and <Game>\Soundtrack\P3R_Soundtrack\... —
        // and those two are genuinely UE apps with their own Engine folder, so no content-based
        // filter separates them. Depth does: the real game is shallower. A wrapped layout like
        // NEKOPALIVE has nothing at depth 1 at all, so it still reaches its depth-2 project.
        int gamesBefore = games.Count;
        foreach (var group in primary.GroupBy(r => r.Depth).OrderBy(g => g.Key))
        {
            foreach (var (root, _) in group)
                ScanBinariesDir(gameName, gameDir, root, games, seenBinDirs);
            if (games.Count != gamesBefore)
                break;
        }

        // Engine fallback: only walked when primary yielded zero rows for this
        // gameDir. Catches pure-modular layouts like Satisfactory where the
        // real launcher .exe lives in <Game>\Engine\Binaries\Win64\.
        if (games.Count == gamesBefore)
            foreach (var group in engineRoots.GroupBy(r => r.Depth).OrderBy(g => g.Key))
            {
                foreach (var (root, _) in group)
                    ScanBinariesDir(gameName, gameDir, root, games, seenBinDirs);
                if (games.Count != gamesBefore)
                    break;
            }
    }

    /// <summary>Deepest wrapper level below a game root that is still searched for a
    /// <c>Binaries\Win64</c>. 2 covers the observed <c>&lt;Game&gt;\Package\&lt;Sub&gt;\</c>
    /// wrapping with one level spare; going deeper buys nothing and costs directory walks
    /// across every game in the library.</summary>
    private const int MaxBinariesSearchDepth = 3;

    /// <summary>Directory names never worth descending into while looking for a
    /// <c>Binaries\Win64</c>. <c>Content</c> is the one that matters — a shipped game's
    /// content tree can hold thousands of folders, and none of them is a binaries root.</summary>
    private static readonly string[] BinariesSearchSkipDirs =
        { "Binaries", "Content", "Saved", "Intermediate", "Config", "DerivedDataCache", "Plugins" };

    /// <summary>
    /// Walk down from <paramref name="dir"/> collecting every directory that owns a
    /// <c>Binaries\Win64</c>, split into Engine-side and everything else so the caller can keep
    /// the two-tier "primary first, Engine only as fallback" rule that stops modular games
    /// producing two rows.
    /// </summary>
    private static void CollectBinariesRoots(
        string dir, string gameDir, int depth,
        List<(string Dir, int Depth)> primary, List<(string Dir, int Depth)> engineRoots)
    {
        // A root is worth recording whether or not it has a Binaries child — ScanBinariesDir
        // re-checks — but only descend while there is depth left. Depth travels with the root so
        // the caller can prefer shallower ones.
        bool underEngine = IsUnderEngineFolder(dir, gameDir);
        (underEngine ? engineRoots : primary).Add((dir, depth));

        if (depth >= MaxBinariesSearchDepth)
            return;

        try
        {
            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                string name = Path.GetFileName(sub);
                if (BinariesSearchSkipDirs.Contains(name, StringComparer.OrdinalIgnoreCase))
                    continue;
                CollectBinariesRoots(sub, gameDir, depth + 1, primary, engineRoots);
            }
        }
        catch
        {
            // Permission error / reparse point — this branch just contributes nothing.
        }
    }

    /// <summary>True when any path component between the game root and <paramref name="dir"/>
    /// (inclusive) is named <c>Engine</c>. Checking the whole relative path rather than the
    /// immediate parent is what makes the Engine tier work under a wrapper folder.</summary>
    private static bool IsUnderEngineFolder(string dir, string gameDir)
    {
        string rel = Path.GetRelativePath(gameDir, dir);
        if (rel == ".") return false;
        return rel.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                  .Any(part => string.Equals(part, "Engine", StringComparison.OrdinalIgnoreCase));
    }

    private void ScanBinariesDir(
        string gameName, string gameDir, string root,
        List<DetectedGame> games, HashSet<string> seenBinDirs)
    {
        string binDir = Path.Combine(root, "Binaries", "Win64");
        if (!Directory.Exists(binDir))
            return;

        // Dedup by BinariesDir
        if (!seenBinDirs.Add(binDir))
            return;

        try
        {
            // Find executables in Binaries/Win64. Stub .exes (CrashReportClient,
            // launcher helpers) are filtered up front so they never win against
            // a real game exe even when sorted earlier alphabetically.
            bool foundUe = false;
            foreach (string exePath in Directory.EnumerateFiles(binDir, "*.exe"))
            {
                string exeName = Path.GetFileName(exePath);
                if (IsKnownStubExe(exeName))
                    continue;

                // Standard UE: xxx-Win64-Shipping.exe
                bool isStandardUe = exeName.Contains("-Win64-Shipping", StringComparison.OrdinalIgnoreCase);

                // Check for Engine folder nearby (UE indicator)
                bool hasEngineFolder = Directory.Exists(Path.Combine(root, "Engine"))
                                    || Directory.Exists(Path.Combine(gameDir, "Engine"));

                if (isStandardUe || hasEngineFolder)
                {
                    games.Add(new DetectedGame
                    {
                        Name = gameName,
                        ExePath = exePath,
                        BinariesDir = binDir,
                        UeVersion = TryDetectUeVersion(exePath),
                    });
                    foundUe = true;
                    break; // One exe per BinariesDir is enough
                }
            }

            // Fallback: any non-stub exe in Binaries/Win64 is likely a UE
            // game even without standard naming. Same stub filter applies
            // so we never surface CrashReportClient as a "game".
            if (!foundUe)
            {
                foreach (string exePath in Directory.EnumerateFiles(binDir, "*.exe"))
                {
                    string exeName = Path.GetFileName(exePath);
                    if (IsKnownStubExe(exeName))
                        continue;

                    games.Add(new DetectedGame
                    {
                        Name = gameName,
                        ExePath = exePath,
                        BinariesDir = binDir,
                        UeVersion = TryDetectUeVersion(exePath),
                    });
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _log.Warn("ProxyDeploy", $"Error scanning {binDir}: {ex.Message}");
        }
    }

    /// <summary>
    /// Known non-game UE helper executables that ship inside Binaries/Win64
    /// folders. These would otherwise be picked up as the "game .exe" on
    /// modular UE builds where Engine/Binaries/Win64 holds both the real
    /// game launcher and CrashReportClient side-by-side (e.g. Satisfactory).
    /// Case-insensitive match.
    /// </summary>
    internal static bool IsKnownStubExe(string exeName)
    {
        // CrashReportClient ships next to the real game exe on modular builds.
        // UnrealEditor / UE4Editor / UnrealFrontend only matter for the generic
        // drive walk (a Steam library never contains an editor install) — filter
        // them so an engine/editor tree is never surfaced as a "game".
        return string.Equals(exeName, "CrashReportClient.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exeName, "UnrealEditor.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exeName, "UE4Editor.exe", StringComparison.OrdinalIgnoreCase)
            || string.Equals(exeName, "UnrealFrontend.exe", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Build the multi-proxy redundancy warning from the list of OUR proxy
    /// DLLs actually present in a game folder, or null when fewer than two
    /// coexist. Pure (no IO) so the rule is unit-testable.
    ///
    /// Only 2+ simultaneously-deployed proxies are a real conflict ("only one
    /// will activate at runtime"). Exactly one deployed proxy — of ANY type —
    /// is the normal state and must NOT warn, regardless of which proxy type
    /// the UI currently has selected. N-proxy-safe.
    /// </summary>
    internal static string? BuildConflictMessage(IReadOnlyList<string> deployedProxyNames)
    {
        if (deployedProxyNames.Count < 2)
            return null;

        return $"Multiple proxy DLLs deployed ({string.Join(", ", deployedProxyNames)})"
             + " — only one will activate at runtime";
    }

    /// <summary>
    /// Classify a game whose SELECTED proxy type file is ABSENT. If another of
    /// OUR proxy types is already deployed in the folder, that's
    /// <see cref="ProxyDeployStatus.DeployedOtherType"/> (redeploying the
    /// selected type would create a redundant second proxy) — otherwise the
    /// folder is genuinely clean (<see cref="ProxyDeployStatus.NotDeployed"/>).
    /// <paramref name="deployedProxyNames"/> lists OUR proxy DLLs present in the
    /// folder; since the selected type's file is absent, it never appears here,
    /// so a non-empty list always means an OTHER type. The returned message
    /// names the single other-type proxy; when 2+ coexist the per-folder
    /// <see cref="BuildConflictMessage"/> already lists them all, so this
    /// returns no message to avoid duplicating the list. Pure (no IO) so the
    /// rule is unit-testable.
    /// </summary>
    internal static (ProxyDeployStatus status, string? message) ClassifyAbsentSelected(
        IReadOnlyList<string> deployedProxyNames)
    {
        if (deployedProxyNames.Count == 0)
            return (ProxyDeployStatus.NotDeployed, null);

        string? message = deployedProxyNames.Count == 1
            ? $"Deployed as {deployedProxyNames[0]}"
            : null;
        return (ProxyDeployStatus.DeployedOtherType, message);
    }

    /// <summary>
    /// Try to detect UE version from the game executable's PE version info.
    /// Returns null if detection fails.
    /// </summary>
    private static string? TryDetectUeVersion(string exePath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(exePath);
            // Some UE games embed "Unreal Engine" or version in FileDescription/Comments
            // For now, just return null — version is detected by the DLL at runtime
            return null;
        }
        catch
        {
            return null;
        }
    }

    // ────────────────────────────────────────────────────────────────
    // Generic (non-Steam) Drive Scan
    // ────────────────────────────────────────────────────────────────

    /// <summary>Directory names hard-skipped during the generic drive walk —
    /// system/junk trees plus "steamapps" (Steam is scanned by the dedicated
    /// path; the resolved Steam roots are also excluded explicitly). Matched by
    /// directory name, case-insensitive.</summary>
    private static readonly HashSet<string> HardSkipDirs = new(StringComparer.OrdinalIgnoreCase)
    {
        "$Recycle.Bin", "System Volume Information", "Windows", "WinSxS",
        "$SysReset", "Recovery", "node_modules", ".git", "WindowsApps", "steamapps",
    };

    // UE game roots are shallow (<Drive>\...\<Game>\<Project>\Binaries\Win64); 6
    // levels covers manual launcher nesting while hard-stopping runaway descent
    // into asset trees. Prune-on-match usually fires long before this.
    private const int MaxWalkDepth = 6;

    public Task<IReadOnlyList<DriveDescriptor>> GetScannableDrivesAsync(CancellationToken ct = default)
    {
        return Task.Run(() => _platform.GetLogicalDrives(), ct);
    }

    public Task<IReadOnlyList<GameProcessInfo>> ListGameProcessesAsync(CancellationToken ct = default)
    {
        return Task.Run(() => _platform.GetRunningProcesses(), ct);
    }

    public Task<InjectResult> InjectDllAsync(int pid, string dllPath, CancellationToken ct = default)
    {
        return Task.Run(() => _platform.InjectDll(pid, dllPath), ct);
    }

    public bool IsElevated() => _platform.IsElevated();

    public Task<InjectResult> InjectDllElevatedAsync(int pid, string dllPath, CancellationToken ct = default)
    {
        return Task.Run(() => _platform.InjectDllElevated(pid, dllPath), ct);
    }

    public Task<IReadOnlyList<DetectedGame>> FindUeGamesOnDrivesAsync(
        IReadOnlyList<DriveDescriptor> selectedDrives,
        IProgress<DriveScanProgress>? progress = null,
        CancellationToken ct = default)
    {
        return Task.Run(async () =>
        {
            // Requirement 5: resolve Steam library roots ONCE for exclusion.
            var steamRoots = new List<string>();
            try
            {
                var libs = await GetSteamLibraryFoldersAsync(ct);
                foreach (string lib in libs)
                {
                    string n = NormalizeDir(lib);
                    if (n.Length > 0)
                        steamRoots.Add(n);
                }
            }
            catch (OperationCanceledException) { throw; }
            catch (Exception ex)
            {
                _log.Warn("ProxyDeploy", $"Steam-root resolve for exclusion failed: {ex.Message}");
            }

            // Requirement 2: partitions of one physical disk scan SEQUENTIALLY,
            // different disks scan in PARALLEL. Bound overall parallelism so a
            // many-disk box doesn't thrash a shared bus.
            var groups = GroupDrivesByPhysicalDisk(selectedDrives);
            using var gate = new SemaphoreSlim(Math.Clamp(Environment.ProcessorCount, 1, 4));

            var tasks = new List<Task<List<DetectedGame>>>(groups.Count);
            foreach (var group in groups)
            {
                tasks.Add(Task.Run(async () =>
                {
                    // Each group owns its list + dedupe set — no shared mutable
                    // state across parallel groups.
                    var local = new List<DetectedGame>();
                    var localSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    await gate.WaitAsync(ct);
                    try
                    {
                        foreach (var drive in group)
                        {
                            ct.ThrowIfCancellationRequested();
                            string label = $"{drive.Letter}:";
                            progress?.Report(new DriveScanProgress(local.Count, label, "scanning"));
                            WalkDrive(drive.Root, 0, local, localSeen, steamRoots, progress, label, ct);
                        }
                    }
                    finally
                    {
                        gate.Release();
                    }
                    return local;
                }, ct));
            }

            var results = await Task.WhenAll(tasks);

            // Merge + global dedupe by BinariesDir (collapse a game reached via
            // two drives / a junction).
            var merged = new List<DetectedGame>();
            var globalSeen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var list in results)
                foreach (var g in list)
                    if (globalSeen.Add(g.BinariesDir))
                        merged.Add(g);

            _log.Info("ProxyDeploy",
                $"Generic scan found {merged.Count} UE game(s) across {selectedDrives.Count} drive(s)");
            return (IReadOnlyList<DetectedGame>)merged;
        }, ct);
    }

    /// <summary>
    /// Bounded, prune-on-match recursive walk. When a directory looks like a UE
    /// game root it is handed to the EXISTING ScanGameFolder (all layout logic
    /// stays there) and NOT descended into (its Content\Paks is the multi-GB
    /// payload). Inaccessible folders are skipped and the walk continues
    /// (requirement 3). Writes only to the caller's local list/set — safe to run
    /// on a worker thread.
    /// </summary>
    private void WalkDrive(
        string dir, int depth,
        List<DetectedGame> games, HashSet<string> seenBinDirs,
        IReadOnlyList<string> steamRoots,
        IProgress<DriveScanProgress>? progress, string driveLabel,
        CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (depth > MaxWalkDepth)
            return;

        // Skip reparse points (junctions/symlinks) to avoid cycles + drive re-entry.
        try
        {
            if ((new DirectoryInfo(dir).Attributes & FileAttributes.ReparsePoint) != 0)
                return;
        }
        catch
        {
            return;
        }

        if (IsExcludedBySteam(dir, steamRoots))
            return;

        // Prune-on-match: reuse the Steam-path per-game-dir detector, then stop.
        if (LooksLikeUeGameRoot(dir))
        {
            int before = games.Count;
            ScanGameFolder(dir, games, seenBinDirs);
            if (games.Count > before)
                progress?.Report(new DriveScanProgress(games.Count, driveLabel, Path.GetFileName(dir)));
            return;
        }

        // Materialize the child list INSIDE the try: EnumerateDirectories is lazy,
        // so an UnauthorizedAccessException for opening `dir` (e.g. System Volume
        // Information) surfaces during iteration, not at the call. IgnoreInaccessible
        // additionally skips individual locked siblings without aborting the batch
        // (requirement 3).
        List<string> children;
        try
        {
            var opts = new EnumerationOptions { IgnoreInaccessible = true, RecurseSubdirectories = false };
            children = new List<string>(Directory.EnumerateDirectories(dir, "*", opts));
        }
        catch
        {
            return; // access denied / gone — skip subtree (requirement 3)
        }

        foreach (string child in children)
        {
            ct.ThrowIfCancellationRequested();
            if (HardSkipDirs.Contains(Path.GetFileName(child)))
                continue;
            WalkDrive(child, depth + 1, games, seenBinDirs, steamRoots, progress, driveLabel, ct);
        }
    }

    // ---- Pure helpers (unit-testable, no shared state / IO side effects) ----

    /// <summary>Canonicalize a directory path for prefix comparison: full path,
    /// no trailing separator. Returns empty on failure (caller treats as
    /// non-match / skip).</summary>
    internal static string NormalizeDir(string path)
    {
        try { path = Path.GetFullPath(path); }
        catch { return string.Empty; }
        return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
    }

    /// <summary>Requirement 5: is <paramref name="path"/> at-or-under a resolved
    /// Steam library root? (The "steamapps" name is additionally hard-skipped
    /// during the walk as a fallback when root resolution fails.)</summary>
    internal static bool IsExcludedBySteam(string path, IReadOnlyList<string> steamRoots)
    {
        string norm = NormalizeDir(path);
        if (norm.Length == 0)
            return false;

        foreach (string root in steamRoots)
        {
            if (root.Length == 0)
                continue;
            if (string.Equals(norm, root, StringComparison.OrdinalIgnoreCase))
                return true;
            // Trailing separator guard so 'D:\SteamLib' does not match 'D:\SteamLibBackup'.
            string prefix = root + Path.DirectorySeparatorChar;
            if (norm.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    /// <summary>Whether a directory name is hard-skipped by the generic walk.</summary>
    internal static bool IsHardSkipDir(string dirName) => HardSkipDirs.Contains(dirName);

    /// <summary>Requirement 4: cheap, stat-only structural test for a UE shipping
    /// game root. Tiers, most→least reliable: (1) a sibling Engine\Binaries\Win64
    /// next to a project dir with Binaries\Win64; (2) a project Content\Paks with
    /// *.pak/*.utoc/*.ucas; (3) a project Binaries\Win64 with a *-Win64-Shipping
    /// exe; (4) a flattened top-level shipping exe. Never enumerates the (huge)
    /// Content tree.</summary>
    internal static bool LooksLikeUeGameRoot(string dir)
    {
        try
        {
            // Tier 1 — canonical cooked tree.
            if (Directory.Exists(Path.Combine(dir, "Engine", "Binaries", "Win64")))
            {
                foreach (string sub in Directory.EnumerateDirectories(dir))
                {
                    if (string.Equals(Path.GetFileName(sub), "Engine", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (Directory.Exists(Path.Combine(sub, "Binaries", "Win64")))
                        return true;
                }
            }
            // Tier 2 — <Project>\Content\Paks\*.pak|*.utoc|*.ucas
            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                if (string.Equals(Path.GetFileName(sub), "Engine", StringComparison.OrdinalIgnoreCase))
                    continue;
                string paks = Path.Combine(sub, "Content", "Paks");
                if (Directory.Exists(paks) && HasAnyFile(paks, "*.pak", "*.utoc", "*.ucas"))
                    return true;
            }
            // Tier 3 — <Project>\Binaries\Win64\*-Win64-Shipping.exe
            foreach (string sub in Directory.EnumerateDirectories(dir))
            {
                string bin = Path.Combine(sub, "Binaries", "Win64");
                if (Directory.Exists(bin) && HasAnyFile(bin, "*-Win64-Shipping.exe"))
                    return true;
            }
            // Tier 4 — flattened top-level shipping exe (some repacks).
            if (HasAnyFile(dir, "*-Win64-Shipping.exe"))
                return true;
        }
        catch
        {
            // Inaccessible — skip (requirement 3).
        }
        return false;
    }

    private static bool HasAnyFile(string dir, params string[] patterns)
    {
        foreach (string pat in patterns)
        {
            try
            {
                foreach (string _ in Directory.EnumerateFiles(dir, pat))
                    return true;
            }
            catch
            {
                // Inaccessible pattern enumeration — try the next.
            }
        }
        return false;
    }

    /// <summary>Requirement 2: partition drives into scan groups by physical disk.
    /// Drives sharing a physical disk number are grouped (scanned sequentially);
    /// a drive whose disk is unknown (null) gets its OWN singleton group (scanned
    /// in parallel, never serialized against an unrelated drive). Pure.</summary>
    internal static IReadOnlyList<IReadOnlyList<DriveDescriptor>> GroupDrivesByPhysicalDisk(
        IReadOnlyList<DriveDescriptor> drives)
    {
        var byDisk = new Dictionary<int, List<DriveDescriptor>>();
        var groups = new List<IReadOnlyList<DriveDescriptor>>();

        foreach (var d in drives)
        {
            if (d.PhysicalDiskNumber is int disk)
            {
                if (!byDisk.TryGetValue(disk, out var bucket))
                {
                    bucket = new List<DriveDescriptor>();
                    byDisk[disk] = bucket;
                    groups.Add(bucket); // preserve first-seen order, one entry per disk
                }
                bucket.Add(d);
            }
            else
            {
                groups.Add(new List<DriveDescriptor> { d }); // unknown → own group
            }
        }

        return groups;
    }

    // ────────────────────────────────────────────────────────────────
    // Deploy Status
    // ────────────────────────────────────────────────────────────────

    // THREADING CONTRACT for everything below that touches a DetectedGame.
    //
    // DetectedGame is an ObservableObject and its Status / InstalledVersion /
    // ErrorMessage / SuggestedProxy are bound to the Proxy Deploy DataGrid. Writing them
    // from inside a Task.Run raises PropertyChanged on a thread-pool thread, which lets
    // Avalonia mutate the visual tree while the render thread is composing it — that is an
    // access violation inside libSkiaSharp, not an exception, so it takes the whole app
    // down with no managed stack. It did: "Scan Steam" then "Update all" over 29 games
    // reliably crashed with 0xc0000005 in libSkiaSharp.
    //
    // So: do the file I/O on the worker, COLLECT the results, and APPLY them after the
    // await — which resumes on the caller's context. Every call site is a UI-thread
    // [RelayCommand] in ProxyDeployViewModel with no ConfigureAwait(false), so that
    // context is the UI thread. Keep it that way; do not add ConfigureAwait(false) to
    // these awaits, and do not reintroduce writes inside the Task.Run bodies.
    //
    // (This service deliberately does NOT reference Avalonia.Threading — in this codebase
    // Dispatcher.UIThread appears only in ViewModels and Views.)

    /// <summary>One game's post-operation state, computed off-thread and applied on the
    /// caller's thread. The two Set* flags exist because some paths deliberately leave a
    /// field untouched, and <c>null</c> is itself a meaningful value for the other two.</summary>
    private readonly record struct GameStatusUpdate(
        DetectedGame Game,
        ProxyDeployStatus Status,
        string? InstalledVersion = null,
        string? ErrorMessage = null,
        bool SetInstalledVersion = true,
        bool SetErrorMessage = true,
        // The load-observation column. Default OFF so the deploy/error paths, which write a
        // status the instant BEFORE a game is launched, do not clobber a real observation with
        // stale nothing — only the refresh path (which re-reads the log folder) sets it.
        string? LoadObservation = null,
        bool SetLoadObservation = false);

    private static void ApplyStatus(in GameStatusUpdate u)
    {
        u.Game.Status = u.Status;
        if (u.SetInstalledVersion) u.Game.InstalledVersion = u.InstalledVersion;
        if (u.SetErrorMessage)     u.Game.ErrorMessage     = u.ErrorMessage;
        if (u.SetLoadObservation)  u.Game.LoadObservation  = u.LoadObservation;
    }

    /// <summary>
    /// Should this game's freshly-computed disk state be applied, or does the caller own its
    /// current Status/ErrorMessage? Pure so the rule is unit-testable without file IO.
    ///
    /// <para>Exists because a refresh run as the TAIL OF AN OPERATION would otherwise erase
    /// that operation's own result. The disk state of a failed deploy is "file absent", which
    /// <see cref="ClassifyAbsentSelected"/> honestly reports as <c>(NotDeployed, null)</c> —
    /// overwriting both the failure status and the reason, and leaving the user a row that
    /// says NotDeployed with a blank Error next to a banner reading "1 failed". The reason
    /// then existed only in the log.</para>
    /// </summary>
    internal static bool ShouldApplyRefresh(string binariesDir, IReadOnlySet<string>? preserve)
        => preserve == null || !preserve.Contains(binariesDir);

    /// <summary>
    /// Read the "did it actually load?" signal for a game from its per-process log folder under
    /// <c>%LOCALAPPDATA%\UE5CEDumper\Logs</c>. The DLL creates that folder on load
    /// (<c>dll/src/Sein.cpp InitProcessMirror</c>), so its presence + newest write time is the
    /// cheapest honest tell that the proxy/inject actually ran — disk state alone cannot say that
    /// (<c>[PROXYLOAD-2026-08-17]</c>). Absence is reported as "not observed" (UNKNOWN), never as a
    /// failure: the game may simply not have been launched with the proxy yet. Best-effort; any IO
    /// error degrades to "not observed". The join key + classification are the pure, tested
    /// <c>ProxyImportAnalyzer.ProcessLogFolderName</c> / <c>ClassifyLoad</c>.
    /// </summary>
    private string ComputeLoadObservation(string exePath)
    {
        DateTime now = DateTime.Now;
        try
        {
            string folderName = ProxyImportAnalyzer.ProcessLogFolderName(exePath);
            if (string.IsNullOrEmpty(folderName))
                return ProxyImportAnalyzer.ClassifyLoad(false, null, now, Constants.LogMaxAgeDays).Display;

            string procDir = Path.Combine(
                _platform.GetAppDataPath(), Constants.LogFolderName, Constants.LogSubFolder, folderName);

            bool present = Directory.Exists(procDir);
            DateTime? lastWrite = present ? NewestLogWrite(procDir) : null;
            return ProxyImportAnalyzer.ClassifyLoad(present, lastWrite, now, Constants.LogMaxAgeDays).Display;
        }
        catch (Exception ex)
        {
            _log.Debug("ProxyDeploy", $"Load-signal probe skipped for {exePath}: {ex.Message}");
            return ProxyImportAnalyzer.ClassifyLoad(false, null, now, Constants.LogMaxAgeDays).Display;
        }
    }

    /// <summary>Newest <c>*.log</c> write time in a per-process log folder — the DLL's "last wrote a
    /// line" is its "last ran", which is a truer signal than the directory's own mtime (that also
    /// moves on the retention sweep's deletes). Falls back to the folder's write time when it holds
    /// no <c>.log</c> yet; null when nothing is readable.</summary>
    private static DateTime? NewestLogWrite(string dir)
    {
        DateTime? newest = null;
        try
        {
            foreach (string f in Directory.EnumerateFiles(dir, "*.log"))
            {
                DateTime t = File.GetLastWriteTime(f);
                if (newest is null || t > newest) newest = t;
            }
        }
        catch { /* fall through to the folder's own time */ }
        if (newest is null)
        {
            try { newest = Directory.GetLastWriteTime(dir); } catch { /* leave null */ }
        }
        return newest;
    }

    public async Task RefreshDeployStatusAsync(
        IList<DetectedGame> games, string sourceDllPath, ProxyType proxyType,
        IReadOnlySet<string>? preserveBinariesDirs = null,
        CancellationToken ct = default)
    {
        // Snapshot so the worker never enumerates a collection the UI thread can mutate.
        var targets = games.ToList();

        var updates = await Task.Run(() =>
        {
            var results = new List<GameStatusUpdate>(targets.Count);
            string? sourceVersion = GetDllVersion(sourceDllPath);

            string selectedDllName = proxyType.GetDllName();
            string[] allProxyNames = AllProxyDllNames();

            foreach (var game in targets)
            {
                ct.ThrowIfCancellationRequested();

                string targetDll = Path.Combine(game.BinariesDir, selectedDllName);

                ProxyDeployStatus status;
                string? installedVersion = null;
                string? errorMessage = null;

                // Which of OUR proxy DLLs are actually present in this folder?
                // Computed up front because it drives BOTH the absent-selected
                // classification (is the folder truly clean, or is another of our
                // proxy types deployed?) and the 2+ redundancy warning below. This
                // is a property of the folder, INDEPENDENT of the selected radio.
                var deployedProxyNames = allProxyNames
                    .Where(name =>
                    {
                        string p = Path.Combine(game.BinariesDir, name);
                        return File.Exists(p) && IsOurProxyDll(p);
                    })
                    .ToList();

                // Status reflects the SELECTED proxy type's state ────────────
                if (!File.Exists(targetDll))
                {
                    // Absent selected type: clean folder → NotDeployed; another of
                    // our types present → DeployedOtherType (don't mislead the user
                    // into redeploying on top of a working proxy of a different type).
                    var (absentStatus, message) = ClassifyAbsentSelected(deployedProxyNames);
                    status = absentStatus;
                    errorMessage = message;
                }
                else if (!IsOurProxyDll(targetDll))
                {
                    status = ProxyDeployStatus.OtherProxy;
                    try
                    {
                        var info = FileVersionInfo.GetVersionInfo(targetDll);
                        errorMessage = $"Other proxy: {info.ProductName ?? info.FileDescription ?? "unknown"}";
                    }
                    catch
                    {
                        errorMessage = "Other proxy DLL detected";
                    }
                }
                else
                {
                    installedVersion = GetDllVersion(targetDll);
                    status = (sourceVersion != null && installedVersion == sourceVersion)
                             ? ProxyDeployStatus.DeployedCurrent
                             : ProxyDeployStatus.DeployedOutdated;
                }

                // Redundancy detection: warn ONLY when 2+ of OUR proxies coexist
                // (only one activates at runtime — see Heiter.cpp's mutex). A
                // single deployed proxy of any type is the normal state and must
                // not warn (otherwise switching tabs falsely flags every game
                // that has a different single proxy installed). N-proxy-safe: no
                // hardcoded type pair. deployedProxyNames was computed up front.
                string? conflictMsg = BuildConflictMessage(deployedProxyNames);
                if (conflictMsg != null)
                {
                    errorMessage = string.IsNullOrEmpty(errorMessage)
                                   ? conflictMsg
                                   : $"{errorMessage}; {conflictMsg}";
                }

                // "Did it actually load?" — orthogonal to the disk status above, so it is set on
                // EVERY refresh regardless of that status ([PROXYLOAD-2026-08-17]). Cheap: a
                // Directory.Exists + a mtime read, no PE parse.
                string loadObservation = ComputeLoadObservation(game.ExePath);

                results.Add(new GameStatusUpdate(game, status, installedVersion, errorMessage,
                    LoadObservation: loadObservation, SetLoadObservation: true));
            }

            return results;
        }, ct);

        // Back on the caller's thread — see the threading contract above. Games the caller
        // reserved keep the status/message its own operation just wrote.
        foreach (var u in updates)
            if (ShouldApplyRefresh(u.Game.BinariesDir, preserveBinariesDirs))
                ApplyStatus(u);
    }

    // ────────────────────────────────────────────────────────────────
    // Deploy / Undeploy
    // ────────────────────────────────────────────────────────────────

    public static DeployVerdict PlanDeploy(bool targetExists, bool targetIsOurs,
                                           bool sameVersion, DeployOptions options)
    {
        if (!targetExists) return DeployVerdict.Proceed;

        if (!targetIsOurs)
        {
            return options.ForeignConsent
                ? DeployVerdict.Proceed
                : DeployVerdict.NeedsForeignConsent;
        }

        if (sameVersion && !options.ForceSameVersion) return DeployVerdict.AlreadyCurrent;

        return DeployVerdict.Proceed;
    }

    /// <summary>Suffix of the staging file <see cref="CopyProxyStaged"/> writes beside the
    /// target. Deliberately NOT one of <see cref="AllProxyDllNames"/> and not a loadable
    /// extension, so nothing — Windows, the deploy grid, undeploy, or the orphan scanner —
    /// can mistake a half-written copy for a deployed proxy.</summary>
    internal const string StageSuffix = ".ue5dump-stage";

    /// <summary>
    /// May the staged copy be published over the live target? (pure — audit #5 AC11)
    ///
    /// Two INDEPENDENT detectors, because they fail on different things: the byte count
    /// catches a short write / truncation, and the ownership flag catches a copy whose
    /// PE version resource did not survive. `sourceBytes > 0` makes an unmeasurable or
    /// empty source a REFUSAL rather than a pass (pass -1 for "could not measure"), the
    /// same rule Flamme's ShouldPublishAtomicWrite settled on DLL-side.
    ///
    /// Ownership is compared to the SOURCE rather than asserted true: a dev build whose
    /// proxy carries no ProductName must still deploy, it just has to copy faithfully.
    /// </summary>
    internal static bool ShouldPublishStagedProxy(long sourceBytes, long stagedBytes,
                                                  bool sourceIsOurs, bool stagedIsOurs)
        => sourceBytes > 0 && stagedBytes == sourceBytes && sourceIsOurs == stagedIsOurs;

    private static long TryFileLength(string path)
    {
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? fi.Length : -1;
        }
        catch
        {
            return -1;   // unmeasurable → ShouldPublishStagedProxy refuses
        }
    }

    /// <summary>
    /// Publish <paramref name="sourcePath"/> at <paramref name="targetPath"/> without ever
    /// exposing a partial file (audit #5 AC11).
    ///
    /// The old code was a bare <c>File.Copy(overwrite: true)</c> straight onto the live
    /// target, which TRUNCATES first and then streams. Any failure part-way — disk full,
    /// source read error, the process being killed — left a truncated DLL sitting at the
    /// real proxy path, and that state is worse than a failed deploy in three ways:
    ///   • the game IMPORTS that name, so it now fails to start;
    ///   • a truncated PE has no version resource, so <see cref="IsOurProxyDll"/> is false
    ///     and the grid reports it as "Other proxy: unknown" — another program's DLL;
    ///   • and on that verdict BOTH removal paths refuse it (PlanUndeploy skips it as
    ///     foreign, redeploy demands ForeignConsent), so the user's own wreckage is
    ///     unremovable from the panel that produced it.
    ///
    /// Staging inside the SAME directory keeps the publish a same-volume rename. Residue
    /// is bounded: the staging file is deleted on every exit path, and a kill between the
    /// copy and the rename leaves one file that the next deploy of this type overwrites.
    /// IOExceptions are left to propagate so DeployAsync's SHARING_VIOLATION filter still
    /// classifies a locked target as "File locked (game running?)" — the rename raises the
    /// same violation the direct copy used to.
    /// </summary>
    internal static void CopyProxyStaged(string sourcePath, string targetPath)
    {
        string stagePath = targetPath + StageSuffix;
        try
        {
            File.Copy(sourcePath, stagePath, overwrite: true);

            long srcBytes = TryFileLength(sourcePath);
            long stgBytes = TryFileLength(stagePath);
            if (!ShouldPublishStagedProxy(srcBytes, stgBytes,
                                          DllProductIsOurs(sourcePath), DllProductIsOurs(stagePath)))
            {
                throw new IOException(
                    $"Staged proxy failed verification (source {srcBytes} bytes, staged {stgBytes} bytes) " +
                    $"— {Path.GetFileName(targetPath)} was left untouched");
            }

            // overwrite:true also succeeds when the target does not exist yet, so the
            // first-ever deploy takes this identical path.
            File.Move(stagePath, targetPath, overwrite: true);
        }
        finally
        {
            // Never leave the staging file in a game's Binaries folder: nothing in this
            // file's removal paths knows the name.
            try { if (File.Exists(stagePath)) File.Delete(stagePath); }
            catch { /* best-effort; the next deploy overwrites it */ }
        }
    }

    public async Task<bool> DeployAsync(string sourceDllPath, DetectedGame game, ProxyType proxyType,
        DeployOptions options = default, CancellationToken ct = default)
    {
        var (ok, update) = await Task.Run<(bool, GameStatusUpdate)>(() =>
        {
            try
            {
                string targetDll = Path.Combine(game.BinariesDir, proxyType.GetDllName());

                bool exists   = File.Exists(targetDll);
                bool isOurs   = exists && IsOurProxyDll(targetDll);
                string? srcVer = null, tgtVer = null;
                if (exists && isOurs)
                {
                    srcVer = GetDllVersion(sourceDllPath);
                    tgtVer = GetDllVersion(targetDll);
                }
                bool sameVersion = srcVer != null && srcVer == tgtVer;

                switch (PlanDeploy(exists, isOurs, sameVersion, options))
                {
                    case DeployVerdict.NeedsForeignConsent:
                        return (false, new GameStatusUpdate(game, ProxyDeployStatus.OtherProxy,
                            ErrorMessage: "Refused: another program's proxy DLL",
                            SetInstalledVersion: false));

                    case DeployVerdict.AlreadyCurrent:
                        // Already up to date — status only, version/error left as they were.
                        return (true, new GameStatusUpdate(game, ProxyDeployStatus.DeployedCurrent,
                            SetInstalledVersion: false, SetErrorMessage: false));
                }

                // Replacing a third party's DLL is irreversible and the same
                // operation erases the row that said whose it was (a successful
                // deploy clears ErrorMessage), so record the identity FIRST —
                // otherwise nothing anywhere says what used to be here.
                if (exists && !isOurs)
                {
                    _log.Warn("ProxyDeploy",
                        $"Replacing another program's {proxyType.GetDllName()} in {game.Name} " +
                        $"({DescribeForeignDll(targetDll)}) — foreign overwrite was explicitly allowed");
                }

                // Whether a proxy will load is an ADVISORY here, never a refusal — a refusal used
                // to live here and rejected version.dll (the broadest-compatible flavour) on every
                // game that loads it dynamically. Two independent risks, at most one of which
                // applies (they partition on whether the flavour is imported):
                //   • BYPASS: the flavour IS imported, so an already-mapped System32 copy can
                //     pre-empt ours — the [PROXYLOAD-2026-08-17] OCTOPATH failure; and
                //   • NEVER-LOADS: a static-only flavour (dxgi/winmm) is absent, so nothing names
                //     it and it cannot load at all.
                // Both fail SILENTLY and TOTALLY when real (zero log, reads like "nothing
                // happened"), so the note rides along with the successful deploy rather than
                // becoming a wall. A null parse result means "could not tell" → no note. Computed
                // HERE, past the early returns, so the paths that deploy nothing do not pay a PE
                // parse. The persistent per-game tell is the Load column, refreshed from the log
                // folder — this note is the one-shot nudge at deploy time.
                var imports = ReadProxyImports(game.ExePath);
                string? riskNote = ProxyImportAnalyzer.DescribeDeployAdvisory(imports, proxyType);

                CopyProxyStaged(sourceDllPath, targetDll);
                _log.Info("ProxyDeploy", $"Deployed {proxyType.GetDisplayName()} to {game.Name}: {targetDll}");
                if (riskNote != null)
                    _log.Warn("ProxyDeploy", $"{proxyType.GetDllName()} for {game.Name}: {riskNote}");
                return (true, new GameStatusUpdate(game, ProxyDeployStatus.DeployedCurrent,
                    InstalledVersion: GetDllVersion(targetDll), ErrorMessage: riskNote));
            }
            catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020) /* SHARING_VIOLATION */
                                      || ex.Message.Contains("being used", StringComparison.OrdinalIgnoreCase))
            {
                _log.Warn("ProxyDeploy", $"Deploy to {game.Name} failed: file locked");
                return (false, new GameStatusUpdate(game, ProxyDeployStatus.ErrorLocked,
                    ErrorMessage: "File locked (game running?)", SetInstalledVersion: false));
            }
            catch (Exception ex)
            {
                _log.Error("ProxyDeploy", $"Deploy to {game.Name} failed: {ex.Message}");
                return (false, new GameStatusUpdate(game, ProxyDeployStatus.ErrorOther,
                    ErrorMessage: ex.Message, SetInstalledVersion: false));
            }
        }, ct);

        ApplyStatus(update);   // caller's thread — see the threading contract above
        return ok;
    }

    /// <summary>All distinct proxy DLL file names we ship. <c>Distinct</c> guards
    /// against a future enum value whose switch arm falls back to the default.</summary>
    public static string[] AllProxyDllNames() =>
        Enum.GetValues<ProxyType>()
            .Select(t => t.GetDllName())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    /// <summary>What an undeploy sweep decided to do, per file.</summary>
    /// <param name="ToDelete">Ours — safe to remove.</param>
    /// <param name="ForeignSkipped">Present but NOT ours (a mod loader, another
    /// tool, or the genuine Windows DLL). Never touched.</param>
    public readonly record struct UndeployPlan(
        IReadOnlyList<string> ToDelete,
        IReadOnlyList<string> ForeignSkipped);

    /// <summary>
    /// Decide which proxy DLLs an undeploy should remove. Pure so the policy can be
    /// tested without fabricating PE files with version resources
    /// (<see cref="IsOurProxyDll"/> reads <c>FileVersionInfo.ProductName</c>).
    /// </summary>
    public static UndeployPlan PlanUndeploy(
        IEnumerable<(string Name, bool Exists, bool IsOurs)> candidates)
    {
        var toDelete = new List<string>();
        var foreign = new List<string>();
        foreach (var (name, exists, isOurs) in candidates)
        {
            if (!exists) continue;
            if (isOurs) toDelete.Add(name);
            else foreign.Add(name);
        }
        return new UndeployPlan(toDelete, foreign);
    }

    /// <summary>
    /// Turn the outcome of an undeploy sweep into a status / message / success
    /// triple. Pure, and the single place the precedence rules live:
    /// a locked file outranks everything (it is the actionable failure); refusing
    /// to touch a foreign DLL is only a FAILURE when we removed nothing of ours,
    /// otherwise it is a note on an otherwise successful clean-up.
    /// </summary>
    public static (ProxyDeployStatus Status, string? Message, bool Success) ResolveUndeployOutcome(
        int removed, IReadOnlyList<string> foreignSkipped, IReadOnlyList<string> locked)
    {
        if (locked.Count > 0)
            return (ProxyDeployStatus.ErrorLocked,
                    $"File locked (game running?): {string.Join(", ", locked)}", false);

        if (removed == 0 && foreignSkipped.Count > 0)
            return (ProxyDeployStatus.OtherProxy,
                    $"Refused: not our proxy DLL ({string.Join(", ", foreignSkipped)})", false);

        if (foreignSkipped.Count > 0)
            return (ProxyDeployStatus.NotDeployed,
                    $"Left another program's {string.Join(", ", foreignSkipped)}", true);

        // removed >= 0 with nothing foreign: a clean folder is just as much a
        // success as one we emptied.
        return (ProxyDeployStatus.NotDeployed, null, true);
    }

    public async Task<bool> UndeployAsync(DetectedGame game, CancellationToken ct = default)
    {
        var (ok, update) = await Task.Run<(bool, GameStatusUpdate)>(() =>
        {
            try
            {
                // Sweep EVERY proxy flavour, not just the one selected in the UI.
                // The radio button picks what to DEPLOY; undeploy is a clean-up, and
                // a user who deployed dxgi.dll and later switched the radio to
                // version.dll would otherwise be unable to remove it at all — while
                // the grid happily reported DeployedOtherType.
                var plan = PlanUndeploy(AllProxyDllNames().Select(name =>
                {
                    string p = Path.Combine(game.BinariesDir, name);
                    bool exists = File.Exists(p);
                    return (name, exists, exists && IsOurProxyDll(p));
                }));

                var locked = new List<string>();
                int removed = 0;
                foreach (var name in plan.ToDelete)
                {
                    // Per-file try/catch: one locked DLL must not abandon the rest.
                    // Removing what we can is what the user asked for; the locked
                    // one is reported by name.
                    try
                    {
                        File.Delete(Path.Combine(game.BinariesDir, name));
                        removed++;
                        _log.Info("ProxyDeploy", $"Undeployed {name} from {game.Name}");
                    }
                    catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020) /* SHARING_VIOLATION */
                                              || ex.Message.Contains("being used", StringComparison.OrdinalIgnoreCase))
                    {
                        locked.Add(name);
                        _log.Warn("ProxyDeploy", $"Undeploy {name} from {game.Name} failed: file locked");
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        locked.Add(name);
                        _log.Warn("ProxyDeploy", $"Undeploy {name} from {game.Name} denied: {ex.Message}");
                    }
                }

                var (status, message, success) =
                    ResolveUndeployOutcome(removed, plan.ForeignSkipped, locked);
                // InstalledVersion is only cleared when something was actually removed.
                return (success, new GameStatusUpdate(game, status,
                    ErrorMessage: message, SetInstalledVersion: removed > 0));
            }
            catch (Exception ex)
            {
                _log.Error("ProxyDeploy", $"Undeploy from {game.Name} failed: {ex.Message}");
                return (false, new GameStatusUpdate(game, ProxyDeployStatus.ErrorOther,
                    ErrorMessage: ex.Message, SetInstalledVersion: false));
            }
        }, ct);

        ApplyStatus(update);   // caller's thread — see the threading contract above
        return ok;
    }

    // ────────────────────────────────────────────────────────────────
    // Leftover ("orphan") proxy cleanup — the impure shell
    //
    // ALL policy lives in ProxyOrphanScanner (pure, no System.IO). This region holds the only
    // filesystem calls the feature makes, and there are deliberately few of them.
    //
    // THE ONE RULE A REVIEWER MUST ENFORCE HERE: no bare `catch { }` around a directory
    // enumeration. The rest of this file swallows enumeration failures and continues, which is
    // right for a SCAN and catastrophic for a DELETE predicate — combined with the laziness of
    // Directory.Enumerate* it produces a partial listing indistinguishable from a clean folder.
    // RealProbe returns null on any failure and PlanPrune refuses on null.
    // ────────────────────────────────────────────────────────────────

    /// <summary>
    /// The ONLY place this feature reads a directory. Bare <c>EnumerateFileSystemEntries</c>
    /// overload on purpose: the <c>EnumerationOptions</c> overload defaults to
    /// <c>AttributesToSkip = Hidden | System</c>, so a folder holding a hidden save file would
    /// enumerate as holding only our DLL. Materialised INSIDE the try because the enumerator is
    /// lazy and would otherwise throw mid-<c>foreach</c> and leave a half-built list.
    /// </summary>
    private static DirSnapshot? RealProbe(string path)
    {
        try
        {
            var di = new DirectoryInfo(path);
            if (!di.Exists) return null;
            bool reparse = (di.Attributes & FileAttributes.ReparsePoint) != 0;

            var files = new List<string>();
            var dirs = new List<string>();
            foreach (string entry in Directory.EnumerateFileSystemEntries(path))
            {
                if (Directory.Exists(entry)) dirs.Add(Path.GetFileName(entry));
                else files.Add(Path.GetFileName(entry));
            }
            return new DirSnapshot(path, files, dirs, reparse);
        }
        catch
        {
            // Missing, denied, or vanished mid-listing. NEVER "empty".
            return null;
        }
    }

    /// <summary>
    /// Is this file one of ours? Two independent signals, OR-combined.
    ///
    /// <para>The resource signal (<c>ProductName</c> plus a corroborating identity field) covers
    /// every proxy ever shipped. The export quorum exists for a different job: it is the only
    /// signal evaluable through an ALREADY-OPEN handle, which is what lets the removal path
    /// re-verify identity across the confirm→delete gap. Keeping both also means a build-system
    /// change that dropped <c>version.rc</c> from the proxy targets would degrade recall instead of
    /// silently recognising nothing.</para>
    ///
    /// <para>Returns <see cref="FileOwnership.Unreadable"/> rather than Foreign when the file
    /// cannot be inspected at all, so the caller can say why instead of quietly skipping.</para>
    /// </summary>
    private FileOwnership ClassifyFileOwnership(string fullPath)
    {
        bool sawAnything = false;
        try
        {
            var info = FileVersionInfo.GetVersionInfo(fullPath);
            sawAnything = true;
            if (string.Equals(info.ProductName, Constants.ProxyProductName, StringComparison.OrdinalIgnoreCase)
                && (string.Equals(info.InternalName, "UE5Dumper", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(info.OriginalFilename, "UE5Dumper.dll", StringComparison.OrdinalIgnoreCase)))
            {
                return FileOwnership.Ours;
            }
        }
        catch
        {
            // No version resource / not a PE / vanished — fall through to the export probe.
        }

        try
        {
            using var fs = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                                          FileShare.Read | FileShare.Delete);
            sawAnything = true;
            if (ProxyImportAnalyzer.HasExportQuorum(fs)) return FileOwnership.Ours;
        }
        catch
        {
            return FileOwnership.Unreadable;
        }

        return sawAnything ? FileOwnership.Foreign : FileOwnership.Unreadable;
    }

    /// <summary>
    /// Is this Steam game folder genuinely gone? TWO independent signals, either one vetoes:
    /// a Steam <c>appmanifest</c> still naming the install directory, or any executable surviving
    /// under the game root.
    ///
    /// <para>The manifest check must be PER LIBRARY, not global — measured on a real machine, the
    /// same <c>installdir</c> name existed in two libraries at once, one installed and one an
    /// orphaned shell. A global name match would have refused the shell forever.</para>
    ///
    /// <para>A manifest that exists but cannot be parsed counts as PRESENT. "Cannot tell" must fail
    /// closed, and no attempt is made to interpret <c>StateFlags</c>: mid-update bit patterns are
    /// guesswork, so any manifest at all is a refusal.</para>
    /// </summary>
    private static LivenessResult ProbeSteamLiveness(string commonRoot, string gameFolderName, string gameRoot)
    {
        try
        {
            string? steamapps = Path.GetDirectoryName(commonRoot);
            if (steamapps != null && Directory.Exists(steamapps))
            {
                foreach (string acf in Directory.EnumerateFiles(steamapps, "appmanifest_*.acf"))
                {
                    string? installDir = TryReadAcfInstallDir(acf, out bool unreadable);
                    if (unreadable)
                    {
                        return new LivenessResult(OrphanVerdict.SteamManifestPresent,
                            $"A Steam manifest ({Path.GetFileName(acf)}) could not be read, so the game may still be installed.",
                            "");
                    }
                    if (installDir != null &&
                        string.Equals(installDir, gameFolderName, StringComparison.OrdinalIgnoreCase))
                    {
                        return new LivenessResult(OrphanVerdict.SteamManifestPresent,
                            $"Steam still lists this game as installed ({Path.GetFileName(acf)}).", "");
                    }
                }
            }
        }
        catch
        {
            return new LivenessResult(OrphanVerdict.SteamManifestPresent,
                "The Steam manifest folder could not be read, so installation state is unknown.", "");
        }

        // Second signal: any surviving executable means content is still there.
        try
        {
            foreach (string exe in Directory.EnumerateFiles(gameRoot, "*.exe", SearchOption.AllDirectories))
            {
                return new LivenessResult(OrphanVerdict.LiveContentPresent,
                    $"An executable is still present ({Path.GetFileName(exe)}), so the game is not gone.", "");
            }
        }
        catch
        {
            return new LivenessResult(OrphanVerdict.LiveContentPresent,
                "The game folder could not be fully read, so it is not treated as empty.", "");
        }

        return new LivenessResult(OrphanVerdict.Deletable, "",
            "no executable survives anywhere under the game folder");
    }

    /// <summary>Read <c>"installdir"</c> from a Steam appmanifest. Sets
    /// <paramref name="unreadable"/> when the file exists but could not be read at all.</summary>
    private static string? TryReadAcfInstallDir(string acfPath, out bool unreadable)
    {
        unreadable = false;
        try
        {
            foreach (string line in ReadLinesShared(acfPath))
            {
                int k = line.IndexOf("\"installdir\"", StringComparison.OrdinalIgnoreCase);
                if (k < 0) continue;

                // The key IS here but the value could not be parsed — a truncated write, an unusual
                // quoting, a format change. That must count as UNREADABLE, not as "this manifest
                // names nothing": returning null here would let a manifest that really does claim
                // this folder fall through and the game get treated as uninstalled.
                int q1 = line.IndexOf('"', k + 12);
                int q2 = q1 < 0 ? -1 : line.IndexOf('"', q1 + 1);
                if (q1 < 0 || q2 < 0)
                {
                    unreadable = true;
                    return null;
                }
                return line[(q1 + 1)..q2];
            }
            return null;
        }
        catch
        {
            unreadable = true;
            return null;
        }
    }

    public async Task<IReadOnlyList<OrphanProxy>> FindOrphanProxiesAsync(
        OrphanScanSources sources,
        IReadOnlySet<string> liveBinariesDirs,
        IProgress<OrphanScanProgress>? progress,
        CancellationToken ct = default)
    {
        // Rows are CONSTRUCTED on the worker, which is safe because nothing is bound yet — the same
        // thing FindUeGamesAsync does with DetectedGame. What must not happen on a worker is
        // MUTATING an already-bound row; see the threading contract on IProxyDeployService.
        return await Task.Run(() =>
        {
            var libs = GetSteamLibraryFoldersCore();
            var commonRoots = libs
                .Select(l => Path.Combine(l, "steamapps", "common"))
                .Where(Directory.Exists)
                .ToList();

            var candidates = new List<(string Dir, OrphanScanSources Src)>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            void AddCandidate(string dir, OrphanScanSources src)
            {
                if (!ProxyOrphanScanner.TryNormalizeDir(dir, out string norm)) return;
                int i = candidates.FindIndex(c => string.Equals(c.Dir, norm, StringComparison.OrdinalIgnoreCase));
                if (i >= 0) { candidates[i] = (candidates[i].Dir, candidates[i].Src | src); return; }
                if (seen.Add(norm)) candidates.Add((norm, src));
            }

            // Source 1 — the authoritative one: a bounded shape scan of the Steam libraries. It is
            // the only source that sees a leftover nobody ever logged.
            if (sources.HasFlag(OrphanScanSources.SteamShapeScan))
            {
                foreach (string common in commonRoots)
                {
                    ct.ThrowIfCancellationRequested();
                    foreach (string binDir in EnumerateBinariesWin64Under(common, ct))
                        AddCandidate(binDir, OrphanScanSources.SteamShapeScan);
                }
            }

            // Sources 2 and 3 — log-derived. Their unique value is NON-Steam locations, which
            // source 1 structurally cannot see. Both are best-effort.
            if (sources.HasFlag(OrphanScanSources.DeployLog))
                foreach (string d in CandidatesFromLogs("view-*.log", isDllLog: false))
                    AddCandidate(d, OrphanScanSources.DeployLog);

            if (sources.HasFlag(OrphanScanSources.DllLoadLog))
                foreach (string d in CandidatesFromLogs("init-*.log", isDllLog: true))
                    AddCandidate(d, OrphanScanSources.DllLoadLog);

            var rows = new List<OrphanProxy>();
            int examined = 0;
            foreach (var (dir, src) in candidates)
            {
                ct.ThrowIfCancellationRequested();
                examined++;
                progress?.Report(new OrphanScanProgress(dir, examined, rows.Count));

                PrunePlan plan = ProxyOrphanScanner.PlanPrune(
                    dir, commonRoots, liveBinariesDirs,
                    RealProbe, ClassifyFileOwnership, ProbeSteamLiveness,
                    _platform.VolumeHasRecycleBin);

                // Only surface rows that either can be acted on, or that hold something of ours and
                // are blocked for a reason worth telling the user about. A folder with nothing of
                // ours in it is not this feature's business and must not appear.
                //
                // NotOnFixedDrive is the second kind, and it was being DROPPED — the filter kept
                // only the two actionable verdicts, so the carefully-worded no-Recycle-Bin refusal
                // was computed and thrown away. Measured end to end on 2026-08-12 (a real leftover
                // version.dll on a fixed volume with NukeOnDelete=1): the scan reported "No leftover
                // proxy DLLs found (23 folder(s) examined)" while the file sat there, and flipping
                // that one registry value — nothing else — made the same folder surface as a row.
                // So the user was told nothing was there precisely on the kind of volume where a
                // hand-delete is unrecoverable. Every OTHER refusal means ClassifyLeaf found none of
                // our files, which is the case this filter's comment is about.
                if (!OrphanVerdictRules.ShouldSurface(plan.Verdict)) continue;

                rows.Add(BuildOrphanRow(dir, src, plan));
            }

            _log.Info("ProxyDeploy",
                $"Orphan scan: {candidates.Count} candidate folder(s) examined, {rows.Count} leftover(s) found");
            return (IReadOnlyList<OrphanProxy>)rows;
        }, ct);
    }

    private OrphanProxy BuildOrphanRow(string dir, OrphanScanSources src, PrunePlan plan)
    {
        long size = 0;
        string version = "";
        foreach (string f in plan.FilesToRecycle)
        {
            try
            {
                var fi = new FileInfo(f);
                if (fi.Exists) size += fi.Length;
                if (version.Length == 0) version = GetDllVersion(f) ?? "";
            }
            catch { /* display-only detail; a missing size must not fail the row */ }
        }

        // A refused row NAMES the files (so the user can deal with them by hand) but authorises
        // NOTHING. AuthorisedFiles is the intersection set the removal path narrows its fresh plan
        // against; leaving it populated on a non-actionable verdict would make the refusal one
        // relaxed gate away from being a delete list. The verdict check in RemoveOrphanProxyAsync
        // already stops it — this makes it stop twice, in the direction that matters.
        bool actionable = OrphanVerdictRules.IsActionable(plan.Verdict);

        return new OrphanProxy
        {
            DllPath = plan.FilesToRecycle.Count > 0 ? plan.FilesToRecycle[0] : dir,
            DllDirectory = dir,
            DllNames = string.Join(", ", plan.FilesToRecycle.Select(Path.GetFileName)),
            AuthorisedFiles = actionable ? plan.FilesToRecycle : Array.Empty<string>(),
            SizeBytes = size,
            FileVersion = version,
            ChainDirs = plan.DirsToRemove,
            TopmostRemovableDir = plan.DirsToRemove.Count > 0 ? plan.DirsToRemove[^1] : "",
            Source = src,
            Verdict = plan.Verdict,
            Blockers = plan.Blockers,
            EvidenceText = plan.EvidenceText,
        };
    }

    /// <summary>
    /// Bounded search for <c>...\Binaries\Win64</c> under a Steam <c>common</c> root. Bounded, not a
    /// full walk: only the game folder and up to two levels below it, which covers every layout
    /// measured (<c>&lt;Game&gt;\Binaries\Win64</c>, <c>&lt;Game&gt;\&lt;Proj&gt;\Binaries\Win64</c>
    /// and the extra-wrapper <c>&lt;Game&gt;\Package\&lt;Proj&gt;\Binaries\Win64</c>).
    /// </summary>
    private static IEnumerable<string> EnumerateBinariesWin64Under(string common, CancellationToken ct)
    {
        List<string> gameDirs;
        try { gameDirs = Directory.EnumerateDirectories(common).ToList(); }
        catch { yield break; }

        foreach (string game in gameDirs)
        {
            ct.ThrowIfCancellationRequested();
            foreach (string bin in ProbeLevels(game, 2))
                yield return bin;
        }
    }

    private static IEnumerable<string> ProbeLevels(string root, int depth)
    {
        string direct = Path.Combine(root, "Binaries", "Win64");
        bool directExists;
        try { directExists = Directory.Exists(direct); } catch { directExists = false; }
        if (directExists) yield return direct;

        if (depth <= 0) yield break;

        List<string> subs;
        try { subs = Directory.EnumerateDirectories(root).ToList(); } catch { yield break; }
        foreach (string sub in subs)
            foreach (string bin in ProbeLevels(sub, depth - 1))
                yield return bin;
    }

    /// <summary>
    /// Read a text file line by line WITHOUT requiring exclusive-ish access, for files something
    /// else legitimately holds open for writing.
    ///
    /// <para><b>Why this is not <c>File.ReadLines</c>.</b> That helper opens with
    /// <c>FileShare.Read</c>, which declares "other handles may only read" — so it fails with a
    /// sharing violation against a writer that already has the file open, which is exactly what our
    /// own logger is doing to <c>view-0.log</c> while the app runs. Measured 2026-08-12: deploying a
    /// proxy and then pressing "Find leftovers" in the SAME session found nothing, because the only
    /// log line naming that folder was in the live <c>view-0.log</c>, the read threw, and the
    /// caller's per-file <c>catch</c> swallowed it — so the whole file contributed zero candidates
    /// and the folder was never examined. It only became visible after a restart rotated the log to
    /// an archive. <c>FileShare.ReadWrite | FileShare.Delete</c> is what tolerates the live writer;
    /// <c>Delete</c> additionally survives the file being rotated out from under us mid-read.</para>
    ///
    /// <para>Lazy, like the helper it replaces, so an open failure surfaces at the first
    /// <c>MoveNext</c> inside the caller's <c>try</c> rather than at the call site.</para>
    /// </summary>
    internal static IEnumerable<string> ReadLinesShared(string path)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.Read,
                                      FileShare.ReadWrite | FileShare.Delete);
        using var reader = new StreamReader(fs);   // UTF-8 + BOM detection, same as File.ReadLines
        string? line;
        while ((line = reader.ReadLine()) != null)
            yield return line;
    }

    /// <summary>
    /// Candidate directories recovered from our own log tree. Best-effort by design: the log
    /// retention window bounds how far back this can see, and a proxy deployed but never launched
    /// leaves no DLL banner at all — which is exactly why the Steam shape scan is the primary
    /// source and this is a complement.
    /// </summary>
    private IEnumerable<string> CandidatesFromLogs(string filePattern, bool isDllLog)
    {
        List<string> lines = new();
        try
        {
            string logRoot = _platform.GetLogDirectoryPath();
            if (!Directory.Exists(logRoot)) return Array.Empty<string>();

            foreach (string dir in Directory.EnumerateDirectories(logRoot))
            {
                foreach (string file in Directory.EnumerateFiles(dir, filePattern))
                {
                    try { lines.AddRange(ReadLinesShared(file)); }
                    catch { /* one unreadable log must not stop the sweep */ }
                }
            }
        }
        catch
        {
            return Array.Empty<string>();
        }
        return ProxyOrphanScanner.CandidateDirsFrom(lines, isDllLog);
    }

    public async Task<OrphanRemovalResult> RemoveOrphanProxyAsync(
        OrphanProxy row, IReadOnlySet<string> liveBinariesDirs, CancellationToken ct = default)
    {
        return await Task.Run(() =>
        {
            var libs = GetSteamLibraryFoldersCore();
            var commonRoots = libs
                .Select(l => Path.Combine(l, "steamapps", "common"))
                .Where(Directory.Exists)
                .ToList();

            // RE-EVALUATE from scratch. The plan the user confirmed was computed before they read
            // the dialog; anything could have changed since, and the cost of re-running it is one
            // directory listing.
            //
            // liveBinariesDirs is threaded in from the row so the LiveGameFolder veto the SCAN applied
            // is applied again here. Passing an empty set made the delete path strictly weaker than
            // the scan that authorised it, which is the wrong direction for a re-check.
            PrunePlan plan = ProxyOrphanScanner.PlanPrune(
                row.DllDirectory, commonRoots, liveBinariesDirs,
                RealProbe, ClassifyFileOwnership, ProbeSteamLiveness,
                _platform.VolumeHasRecycleBin);

            if (!OrphanVerdictRules.IsActionable(plan.Verdict))
            {
                string why = plan.Blockers.Count > 0 ? plan.Blockers[0] : "no longer eligible";
                _log.Warn("ProxyDeploy", $"Orphan removal refused on re-check ({row.DllDirectory}): {why}");
                return new OrphanRemovalResult(false, $"Skipped — {why}", 0, 0);
            }

            // INTERSECT the fresh plan with what the user actually authorised. Re-planning alone is
            // not enough: a folder that merely stopped being shared between the scan and the click
            // would come back as prunable and we would remove directories the confirmation said would
            // be left in place. The intersection is what makes "the result can be smaller, never
            // larger" true rather than aspirational — and the report and dialog both say those words.
            var authorisedFiles = new HashSet<string>(row.AuthorisedFiles, StringComparer.OrdinalIgnoreCase);
            var authorisedDirs = new HashSet<string>(row.ChainDirs, StringComparer.OrdinalIgnoreCase);
            var filesToRecycle = plan.FilesToRecycle.Where(authorisedFiles.Contains).ToList();
            var dirsToRemove = plan.DirsToRemove.Where(authorisedDirs.Contains).ToList();

            if (filesToRecycle.Count < plan.FilesToRecycle.Count || dirsToRemove.Count < plan.DirsToRemove.Count)
            {
                _log.Info("ProxyDeploy",
                    $"Orphan removal narrowed to what was confirmed for {row.DllDirectory}: " +
                    $"{filesToRecycle.Count}/{plan.FilesToRecycle.Count} file(s), " +
                    $"{dirsToRemove.Count}/{plan.DirsToRemove.Count} folder(s)");
            }
            if (filesToRecycle.Count == 0)
                return new OrphanRemovalResult(false, "Skipped — nothing left that you confirmed", 0, 0);

            var locked = new List<string>();
            var readOnly = new List<string>();
            var failed = new List<string>();
            int recycled = 0;
            int vanished = 0;   // already gone before we got to it — not a failure, not a removal

            foreach (string file in filesToRecycle)   // the CONFIRMED subset, not the fresh plan
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    var fi = new FileInfo(file);
                    if (!fi.Exists)
                    {
                        // Already gone (a parallel cleanup, or the user deleted it by hand while the
                        // dialog was open). Count it so the outcome does not claim "nothing was
                        // removed" and does not invent a cause that never occurred.
                        vanished++;
                        continue;
                    }

                    // Never clear ReadOnly: a user who set it meant it. Report and move on.
                    if ((fi.Attributes & FileAttributes.ReadOnly) != 0)
                    {
                        readOnly.Add(fi.Name);
                        continue;
                    }

                    // Final identity check through a HELD handle. FileShare.Read denies a writer
                    // trying to replace the file (Steam "Verify integrity" restoring a real
                    // version.dll is the realistic race, not an attacker), while FileShare.Delete
                    // still permits the recycle below.
                    using (var fs = new FileStream(file, FileMode.Open, FileAccess.Read,
                                                   FileShare.Read | FileShare.Delete))
                    {
                        if (!ProxyImportAnalyzer.HasExportQuorum(fs))
                        {
                            _log.Warn("ProxyDeploy",
                                $"Orphan removal skipped {file}: failed the pre-delete identity re-check");
                            failed.Add(fi.Name);
                            continue;
                        }
                    }

                    if (_platform.MoveToRecycleBin(file))
                    {
                        recycled++;
                        _log.Info("ProxyDeploy", $"Recycled leftover proxy {file}");
                    }
                    else
                    {
                        failed.Add(fi.Name);
                        _log.Warn("ProxyDeploy", $"Could not recycle {file} (refused or no Recycle Bin)");
                    }
                }
                catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020)
                                          || ex.Message.Contains("being used", StringComparison.OrdinalIgnoreCase))
                {
                    locked.Add(Path.GetFileName(file));
                    _log.Warn("ProxyDeploy", $"Leftover proxy {file} is locked");
                }
                catch (UnauthorizedAccessException ex)
                {
                    failed.Add(Path.GetFileName(file));
                    _log.Warn("ProxyDeploy", $"Access denied removing {file}: {ex.Message}");
                }
                catch (Exception ex)
                {
                    failed.Add(Path.GetFileName(file));
                    _log.Error("ProxyDeploy", $"Unexpected error removing {file}: {ex.Message}");
                }
            }

            // Prune only when EVERY file went. A folder that still holds one of our DLLs must keep
            // its folder, or the next scan cannot find it again.
            int dirsRemoved = 0;
            string? pruneStopReason = null;
            bool allFilesGone = recycled + vanished == filesToRecycle.Count && locked.Count == 0
                                && readOnly.Count == 0 && failed.Count == 0;
            if (allFilesGone)
            {
                foreach (string dir in dirsToRemove)   // deepest-first, and only what was confirmed
                {
                    ct.ThrowIfCancellationRequested();
                    try
                    {
                        // NON-recursive on purpose. This is the kernel-enforced emptiness check:
                        // anything left inside — including something created while the user read
                        // the dialog — makes this throw, and the walk stops right there.
                        Directory.Delete(dir, recursive: false);
                        dirsRemoved++;
                        _log.Info("ProxyDeploy", $"Removed empty folder {dir}");
                    }
                    catch (IOException ex)
                    {
                        // EXPECTED and the whole point: the folder still holds something, so it and
                        // everything above it are kept. Info, not a warning.
                        pruneStopReason = "a folder still had something in it";
                        _log.Info("ProxyDeploy", $"Stopped pruning at {dir} (not empty): {ex.Message}");
                        break;
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        // NOT the same thing, and it must not read as "not empty" to the user: the
                        // folder may well be empty and we simply are not allowed to remove it. Warn,
                        // and say so in the result text so the difference is visible.
                        pruneStopReason = $"permission was denied on {Path.GetFileName(dir)}";
                        _log.Warn("ProxyDeploy", $"Access denied removing folder {dir}: {ex.Message}");
                        break;
                    }
                    catch (Exception ex)
                    {
                        pruneStopReason = $"an unexpected error on {Path.GetFileName(dir)}";
                        _log.Error("ProxyDeploy", $"Unexpected error removing folder {dir}: {ex.Message}");
                        break;
                    }
                }
            }

            var (success, message) = ProxyOrphanScanner.ResolveRemovalOutcome(
                recycled, vanished, dirsRemoved, dirsToRemove.Count, locked, readOnly, failed,
                pruneStopReason);
            return new OrphanRemovalResult(success, message, recycled, dirsRemoved);
        }, ct);
    }

    // ────────────────────────────────────────────────────────────────
    // Proxy Suggestion (import-table + remembered pick)
    // ────────────────────────────────────────────────────────────────

    public async Task ApplyProxySuggestionsAsync(
        IReadOnlyList<DetectedGame> games,
        IReadOnlyDictionary<string, ProxyType> confirmedByExe,
        IReadOnlyDictionary<string, ProxyType> rememberedByGame,
        IReadOnlySet<string> injectedExes,
        bool enabled,
        CancellationToken ct = default)
    {
        var targets = games.ToList();

        var suggestions = await Task.Run(() =>
        {
            var results = new List<(DetectedGame Game, ProxyType? Type, string? Display)>(targets.Count);

            foreach (var game in targets)
            {
                ct.ThrowIfCancellationRequested();

                if (!enabled)
                {
                    results.Add((game, null, null));
                    continue;
                }

                string exeName = Path.GetFileName(game.ExePath);
                ProxyType? confirmed =
                    confirmedByExe.TryGetValue(exeName, out var c) ? c : null;
                ProxyType? remembered =
                    rememberedByGame.TryGetValue(game.Name, out var p) ? p : null;
                bool injected = injectedExes.Contains(exeName);

                var imports = ReadProxyImports(game.ExePath);
                var suggestion = ProxyImportAnalyzer.Recommend(imports, confirmed, remembered, injected);

                results.Add((game, suggestion.Type, suggestion.Display));
            }

            return results;
        }, ct);

        // Back on the caller's thread — see the threading contract above.
        // SuggestedProxy feeds a DataGrid column, so this pass is a visual-tree mutation
        // exactly like the status one, and reading 29 PE import tables is slow enough that
        // it used to land mid-render.
        foreach (var (game, type, display) in suggestions)
        {
            game.SuggestedProxyType = type;
            game.SuggestedProxy = display;
        }
    }

    /// <summary>Open a game .exe and parse only its PE headers/import directory
    /// (no full-file read) to learn which proxy DLLs it imports. Returns null when
    /// the file is missing/locked/malformed — the caller then shows no viability
    /// hint. The parsing itself lives in the pure, testable ProxyImportAnalyzer.</summary>
    private ProxyImportAnalyzer.ProxyImportInfo? ReadProxyImports(string exePath)
    {
        var info = AnalyzeOnePe(exePath);
        if (info is not { ImportsNone: true }) return info;

        // Importing NONE of the three is the signature of a MODULAR UE build: the
        // .exe is a thin bootstrap (Satisfactory's is ~264 KB) and the engine lives
        // in sibling *-Win64-Shipping.dll modules. Reading the stub alone made the
        // Suggested column claim "no dxgi/dinput8" for a game where a dxgi proxy
        // loads perfectly well (D3D12RHI imports it). A proxy activates if ANY
        // module in the process imports that name, so fold the siblings in.
        return info.Value.Merge(ReadModuleImports(Path.GetDirectoryName(exePath)));
    }

    /// <summary>Parse one PE's import directories. Returns null when the file is
    /// missing/locked/malformed — the caller then shows no viability hint.</summary>
    private ProxyImportAnalyzer.ProxyImportInfo? AnalyzeOnePe(string path)
    {
        try
        {
            using var fs = new FileStream(
                path, FileMode.Open, FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete);
            return ProxyImportAnalyzer.Analyze(fs);
        }
        catch (Exception ex)
        {
            _log.Debug("ProxyDeploy", $"Import parse skipped for {path}: {ex.Message}");
            return null;
        }
    }

    /// <summary>Upper bound on modular-build modules parsed per game — a runaway
    /// guard, not a budget. Satisfactory ships 182 next to its stub, and the
    /// all-three short-circuit does NOT fire there (nothing imports dinput8), so
    /// the whole set really is walked; 512 keeps a comfortable margin rather than
    /// silently truncating the answer. Cheap because Analyze reads PE headers
    /// only, and this path is reached solely for a stub exe.</summary>
    private const int MaxModularModulesScanned = 512;

    /// <summary>OR the proxy-import flags of the <c>*-Win64-Shipping.dll</c> modules
    /// sitting next to a modular build's bootstrap .exe. Top-level only (no recursion)
    /// — that is where UE puts them, and it keeps this off the hot path of a library
    /// scan. Short-circuits once all three names are accounted for.</summary>
    private ProxyImportAnalyzer.ProxyImportInfo ReadModuleImports(string? dir)
    {
        var acc = new ProxyImportAnalyzer.ProxyImportInfo(false, false, false);
        if (string.IsNullOrEmpty(dir) || !Directory.Exists(dir)) return acc;
        try
        {
            int seen = 0;
            foreach (string dll in Directory.EnumerateFiles(dir, "*-Win64-Shipping.dll"))
            {
                if (++seen > MaxModularModulesScanned) break;
                if (AnalyzeOnePe(dll) is { } m) acc = acc.Merge(m);
                if (acc is { ImportsVersion: true, ImportsDinput8: true, ImportsDxgi: true })
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.Debug("ProxyDeploy", $"Modular import scan skipped for {dir}: {ex.Message}");
        }
        return acc;
    }

    // ────────────────────────────────────────────────────────────────
    // DLL Identification
    // ────────────────────────────────────────────────────────────────

    /// <summary>Best-effort identity of a DLL that is NOT ours, for the log line
    /// written before it is replaced. The row that displayed this is blanked by
    /// the successful deploy, so the log is the only surviving record.</summary>
    private static string DescribeForeignDll(string dllPath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(dllPath);
            string product = string.IsNullOrWhiteSpace(info.ProductName) ? "unknown product" : info.ProductName!;
            string version = string.IsNullOrWhiteSpace(info.FileVersion) ? "no version" : info.FileVersion!;
            return $"{product} {version}";
        }
        catch (Exception ex)
        {
            return $"unreadable version info: {ex.GetType().Name}";
        }
    }

    public bool IsOurProxyDll(string dllPath) => DllProductIsOurs(dllPath);

    /// <summary>Static twin of <see cref="IsOurProxyDll"/> so the staged-copy helper
    /// (which must stay static to be unit-testable against a temp folder) can apply the
    /// SAME ownership predicate the panel and both removal paths use.</summary>
    private static bool DllProductIsOurs(string dllPath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(dllPath);
            return string.Equals(info.ProductName, Constants.ProxyProductName, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    public string? GetDllVersion(string dllPath)
    {
        try
        {
            var info = FileVersionInfo.GetVersionInfo(dllPath);
            return info.FileVersion;
        }
        catch
        {
            return null;
        }
    }
}
