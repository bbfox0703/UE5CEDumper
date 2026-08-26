using UE5DumpUI.Models;

namespace UE5DumpUI.Core;

/// <summary>
/// Service for detecting Steam games and managing proxy DLL deployment.
/// All OS-dependent calls (registry, file system, PE version info) are
/// encapsulated here for testability and platform abstraction.
/// </summary>
public interface IProxyDeployService
{
    /// <summary>
    /// Get all Steam library folder paths by parsing libraryfolders.vdf.
    /// Returns empty list if Steam is not installed or VDF parse fails.
    /// </summary>
    Task<IReadOnlyList<string>> GetSteamLibraryFoldersAsync(CancellationToken ct = default);

    /// <summary>
    /// Scan Steam library folders for UE game executables.
    /// Detects standard (xxx-Win64-Shipping.exe) and custom UE game layouts.
    /// </summary>
    Task<IReadOnlyList<DetectedGame>> FindUeGamesAsync(
        IReadOnlyList<string> libraryPaths, CancellationToken ct = default);

    /// <summary>
    /// Enumerate the ready fixed/removable drives (with physical-disk numbers)
    /// available for the generic (non-Steam) scan. Delegates to the platform
    /// layer so the caller stays platform-agnostic.
    /// </summary>
    Task<IReadOnlyList<DriveDescriptor>> GetScannableDrivesAsync(CancellationToken ct = default);

