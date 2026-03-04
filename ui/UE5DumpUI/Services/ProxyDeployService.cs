using System.Diagnostics;
using System.IO;
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

    public ProxyDeployService(ILoggingService log)
    {
        _log = log;
    }

    // ────────────────────────────────────────────────────────────────
    // Steam Library Detection
    // ────────────────────────────────────────────────────────────────

    public Task<IReadOnlyList<string>> GetSteamLibraryFoldersAsync(CancellationToken ct = default)
    {
        return Task.Run(() =>
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
                var paths = VdfParser.ParseLibraryFolders(vdfContent);

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
        }, ct);
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

        // Search pattern: look for Binaries/Win64/*.exe at up to 2 levels deep
        // Level 0: <gameDir>/Binaries/Win64/
        // Level 1: <gameDir>/<subDir>/Binaries/Win64/
        var searchRoots = new List<string> { gameDir };

        try
        {
            foreach (string sub in Directory.EnumerateDirectories(gameDir))
            {
                // Skip the UE Engine folder — it only contains CrashReportClient.exe,
                // not game binaries.  Avoids false-positive entries.
                if (string.Equals(Path.GetFileName(sub), "Engine", StringComparison.OrdinalIgnoreCase))
                    continue;
                searchRoots.Add(sub);
            }
        }
        catch
        {
            // Permission error etc. — just use the root
        }

        foreach (string root in searchRoots)
        {
            string binDir = Path.Combine(root, "Binaries", "Win64");
            if (!Directory.Exists(binDir))
                continue;

            // Dedup by BinariesDir
            if (!seenBinDirs.Add(binDir))
                continue;

            try
            {
                // Find executables in Binaries/Win64
                bool foundUe = false;
                foreach (string exePath in Directory.EnumerateFiles(binDir, "*.exe"))
                {
                    string exeName = Path.GetFileName(exePath);

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

                // Fallback: any exe in Binaries/Win64 with no .dll extension
                // is likely a UE game even without standard naming
                if (!foundUe)
                {
                    foreach (string exePath in Directory.EnumerateFiles(binDir, "*.exe"))
                    {
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
    // Deploy Status
    // ────────────────────────────────────────────────────────────────

    public Task RefreshDeployStatusAsync(
        IList<DetectedGame> games, string sourceDllPath, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            string? sourceVersion = GetDllVersion(sourceDllPath);

            foreach (var game in games)
            {
                ct.ThrowIfCancellationRequested();

                string targetDll = Path.Combine(game.BinariesDir, Constants.ProxyDllName);
                game.ErrorMessage = null;

                if (!File.Exists(targetDll))
                {
                    game.Status = ProxyDeployStatus.NotDeployed;
                    game.InstalledVersion = null;
                    continue;
                }

                if (!IsOurProxyDll(targetDll))
                {
                    game.Status = ProxyDeployStatus.OtherProxy;
                    game.InstalledVersion = null;

                    // Try to identify what it is
                    try
                    {
                        var info = FileVersionInfo.GetVersionInfo(targetDll);
                        game.ErrorMessage = $"Other proxy: {info.ProductName ?? info.FileDescription ?? "unknown"}";
                    }
                    catch
                    {
                        game.ErrorMessage = "Other proxy DLL detected";
                    }
                    continue;
                }

                // It's our DLL — check version
                string? installedVersion = GetDllVersion(targetDll);
                game.InstalledVersion = installedVersion;

                if (sourceVersion != null && installedVersion == sourceVersion)
                    game.Status = ProxyDeployStatus.DeployedCurrent;
                else
                    game.Status = ProxyDeployStatus.DeployedOutdated;
            }
        }, ct);
    }

    // ────────────────────────────────────────────────────────────────
    // Deploy / Undeploy
    // ────────────────────────────────────────────────────────────────

    public Task<bool> DeployAsync(string sourceDllPath, DetectedGame game, bool force = false,
        CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                string targetDll = Path.Combine(game.BinariesDir, Constants.ProxyDllName);

                // Refuse to overwrite another program's proxy DLL
                if (File.Exists(targetDll) && !IsOurProxyDll(targetDll) && !force)
                {
                    game.Status = ProxyDeployStatus.OtherProxy;
                    game.ErrorMessage = "Refused: another program's proxy DLL";
                    return false;
                }

                // Skip if same version (unless force)
                if (!force && File.Exists(targetDll) && IsOurProxyDll(targetDll))
                {
                    string? srcVer = GetDllVersion(sourceDllPath);
                    string? tgtVer = GetDllVersion(targetDll);
                    if (srcVer != null && srcVer == tgtVer)
                    {
                        game.Status = ProxyDeployStatus.DeployedCurrent;
                        return true; // Already up to date
                    }
                }

                File.Copy(sourceDllPath, targetDll, overwrite: true);
                game.Status = ProxyDeployStatus.DeployedCurrent;
                game.InstalledVersion = GetDllVersion(targetDll);
                game.ErrorMessage = null;
                _log.Info("ProxyDeploy", $"Deployed to {game.Name}: {targetDll}");
                return true;
            }
            catch (IOException ex) when (ex.HResult == unchecked((int)0x80070020) /* SHARING_VIOLATION */
                                      || ex.Message.Contains("being used", StringComparison.OrdinalIgnoreCase))
            {
                game.Status = ProxyDeployStatus.ErrorLocked;
                game.ErrorMessage = "File locked (game running?)";
                _log.Warn("ProxyDeploy", $"Deploy to {game.Name} failed: file locked");
                return false;
            }
            catch (Exception ex)
            {
                game.Status = ProxyDeployStatus.ErrorOther;
                game.ErrorMessage = ex.Message;
                _log.Error("ProxyDeploy", $"Deploy to {game.Name} failed: {ex.Message}");
                return false;
            }
        }, ct);
    }

    public Task<bool> UndeployAsync(DetectedGame game, CancellationToken ct = default)
    {
        return Task.Run(() =>
        {
            try
            {
                string targetDll = Path.Combine(game.BinariesDir, Constants.ProxyDllName);

                if (!File.Exists(targetDll))
                {
                    game.Status = ProxyDeployStatus.NotDeployed;
                    game.InstalledVersion = null;
                    return true;
                }

                // Refuse to delete another program's proxy DLL
                if (!IsOurProxyDll(targetDll))
                {
                    game.Status = ProxyDeployStatus.OtherProxy;
                    game.ErrorMessage = "Refused: not our proxy DLL";
                    return false;
                }

                File.Delete(targetDll);
                game.Status = ProxyDeployStatus.NotDeployed;
                game.InstalledVersion = null;
                game.ErrorMessage = null;
                _log.Info("ProxyDeploy", $"Undeployed from {game.Name}: {targetDll}");
                return true;
            }
            catch (IOException ex) when (ex.Message.Contains("being used", StringComparison.OrdinalIgnoreCase))
            {
                game.Status = ProxyDeployStatus.ErrorLocked;
                game.ErrorMessage = "File locked (game running?)";
                _log.Warn("ProxyDeploy", $"Undeploy from {game.Name} failed: file locked");
                return false;
            }
            catch (Exception ex)
            {
                game.Status = ProxyDeployStatus.ErrorOther;
                game.ErrorMessage = ex.Message;
                _log.Error("ProxyDeploy", $"Undeploy from {game.Name} failed: {ex.Message}");
                return false;
            }
        }, ct);
    }

    // ────────────────────────────────────────────────────────────────
    // DLL Identification
    // ────────────────────────────────────────────────────────────────

    public bool IsOurProxyDll(string dllPath)
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
