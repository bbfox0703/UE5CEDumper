using CommunityToolkit.Mvvm.ComponentModel;

namespace UE5DumpUI.Models;

/// <summary>
/// Status of proxy DLL deployment for a detected UE game.
/// </summary>
public enum ProxyDeployStatus
{
    /// <summary>No version.dll in the game's Binaries/Win64 directory.</summary>
    NotDeployed,

    /// <summary>Our proxy DLL deployed, same version as source.</summary>
    DeployedCurrent,

    /// <summary>Our proxy DLL deployed, but a different (older) version.</summary>
    DeployedOutdated,

    /// <summary>A version.dll exists but is NOT ours — another program's proxy. Blocked.</summary>
    OtherProxy,

    /// <summary>
    /// The SELECTED proxy type is not deployed, but another of OUR proxy types
    /// (e.g. dxgi.dll while the version.dll radio is selected) IS deployed in the
    /// folder. Distinct from <see cref="NotDeployed"/> (a genuinely clean folder)
    /// so the user does not redeploy a redundant second proxy on top of a working one.
    /// </summary>
    DeployedOtherType,

    /// <summary>File operation failed because the game is running (file locked).</summary>
    ErrorLocked,

    /// <summary>Unexpected error during deploy/undeploy.</summary>
    ErrorOther
}

/// <summary>
/// A detected UE game in a Steam library folder.
/// Extends ObservableObject so property changes (IsSelected, Status, etc.)
/// are reflected in the DataGrid UI immediately.
/// </summary>
public sealed partial class DetectedGame : ObservableObject
{
    /// <summary>Display name (typically the Steam folder name).</summary>
    public string Name { get; init; } = "";

    /// <summary>Full path to the game executable.</summary>
    public string ExePath { get; init; } = "";

    /// <summary>Directory containing the game executable (deploy target for version.dll).</summary>
    public string BinariesDir { get; init; } = "";

    /// <summary>Detected UE version string, or null if unknown.</summary>
    public string? UeVersion { get; init; }

    /// <summary>Current proxy DLL deployment status.</summary>
    [ObservableProperty] private ProxyDeployStatus _status;

    /// <summary>Installed proxy DLL version string (if deployed).</summary>
    [ObservableProperty] private string? _installedVersion;

    /// <summary>Error message from last operation (if any).</summary>
    [ObservableProperty] private string? _errorMessage;

    /// <summary>Whether this game is selected for batch operations.</summary>
    [ObservableProperty] private bool _isSelected;

    /// <summary>
    /// Suggested proxy type for this game (import-table + remembered-pick based),
    /// or null when suggestions are disabled / not yet computed. Advisory only —
    /// it never changes the global proxy radio and never auto-deploys.
    /// </summary>
    [ObservableProperty] private ProxyType? _suggestedProxyType;

    /// <summary>Concise column text for the suggestion (e.g. "version · default",
    /// "dxgi · last used", "version · default · alt: dxgi"). Null clears the cell.</summary>
    [ObservableProperty] private string? _suggestedProxy;

    /// <summary>
    /// Concise column text for the "did it actually load?" signal — "loaded 2026-08-17",
    /// "loaded 2026-07-01 (stale)", or "not observed" — computed from the per-process log
    /// folder the DLL creates on load (see <c>ProxyImportAnalyzer.ClassifyLoad</c>).
    ///
    /// <para><b>Orthogonal to <see cref="Status"/>, which is DISK state only.</b>
    /// <c>DeployedCurrent</c> + "not observed" is precisely the <c>[PROXYLOAD-2026-08-17]</c>
    /// silent failure: the file is in place yet the proxy never ran. "not observed" is honest
    /// UNKNOWN (the game may not have been launched), never a claim of failure. Null clears the
    /// cell.</para>
    /// </summary>
    [ObservableProperty] private string? _loadObservation;
}