    /// <summary>
    /// Generic (non-Steam) scan: walk the selected drives for UE games by folder
    /// structure. Drives on the same physical disk are walked sequentially while
    /// different disks run in parallel; Steam library folders and system/junk
    /// trees are excluded; inaccessible folders are skipped and the walk
    /// continues. Reuses the same per-game-dir detection as the Steam path.
    /// </summary>
    Task<IReadOnlyList<DetectedGame>> FindUeGamesOnDrivesAsync(
        IReadOnlyList<DriveDescriptor> selectedDrives,
        IProgress<DriveScanProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Enumerate running processes as DLL-injection candidates (UE games flagged).
    /// Delegates to the platform layer.
    /// </summary>
    Task<IReadOnlyList<GameProcessInfo>> ListGameProcessesAsync(CancellationToken ct = default);

    /// <summary>
    /// Inject a DLL into a running process (CreateRemoteThread + LoadLibraryW).
    /// Used by the "Inject into running game" flow. Delegates to the platform layer.
    /// </summary>
    Task<InjectResult> InjectDllAsync(int pid, string dllPath, CancellationToken ct = default);

    /// <summary>True when the app is running elevated (so an Access-Denied inject
    /// can't be helped by relaunching as Administrator — it's already admin).</summary>
    bool IsElevated();

    /// <summary>Retry injection elevated (UAC-prompt relaunch does just the inject).
    /// Used when <see cref="InjectDllAsync"/> returns AccessDenied.</summary>
    Task<InjectResult> InjectDllElevatedAsync(int pid, string dllPath, CancellationToken ct = default);

    /// <summary>
    /// Check deployment status for each game (for the given proxy type) and
    /// update Status / InstalledVersion / StatusDetail. Independently of the
    /// selected type, flags a redundancy warning via StatusDetail only when
    /// 2+ of our proxy DLLs actually coexist in the same folder (only one
    /// activates at runtime).
    ///
    /// <para><paramref name="preserveBinariesDirs"/> names games whose current
    /// Status/StatusDetail the CALLER owns and this refresh must not touch. Pass the
    /// games an operation just failed on when the refresh is that operation's own tail:
    /// the disk state of a failed deploy is "file absent", which this method honestly
    /// reports as <c>NotDeployed</c> with no message — erasing the failure and its reason.
    /// Leave it null for a standalone Refresh, where recomputing everything is correct.</para>
    /// </summary>
    /// <remarks>
    /// THREADING: every method on this interface that writes a <see cref="DetectedGame"/>
    /// must do so on the CALLER's thread, not on a worker. Those properties are bound to
    /// the Proxy Deploy DataGrid, and raising PropertyChanged off the UI thread lets
    /// Avalonia mutate the visual tree under the render thread — an access violation
    /// inside libSkiaSharp that kills the process with no managed stack. Do the I/O on a
    /// worker, then apply the results after the await. Callers must not use
    /// ConfigureAwait(false).
    /// </remarks>
    Task RefreshDeployStatusAsync(
        IList<DetectedGame> games, string sourceDllPath, ProxyType proxyType,
        IReadOnlySet<string>? preserveBinariesDirs = null,
        CancellationToken ct = default);

    /// <summary>
    /// Deploy the proxy DLL of the given type to a game's Binaries directory.
    /// Returns true on success, false on failure (sets game.StatusDetail).
    /// </summary>
    Task<bool> DeployAsync(string sourceDllPath, DetectedGame game, ProxyType proxyType,
        DeployOptions options = default, CancellationToken ct = default);

    /// <summary>
    /// Undeploy (delete) our proxy DLLs from a game's Binaries directory.
    ///
    /// <para><b>Type-agnostic on purpose.</b> It sweeps EVERY proxy flavour we ship,
    /// not the one currently selected in the UI: the radio button chooses what to
    /// <i>deploy</i>, whereas undeploy is a clean-up. Scoping it to the selection
    /// left a user who deployed <c>dxgi.dll</c> and later switched the radio to
    /// <c>version.dll</c> unable to remove it at all — while the grid reported
    /// <see cref="ProxyDeployStatus.DeployedOtherType"/> at them.</para>
    ///
    /// <para>Only ever deletes files that are OURS (<c>ProductName</c> check via
    /// <c>IsOurProxyDll</c>); a foreign <c>version.dll</c>/<c>dxgi.dll</c> — a mod
    /// loader, another tool — is always left alone.</para>
    ///
    /// Returns true when nothing of ours is left behind (including the case where
    /// there was nothing to remove).
    /// </summary>
    Task<bool> UndeployAsync(DetectedGame game, CancellationToken ct = default);

    /// <summary>
    /// Find LEFTOVER proxy DLLs of ours — folders still holding one of our proxies after the game
    /// that owned them was uninstalled. Steam leaves the folder behind precisely because it contains
    /// a file Steam does not own, so the shell survives with our DLL alone inside it.
    ///
    /// <para>Purely a REPORT. It deletes nothing, and it must not be folded into the Scan Steam or
    /// Refresh flows — the caller invokes it from its own button.</para>
    ///
    /// <para><paramref name="liveBinariesDirs"/> is the set of <c>DetectedGame.BinariesDir</c> values
    /// the panel already knows to be installed; any candidate matching one is refused outright.</para>
    /// </summary>
    /// <remarks>
    /// THREADING: the same contract as <see cref="RefreshDeployStatusAsync"/> above applies, with one
    /// clarification. CONSTRUCTING <see cref="OrphanProxy"/> rows on a worker is safe — nothing is
    /// data-bound until the caller adds them to a collection. MUTATING an already-bound row from a
    /// worker is not, and is the shape that produced the libSkiaSharp access violation. So: build the
    /// list on the worker, return it, add it on the UI thread; and apply every per-row result from
    /// <see cref="RemoveOrphanProxyAsync"/> after the await. Callers must not use ConfigureAwait(false).
    /// </remarks>
    Task<IReadOnlyList<OrphanProxy>> FindOrphanProxiesAsync(
        OrphanScanSources sources,
        IReadOnlySet<string> liveBinariesDirs,
        IProgress<OrphanScanProgress>? progress = null,
        CancellationToken ct = default);

    /// <summary>
    /// Remove one leftover row: move its file(s) to the RECYCLE BIN (never unlink), then remove the
    /// now-empty folder chain one level at a time with the NON-recursive
    /// <c>Directory.Delete</c> — which is a kernel-enforced emptiness check and closes the
    /// confirm→delete race, because anything created in the meantime simply makes that step throw
    /// and the walk stops there.
    ///
    /// <para>The full eligibility decision is RE-EVALUATED from disk first, then INTERSECTED with what
    /// the row actually authorised (<see cref="OrphanProxy.AuthorisedFiles"/> /
    /// <see cref="OrphanProxy.ChainDirs"/>). Both halves matter: re-evaluating stops a stale plan
    /// being acted on, and intersecting stops a newly-eligible folder being removed when the user was
    /// told it would be left alone. Together they make "the result can be smaller than what you were
    /// shown, never larger" a property of the code rather than a hope.</para>
    ///
    /// <para><paramref name="liveBinariesDirs"/> must be the SAME set given to
    /// <see cref="FindOrphanProxiesAsync"/>, so the installed-game veto is re-applied here too;
    /// passing an empty set would make the deleting path weaker than the scan that authorised it.</para>
    ///
    /// <para>Returns an immutable result so the caller can apply it to bound properties on its own
    /// thread.</para>
    /// </summary>
    Task<OrphanRemovalResult> RemoveOrphanProxyAsync(
        OrphanProxy row, IReadOnlySet<string> liveBinariesDirs, CancellationToken ct = default);

    /// <summary>
    /// Compute a per-game proxy suggestion for each detected game and write it to
    /// <c>SuggestedProxyType</c> / <c>SuggestedProxy</c>. Preference order:
    /// (0) a proxy CONFIRMED to have loaded this game (<paramref name="confirmedByExe"/>,
    /// keyed by .exe file name — the DLL self-reported a proxy load that stayed
    /// stable); (1) a proxy the user last deployed for this game
    /// (<paramref name="rememberedByGame"/>, keyed by <c>DetectedGame.Name</c>);
    /// (2) <c>injection · no proxy deployed</c> when the user has successfully
    /// injected into this game (<paramref name="injectedExes"/>, matched by .exe
    /// file name) and never deployed a proxy; (3) the safe <c>version.dll</c>
    /// default, with the .exe import table only annotating which proxies are
    /// importable. When <paramref name="enabled"/> is false the suggestion fields
    /// are cleared. Advisory only — never changes the selected proxy type, never deploys.
    /// </summary>
    Task ApplyProxySuggestionsAsync(
        IReadOnlyList<DetectedGame> games,
        IReadOnlyDictionary<string, ProxyType> confirmedByExe,
        IReadOnlyDictionary<string, ProxyType> rememberedByGame,
        IReadOnlySet<string> injectedExes,
        bool enabled,
        CancellationToken ct = default);

    /// <summary>
    /// Check if a DLL at the given path is ours (ProductName == "UE5CEDumper").
    /// </summary>
    bool IsOurProxyDll(string dllPath);

    /// <summary>
    /// Get the file version string from a DLL's PE version info.
    /// Returns null if version info is unavailable.
    /// </summary>
    string? GetDllVersion(string dllPath);
}
