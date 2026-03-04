using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using UE5DumpUI.Core;
using UE5DumpUI.Models;

namespace UE5DumpUI.ViewModels;

/// <summary>
/// ViewModel for the Proxy DLL Deploy tab.
/// Manages Steam game detection and proxy DLL deployment.
/// Not pipe-dependent — works independently of game connection.
/// </summary>
public partial class ProxyDeployViewModel : ViewModelBase
{
    private readonly IProxyDeployService _deploy;
    private readonly ILoggingService _log;

    [ObservableProperty] private bool _isScanning;
    [ObservableProperty] private string _statusText = "";
    [ObservableProperty] private string _sourceDllPath = "";
    [ObservableProperty] private string? _sourceDllVersion;
    [ObservableProperty] private bool _forceOverwrite;
    [ObservableProperty] private string? _lastOperationResult;

    public ObservableCollection<DetectedGame> Games { get; } = new();

    /// <summary>Whether any games are selected for batch operations.</summary>
    public bool HasSelection => Games.Any(g => g.IsSelected);

    public ProxyDeployViewModel(IProxyDeployService deploy, ILoggingService log)
    {
        _deploy = deploy;
        _log = log;

        try
        {
            // Locate source DLL next to the UI executable.
            // Environment.ProcessPath returns the real exe path even for single-file publish
            // (AppContext.BaseDirectory returns the temp extraction dir for single-file apps).
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            var dllPath = Path.Combine(exeDir, Constants.ProxyDllName);
            SourceDllPath = dllPath;
            SourceDllVersion = File.Exists(dllPath)
                ? _deploy.GetDllVersion(dllPath)
                : null;

            StatusText = File.Exists(dllPath)
                ? $"Source: {Constants.ProxyDllName} v{SourceDllVersion ?? "?"}"
                : $"Source DLL not found: {dllPath}";
        }
        catch (Exception ex)
        {
            StatusText = $"Init error: {ex.Message}";
            _log.Error("ProxyDeploy", $"ViewModel init failed: {ex}");
        }
    }

    [RelayCommand]
    private async Task ScanAsync(CancellationToken ct)
    {
        try
        {
            ClearError();
            IsScanning = true;
            StatusText = "Detecting Steam libraries...";
            LastOperationResult = null;

            var libraries = await _deploy.GetSteamLibraryFoldersAsync(ct);
            if (libraries.Count == 0)
            {
                StatusText = "No Steam libraries found";
                IsScanning = false;
                return;
            }

            StatusText = $"Scanning {libraries.Count} library folder(s)...";
            var found = await _deploy.FindUeGamesAsync(libraries, ct);

            Games.Clear();
            foreach (var game in found)
                Games.Add(game);

            if (Games.Count > 0 && File.Exists(SourceDllPath))
            {
                StatusText = "Checking deploy status...";
                await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, ct);
            }

            StatusText = $"Found {Games.Count} UE game(s)";
            OnPropertyChanged(nameof(HasSelection));
        }
        catch (OperationCanceledException)
        {
            StatusText = "Scan cancelled";
        }
        catch (Exception ex)
        {
            StatusText = "Scan failed";
            SetError(ex);
            _log.Error("ProxyDeploy", $"Scan failed: {ex.Message}");
        }
        finally
        {
            IsScanning = false;
        }
    }

    [RelayCommand]
    private async Task RefreshAsync(CancellationToken ct)
    {
        if (Games.Count == 0) return;

        try
        {
            ClearError();
            StatusText = "Refreshing status...";
            LastOperationResult = null;

            // Re-read source DLL version (may have changed)
            SourceDllVersion = File.Exists(SourceDllPath)
                ? _deploy.GetDllVersion(SourceDllPath)
                : null;

            await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, ct);
            StatusText = $"{Games.Count} game(s) — status refreshed";
        }
        catch (Exception ex)
        {
            StatusText = "Refresh failed";
            SetError(ex);
        }
    }

    [RelayCommand]
    private async Task DeploySelectedAsync(CancellationToken ct)
    {
        if (!File.Exists(SourceDllPath))
        {
            SetError($"Source DLL not found: {SourceDllPath}");
            return;
        }

        var selected = Games.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            LastOperationResult = "No games selected";
            return;
        }

        ClearError();
        int ok = 0, fail = 0;

        foreach (var game in selected)
        {
            ct.ThrowIfCancellationRequested();
            StatusText = $"Deploying to {game.Name}...";

            bool success = await _deploy.DeployAsync(SourceDllPath, game, ForceOverwrite, ct);
            if (success) ok++;
            else fail++;
        }

        LastOperationResult = $"Deployed: {ok} success, {fail} failed";
        StatusText = LastOperationResult;
        _log.Info("ProxyDeploy", LastOperationResult);
    }

    [RelayCommand]
    private async Task UndeploySelectedAsync(CancellationToken ct)
    {
        var selected = Games.Where(g => g.IsSelected).ToList();
        if (selected.Count == 0)
        {
            LastOperationResult = "No games selected";
            return;
        }

        ClearError();
        int ok = 0, fail = 0;

        foreach (var game in selected)
        {
            ct.ThrowIfCancellationRequested();
            StatusText = $"Removing from {game.Name}...";

            bool success = await _deploy.UndeployAsync(game, ct);
            if (success) ok++;
            else fail++;
        }

        LastOperationResult = $"Removed: {ok} success, {fail} failed";
        StatusText = LastOperationResult;
        _log.Info("ProxyDeploy", LastOperationResult);
    }

    [RelayCommand]
    private async Task UpdateAllAsync(CancellationToken ct)
    {
        if (!File.Exists(SourceDllPath))
        {
            SetError($"Source DLL not found: {SourceDllPath}");
            return;
        }

        // Update all games that have our outdated DLL
        var outdated = Games.Where(g =>
            g.Status == ProxyDeployStatus.DeployedOutdated
            || g.Status == ProxyDeployStatus.DeployedCurrent).ToList();

        if (outdated.Count == 0)
        {
            LastOperationResult = "No deployed games to update";
            return;
        }

        ClearError();
        int ok = 0, fail = 0;

        foreach (var game in outdated)
        {
            ct.ThrowIfCancellationRequested();
            StatusText = $"Updating {game.Name}...";

            bool success = await _deploy.DeployAsync(SourceDllPath, game, force: true, ct);
            if (success) ok++;
            else fail++;
        }

        LastOperationResult = $"Updated: {ok} success, {fail} failed";
        StatusText = LastOperationResult;
        _log.Info("ProxyDeploy", LastOperationResult);
    }

    // ────────────────────────────────────────────────────────────────
    // Selection helpers
    // ────────────────────────────────────────────────────────────────

    [RelayCommand]
    private void SelectAll()
    {
        foreach (var g in Games) g.IsSelected = true;
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private void UnselectAll()
    {
        foreach (var g in Games) g.IsSelected = false;
        OnPropertyChanged(nameof(HasSelection));
    }

    [RelayCommand]
    private void InvertSelection()
    {
        foreach (var g in Games) g.IsSelected = !g.IsSelected;
        OnPropertyChanged(nameof(HasSelection));
    }

    /// <summary>
    /// Notify that selection changed (called from View).
    /// </summary>
    public void NotifySelectionChanged()
    {
        OnPropertyChanged(nameof(HasSelection));
    }
}
