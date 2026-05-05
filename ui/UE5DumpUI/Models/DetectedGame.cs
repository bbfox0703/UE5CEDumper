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
}
