using System.Collections.ObjectModel;
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

    /// <summary>
    /// Which proxy DLL the user wants to deploy. Bound to the RadioButtons
    /// at the top of the panel. Changing this triggers a status refresh so
    /// the DataGrid reflects the deploy state of the newly-selected DLL.
    /// </summary>
    [ObservableProperty] private ProxyType _selectedProxyType = ProxyType.Version;

    // Two-way bindings for radio button state — Avalonia RadioButtons bind
    // to bool, so we expose convenience properties that mirror SelectedProxyType.
    public bool IsVersionSelected
    {
        get => SelectedProxyType == ProxyType.Version;
        set { if (value) SelectedProxyType = ProxyType.Version; }
    }
    public bool IsDinput8Selected
    {
        get => SelectedProxyType == ProxyType.Dinput8;
        set { if (value) SelectedProxyType = ProxyType.Dinput8; }
    }

    /// <summary>
    /// Detected games. Non-replaceable: items are added/removed in place so that
    /// per-row PropertyChanged notifications from the DataGrid keep working.
    /// Replacing the collection (the previous approach) caused stale visuals
    /// because new ItemsSource swaps don't re-bind row containers reliably when
    /// the underlying items are the same instances.
    /// </summary>
    public ObservableCollection<DetectedGame> Games { get; } = new();

    /// <summary>Whether any games are selected for batch operations.</summary>
    public bool HasSelection => Games.Any(g => g.IsSelected);

    public ProxyDeployViewModel(IProxyDeployService deploy, ILoggingService log)
    {
        _deploy = deploy;
        _log = log;

        UpdateSourceDllInfo();
    }

    /// <summary>
    /// Locate the source DLL for the currently-selected proxy type.
    /// The proxy/ subdirectory next to the UI executable is kept separate
    /// so Windows DLL search order doesn't load our version.dll into the
    /// UI process itself.
    /// </summary>
    private void UpdateSourceDllInfo()
    {
        try
        {
            var exeDir = Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
            var dllName = SelectedProxyType.GetDllName();
            var dllPath = Path.Combine(exeDir, "proxy", dllName);
            SourceDllPath = dllPath;
            SourceDllVersion = File.Exists(dllPath)
                ? _deploy.GetDllVersion(dllPath)
                : null;

            StatusText = File.Exists(dllPath)
                ? $"Source: {dllName} v{SourceDllVersion ?? "?"}"
                : $"Source DLL not found: {dllPath}";
        }
        catch (Exception ex)
        {
            StatusText = $"Init error: {ex.Message}";
            _log.Error("ProxyDeploy", $"ViewModel init failed: {ex}");
        }
    }

    /// <summary>
    /// Triggered by the source-generated SelectedProxyType setter
    /// (from [ObservableProperty]). Re-resolves the source DLL path for
    /// the new proxy type and refreshes deploy status of detected games.
    /// </summary>
    partial void OnSelectedProxyTypeChanged(ProxyType value)
    {
        UpdateSourceDllInfo();
        // Notify radio button mirror properties so XAML stays in sync.
        OnPropertyChanged(nameof(IsVersionSelected));
        OnPropertyChanged(nameof(IsDinput8Selected));

        // If we already have games, re-evaluate their deploy status against
        // the new proxy type. Fire-and-forget — UI doesn't block on toggle.
        if (Games.Count > 0 && File.Exists(SourceDllPath))
        {
            _ = RefreshAfterTypeChangeAsync();
        }
    }

    private async Task RefreshAfterTypeChangeAsync()
    {
        try
        {
            await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType);
        }
        catch (Exception ex)
        {
            _log.Warn("ProxyDeploy", $"Refresh after type change failed: {ex.Message}");
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
            foreach (var g in found) Games.Add(g);

            if (Games.Count > 0 && File.Exists(SourceDllPath))
            {
                StatusText = "Checking deploy status...";
                await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType, ct);
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

            await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType, ct);
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
        if (IsScanning) { LastOperationResult = "Wait for scan to finish"; return; }

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

            bool success = await _deploy.DeployAsync(SourceDllPath, game, SelectedProxyType, ForceOverwrite, ct);
            if (success) ok++;
            else fail++;
        }

        // Refresh status from disk to ensure DataGrid reflects actual state
        await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType, ct);

        LastOperationResult = $"Deployed: {ok} success, {fail} failed";
        StatusText = LastOperationResult;
        _log.Info("ProxyDeploy", LastOperationResult);
    }

    [RelayCommand]
    private async Task UndeploySelectedAsync(CancellationToken ct)
    {
        if (IsScanning) { LastOperationResult = "Wait for scan to finish"; return; }

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

            bool success = await _deploy.UndeployAsync(game, SelectedProxyType, ct);
            if (success) ok++;
            else fail++;
        }

        // Refresh status from disk to ensure DataGrid reflects actual state
        await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType, ct);

        LastOperationResult = $"Removed: {ok} success, {fail} failed";
        StatusText = LastOperationResult;
        _log.Info("ProxyDeploy", LastOperationResult);
    }

    [RelayCommand]
    private async Task UpdateAllAsync(CancellationToken ct)
    {
        if (IsScanning)
        {
            LastOperationResult = "Wait for scan to finish";
            return;
        }

        if (!File.Exists(SourceDllPath))
        {
            SetError($"Source DLL not found: {SourceDllPath}");
            return;
        }

        // Update all games that have our outdated DLL (ignores selection)
        var outdated = Games.Where(g =>
            g.Status == ProxyDeployStatus.DeployedOutdated).ToList();

        if (outdated.Count == 0)
        {
            // Show why nothing happened
            int currentCount = Games.Count(g => g.Status == ProxyDeployStatus.DeployedCurrent);
            if (currentCount > 0)
                LastOperationResult = $"All {currentCount} deployed game(s) already up-to-date";
            else
                LastOperationResult = "No deployed games to update";
            return;
        }

        ClearError();
        int ok = 0, fail = 0;

        foreach (var game in outdated)
        {
            ct.ThrowIfCancellationRequested();
            StatusText = $"Updating {game.Name}...";

            bool success = await _deploy.DeployAsync(SourceDllPath, game, SelectedProxyType, force: true, ct: ct);
            if (success) ok++;
            else fail++;
        }

        // Refresh status from disk to ensure DataGrid reflects actual state
        await _deploy.RefreshDeployStatusAsync(Games, SourceDllPath, SelectedProxyType, ct);

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
