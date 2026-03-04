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
    /// Check deployment status for each game and update Status/InstalledVersion fields.
    /// </summary>
    Task RefreshDeployStatusAsync(
        IList<DetectedGame> games, string sourceDllPath, CancellationToken ct = default);

    /// <summary>
    /// Deploy version.dll to a game's Binaries directory.
    /// Returns true on success, false on failure (sets game.ErrorMessage).
    /// </summary>
    Task<bool> DeployAsync(string sourceDllPath, DetectedGame game, bool force = false,
        CancellationToken ct = default);

    /// <summary>
    /// Undeploy (delete) version.dll from a game's Binaries directory.
    /// Only removes our DLL (checks ProductName). Returns true on success.
    /// </summary>
    Task<bool> UndeployAsync(DetectedGame game, CancellationToken ct = default);

    /// <summary>
    /// Check if a version.dll at the given path is ours (ProductName == "UE5CEDumper").
    /// </summary>
    bool IsOurProxyDll(string dllPath);

    /// <summary>
    /// Get the file version string from a DLL's PE version info.
    /// Returns null if version info is unavailable.
    /// </summary>
    string? GetDllVersion(string dllPath);
}
